using CircuitBreakerService.Contracts;
using Confluent.Kafka;
using Polly;
using Polly.CircuitBreaker;
using CircuitState = CircuitBreakerService.Contracts.CircuitState;

namespace CircuitBreakerService.Services;
public class KafkaProducerService : BackgroundService
{
    private readonly IProducer<string, string> _producer;
    private readonly IDistributedCircuitStateStore _stateStore;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly AsyncCircuitBreakerPolicy<DeliveryResult<string, string>> _circuitBreaker;
    private const string CircuitId = "producer-circuit";
    private readonly TimeSpan _circuitBreakDuration = TimeSpan.FromSeconds(30);

    public KafkaProducerService(
        IProducer<string, string> producer,
        IDistributedCircuitStateStore stateStore,
        ILogger<KafkaProducerService> logger)
    {
        _producer = producer;
        _stateStore = stateStore;
        _logger = logger;

        _circuitBreaker = Policy<DeliveryResult<string, string>>
            .Handle<ProduceException<string, string>>()
            .OrResult(r => r.Status != PersistenceStatus.Persisted)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 3,
                durationOfBreak: _circuitBreakDuration,
                onBreak: async (result, duration) => await OnCircuitBreakAsync(result, duration),
                onReset: async () => await OnCircuitResetAsync());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var messageCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _circuitBreaker.ExecuteAsync(async () =>
                {
                    var message = new Message<string, string>
                    {
                        Key = messageCount.ToString(),
                        Value = $"Message-{messageCount++}"
                    };

                    var result = await _producer.ProduceAsync("target-topic", message, stoppingToken);
                    HandleDeliveryResult(result);
                    return result;
                });

                await Task.Delay(1000, stoppingToken);
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Producer circuit open. Waiting to resume...");
                await Task.Delay(_circuitBreakDuration, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical producer error");
            }
        }
    }

    private void HandleDeliveryResult(DeliveryResult<string, string> result)
    {
        if (result.Status == PersistenceStatus.Persisted)
        {
            _logger.LogInformation($"Delivered to {result.TopicPartitionOffset}");
            return;
        }

        _logger.LogError("Message delivery failed.");
        throw new ProduceException<string, string>(new Error(ErrorCode.BrokerNotAvailable,"Message delivery failed."), result);
    }

    private async Task OnCircuitBreakAsync(
        DelegateResult<DeliveryResult<string, string>> result,
        TimeSpan duration)
    {
        await _stateStore.SetCircuitStateAsync(CircuitId, CircuitState.Open, duration);
        _logger.LogWarning($"Circuit opened: {result.Result}");
    }

    private async Task OnCircuitResetAsync()
    {
        await _stateStore.SetCircuitStateAsync(CircuitId, CircuitState.Closed);
        _logger.LogInformation("Producer circuit reset");
    }

    public override void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        base.Dispose();
    }
}