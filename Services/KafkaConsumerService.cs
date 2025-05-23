using CircuitBreakerService.Contracts;
using Confluent.Kafka;


namespace CircuitBreakerService.Services;


public class KafkaConsumerService : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IDistributedCircuitStateStore _stateStore;
    private readonly ILogger<KafkaConsumerService> _logger;
    private const string CircuitId = "consumer-circuit";
    private readonly TimeSpan _circuitBreakDuration = TimeSpan.FromSeconds(30);
    private bool _isPaused;

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
                var state = await _stateStore.GetCircuitStateAsync(CircuitId);
                await ManageCircuitStateAsync(state);

                if (_isPaused)
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                var result = _consumer.Consume(stoppingToken);
                if (result?.Message == null) continue;

                await ProcessMessageAsync(result.Message.Value);
                await _stateStore.ResetFailureCountAsync(CircuitId);
                _consumer.Commit(result);
            }
            catch (ConsumeException ex)
            {
                await HandleConsumeErrorAsync(ex);
            }
        }
    }

    private async Task ManageCircuitStateAsync(CircuitState state)
    {
        switch (state)
        {
            case CircuitState.Open when !_isPaused:
                _logger.LogWarning("Pausing consumption due to open circuit");
                _consumer.Pause(_consumer.Assignment);
                _isPaused = true;
                break;

            case CircuitState.Closed when _isPaused:
                _logger.LogInformation("Resuming consumption");
                _consumer.Resume(_consumer.Assignment);
                _isPaused = false;
                break;
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        // Simulate processing - throw every 3rd message
        if (DateTime.UtcNow.Second % 3 == 0)
            throw new InvalidOperationException("Simulated processing failure");

        await Task.Delay(100);
        _logger.LogInformation($"Processed message: {message}");
    }

    private async Task HandleConsumeErrorAsync(ConsumeException ex)
    {
        _logger.LogError(ex.Error.Reason);
        var failures = await _stateStore.IncrementFailureCountAsync(CircuitId);

        if (failures >= 3)
        {
            await _stateStore.SetCircuitStateAsync(
                CircuitId,
                CircuitState.Open,
                _circuitBreakDuration);
        }
    }

    public override void Dispose()
    {
        _consumer.Close();
        base.Dispose();
    }
}