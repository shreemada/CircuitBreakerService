using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using Confluent.Kafka;

namespace CircuitBreakerService.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IDistributedCircuitStateStore _stateStore; // <-- Interface dependency
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly TimeSpan _circuitBreakDuration = TimeSpan.FromSeconds(30);
    private const string CircuitId = "downstream-service";

    public KafkaConsumerService(
        IConsumer<Ignore, string> consumer,
        IDistributedCircuitStateStore stateStore, // Correct interface
        ILogger<KafkaConsumerService> logger)
    {
        _consumer = consumer;
        _stateStore = stateStore;
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe("target-topic");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var state = await _stateStore.GetCircuitStateAsync(CircuitId);

                if (state == CircuitState.Open)
                {
                    await HandleOpenCircuitAsync(stoppingToken);
                    continue;
                }

                var result = _consumer.Consume(stoppingToken);
                await ProcessMessageWithCircuitBreakerAsync(result, stoppingToken);
            }
            catch (ConsumeException e)
            {
                _logger.LogError(e, "Consume error");
            }
        }
    }

    private async Task ProcessMessageWithCircuitBreakerAsync(
        ConsumeResult<Ignore, string> result,
        CancellationToken ct)
    {
        try
        {
            await ProcessMessageAsync(result.Message.Value);
            await _stateStore.ResetFailureCountAsync(CircuitId);
            _consumer.Commit(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed");
            var failures = await _stateStore.IncrementFailureCountAsync(CircuitId);

            if (failures >= 5)
            {
                await _stateStore.SetCircuitStateAsync(
                    CircuitId,
                    CircuitState.Open,
                    _circuitBreakDuration);
            }
            throw;
        }
    }

    private async Task HandleOpenCircuitAsync(CancellationToken ct)
    {
        _logger.LogWarning("Circuit is open. Pausing consumption...");
        await Task.Delay(_circuitBreakDuration, ct);
        await _stateStore.SetCircuitStateAsync(CircuitId, CircuitState.HalfOpen);
    }

    private async Task ProcessMessageAsync(string message)
    {
        // Your message processing logic
        await Task.Delay(100); // Simulate work
    }
}