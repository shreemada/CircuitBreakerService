using CircuitBreakerService.Contracts;
using Polly.CircuitBreaker;

namespace CircuitBreakerService.Services;

public class CircuitBreakerMonitoringService : BackgroundService
{
    private readonly IDistributedCircuitStateStore _stateStore;
    private readonly ILogger<CircuitBreakerMonitoringService> _logger;
    private readonly TimeSpan _monitoringInterval;

    public CircuitBreakerMonitoringService(
        IDistributedCircuitStateStore stateStore,
        ILogger<CircuitBreakerMonitoringService> logger,
        IConfiguration configuration)
    {
        _stateStore = stateStore;
        _logger = logger;
        _monitoringInterval = TimeSpan.FromSeconds(
            configuration.GetValue<int>("CircuitBreaker:MonitoringIntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Circuit breaker monitoring service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorCircuitBreakers();
                await Task.Delay(_monitoringInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in circuit breaker monitoring");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Circuit breaker monitoring service stopped");
    }

    private async Task MonitorCircuitBreakers()
    {
        // In a real implementation, you might maintain a registry of all circuit breaker names
        // For this example, we'll check some common circuit breaker names
        var circuitNames = new[]
        {
            "downstream-service",
            "consumer-1", "consumer-2", "consumer-3", "consumer-4", "consumer-5",
            "producer-1", "producer-2", "producer-3", "producer-4", "producer-5"
        };

        foreach (var circuitName in circuitNames)
        {
            try
            {
                var state = await _stateStore.GetCircuitStateAsync(circuitName);
                var failureCount = await _stateStore.GetFailureCountAsync(circuitName);
                var lastFailureTime = await _stateStore.GetLastFailureTimeAsync(circuitName);

                if (state != CircuitState.Closed || failureCount > 0)
                {
                    _logger.LogInformation(
                        "Circuit breaker {CircuitName}: State={State}, Failures={FailureCount}, LastFailure={LastFailureTime}",
                        circuitName, state, failureCount, lastFailureTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "None");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to monitor circuit breaker {CircuitName}", circuitName);
            }
        }
    }
}