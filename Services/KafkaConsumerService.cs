using CircuitBreakerService.Contracts;
using Confluent.Kafka;

namespace CircuitBreakerService.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IDistributedCircuitStateStore _stateStore;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly string _circuitId = "downstream-service";

    public KafkaConsumerService(
        IConsumer<Ignore, string> consumer,
        IDistributedCircuitStateStore stateStore,
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
                var state = await _stateStore.GetCircuitStateAsync(_circuitId);

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
            // Business logic here
            //await ProcessMessageAsync(result.Message.Value);
            await _stateStore.ResetFailureCountAsync(_circuitId);
            _consumer.Commit(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed");
            var failures = await _stateStore.IncrementFailureCountAsync(_circuitId);

            if (failures >= 5)
            {
                await _stateStore.SetCircuitStateAsync(
                    _circuitId,
                    CircuitState.Open,
                    TimeSpan.FromSeconds(30));
            }
            throw;
        }
    }

    private async Task HandleOpenCircuitAsync(CancellationToken ct)
    {
        _logger.LogWarning("Circuit is open. Pausing consumption...");
        _consumer.Pause(_consumer.Assignment);

        if (await _stateStore.AcquireProbeLeadershipAsync(_circuitId, Environment.MachineName))
        {
            _logger.LogInformation("Acquired leadership for circuit probe");
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            await _stateStore.SetCircuitStateAsync(_circuitId, CircuitState.HalfOpen);
        }

        _consumer.Resume(_consumer.Assignment);
    }
}