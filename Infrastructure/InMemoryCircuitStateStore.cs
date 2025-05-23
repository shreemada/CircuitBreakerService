using CircuitBreakerService.Contracts;
using CircuitBreakerService.Models;
using Polly.CircuitBreaker;
using System.Collections.Concurrent;

namespace CircuitBreakerService.Infrastructure;

public class InMemoryCircuitStateStore : IDistributedCircuitStateStore
{
    private readonly ConcurrentDictionary<string, CircuitInfo> _circuitStates = new();
    private readonly ConcurrentDictionary<string, (SemaphoreSlim semaphore, DateTimeOffset expiry)> _locks = new();
    private readonly ILogger<InMemoryCircuitStateStore> _logger;

    public InMemoryCircuitStateStore(ILogger<InMemoryCircuitStateStore> logger)
    {
        _logger = logger;
    }

    public Task<CircuitState> GetCircuitStateAsync(string circuitName)
    {
        var circuitInfo = _circuitStates.GetOrAdd(circuitName, _ => new CircuitInfo
        {
            CircuitName = circuitName,
            State = CircuitState.Closed
        });

        return Task.FromResult(circuitInfo.State);
    }

    public Task SetCircuitStateAsync(string circuitName, CircuitState state)
    {
        _circuitStates.AddOrUpdate(circuitName,
            new CircuitInfo { CircuitName = circuitName, State = state, OpenedAt = DateTimeOffset.UtcNow },
            (key, existing) =>
            {
                existing.State = state;
                if (state == CircuitState.Open)
                    existing.OpenedAt = DateTimeOffset.UtcNow;
                return existing;
            });

        _logger.LogInformation("Circuit {CircuitName} state changed to {State}", circuitName, state);
        return Task.CompletedTask;
    }

    public async Task<bool> TryLockAsync(string circuitName, TimeSpan lockDuration)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = now.Add(lockDuration);

        var lockInfo = _locks.AddOrUpdate(circuitName,
            (new SemaphoreSlim(1, 1), expiry),
            (key, existing) =>
            {
                if (existing.expiry < now)
                {
                    existing.semaphore.Dispose();
                    return (new SemaphoreSlim(1, 1), expiry);
                }
                return existing;
            });

        return await lockInfo.semaphore.WaitAsync(TimeSpan.FromMilliseconds(100));
    }

    public Task ReleaseLockAsync(string circuitName)
    {
        if (_locks.TryGetValue(circuitName, out var lockInfo))
        {
            lockInfo.semaphore.Release();
        }
        return Task.CompletedTask;
    }

    public Task IncrementFailureCountAsync(string circuitName)
    {
        _circuitStates.AddOrUpdate(circuitName,
            new CircuitInfo { CircuitName = circuitName, FailureCount = 1 },
            (key, existing) =>
            {
                existing.FailureCount++;
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task ResetFailureCountAsync(string circuitName)
    {
        _circuitStates.AddOrUpdate(circuitName,
            new CircuitInfo { CircuitName = circuitName, FailureCount = 0 },
            (key, existing) =>
            {
                existing.FailureCount = 0;
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task<int> GetFailureCountAsync(string circuitName)
    {
        var circuitInfo = _circuitStates.GetOrAdd(circuitName, _ => new CircuitInfo
        {
            CircuitName = circuitName
        });

        return Task.FromResult(circuitInfo.FailureCount);
    }

    public Task SetLastFailureTimeAsync(string circuitName, DateTimeOffset timestamp)
    {
        _circuitStates.AddOrUpdate(circuitName,
            new CircuitInfo { CircuitName = circuitName, LastFailureTime = timestamp },
            (key, existing) =>
            {
                existing.LastFailureTime = timestamp;
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLastFailureTimeAsync(string circuitName)
    {
        var circuitInfo = _circuitStates.GetOrAdd(circuitName, _ => new CircuitInfo
        {
            CircuitName = circuitName
        });

        return Task.FromResult(circuitInfo.LastFailureTime);
    }
}