using CircuitBreakerService.Contracts;
using CircuitBreakerService.Models;
using Polly;
using Polly.CircuitBreaker;

namespace CircuitBreakerService.Infrastructure;

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}

public class DistributedCircuitBreaker
{
    private readonly IDistributedCircuitStateStore _stateStore;
    private readonly CircuitBreakerConfiguration _configuration;
    private readonly ILogger<DistributedCircuitBreaker> _logger;
    private readonly string _circuitName;

    public DistributedCircuitBreaker(
        string circuitName,
        IDistributedCircuitStateStore stateStore,
        CircuitBreakerConfiguration configuration,
        ILogger<DistributedCircuitBreaker> logger)
    {
        _circuitName = circuitName;
        _stateStore = stateStore;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation)
    {
        var state = await _stateStore.GetCircuitStateAsync(_circuitName);

        switch (state)
        {
            case CircuitState.Open:
                if (await ShouldAttemptReset())
                {
                    return await AttemptReset(operation);
                }
                throw new CircuitBreakerOpenException($"Circuit breaker {_circuitName} is open");

            case CircuitState.HalfOpen:
                return await ExecuteInHalfOpenState(operation);

            case CircuitState.Closed:
            default:
                return await ExecuteInClosedState(operation);
        }
    }

    private async Task<bool> ShouldAttemptReset()
    {
        var lastFailureTime = await _stateStore.GetLastFailureTimeAsync(_circuitName);
        if (!lastFailureTime.HasValue)
            return true;

        return DateTimeOffset.UtcNow - lastFailureTime.Value >= _configuration.DurationOfBreak;
    }

    private async Task<TResult> AttemptReset<TResult>(Func<Task<TResult>> operation)
    {
        if (await _stateStore.TryLockAsync(_circuitName, TimeSpan.FromSeconds(30)))
        {
            try
            {
                await _stateStore.SetCircuitStateAsync(_circuitName, CircuitState.HalfOpen);
                return await ExecuteInHalfOpenState(operation);
            }
            finally
            {
                await _stateStore.ReleaseLockAsync(_circuitName);
            }
        }

        throw new CircuitBreakerOpenException($"Circuit breaker {_circuitName} is open");
    }

    private async Task<TResult> ExecuteInHalfOpenState<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            var result = await operation();
            await OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            await OnFailure(ex);
            throw;
        }
    }

    private async Task<TResult> ExecuteInClosedState<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            var result = await operation();
            await OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            await OnFailure(ex);
            throw;
        }
    }

    private async Task OnSuccess()
    {
        var currentState = await _stateStore.GetCircuitStateAsync(_circuitName);

        if (currentState == CircuitState.HalfOpen)
        {
            await _stateStore.SetCircuitStateAsync(_circuitName, CircuitState.Closed);
            await _stateStore.ResetFailureCountAsync(_circuitName);
            _logger.LogInformation("Circuit breaker {CircuitName} reset to closed state", _circuitName);
        }
    }

    private async Task OnFailure(Exception exception)
    {
        await _stateStore.IncrementFailureCountAsync(_circuitName);
        await _stateStore.SetLastFailureTimeAsync(_circuitName, DateTimeOffset.UtcNow);

        var failureCount = await _stateStore.GetFailureCountAsync(_circuitName);
        var currentState = await _stateStore.GetCircuitStateAsync(_circuitName);

        _logger.LogWarning("Circuit breaker {CircuitName} recorded failure #{FailureCount}: {Exception}",
            _circuitName, failureCount, exception.Message);

        if (currentState == CircuitState.HalfOpen)
        {
            await _stateStore.SetCircuitStateAsync(_circuitName, CircuitState.Open);
            _logger.LogWarning("Circuit breaker {CircuitName} opened due to failure in half-open state", _circuitName);
        }
        else if (failureCount >= _configuration.FailureThreshold)
        {
            await _stateStore.SetCircuitStateAsync(_circuitName, CircuitState.Open);
            _logger.LogWarning("Circuit breaker {CircuitName} opened due to {FailureCount} failures",
                _circuitName, failureCount);
        }
    }

    public async Task<CircuitState> GetStateAsync()
    {
        return await _stateStore.GetCircuitStateAsync(_circuitName);
    }

    public async Task<bool> IsCircuitOpenAsync()
    {
        var state = await GetStateAsync();
        return state == CircuitState.Open;
    }
}