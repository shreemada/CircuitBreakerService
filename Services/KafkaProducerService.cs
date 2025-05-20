using CircuitBreakerService.Contracts;
using Confluent.Kafka;
using Polly;
using Polly.CircuitBreaker;
using CircuitState = CircuitBreakerService.Contracts.CircuitState;

namespace CircuitBreakerService.Services;
public class KafkaProducerService
{
    private readonly IProducer<string, string> _producer;
    private readonly IDistributedCircuitStateStore _stateStore;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    public KafkaProducerService(
        IProducer<string, string> producer,
        IDistributedCircuitStateStore stateStore,
        ILogger<KafkaProducerService> logger)
    {
        _producer = producer;
        _stateStore = stateStore;
        _logger = logger;

        _circuitBreaker = Policy
            .Handle<ProduceException<string, string>>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) => OnCircuitBreakAsync(),
                onReset: () => OnCircuitResetAsync());
    }

    public async Task ProduceAsync(string topic, string message)
    {
        await _circuitBreaker.ExecuteAsync(async () =>
        {
            var result = await _producer.ProduceAsync(
                topic,
                new Message<string, string> { Value = message });

            if (result.Status == PersistenceStatus.NotPersisted)
            {
                throw new ProduceException<string, string>(
                    new Error(ErrorCode.Local_Fail, "Delivery failed"),
                    result,
                    new KafkaException(ErrorCode.Local_Fail));
            }

            return result;
        });
    }

    private async Task OnCircuitBreakAsync()
    {
        await _stateStore.SetCircuitStateAsync("producer-circuit",
            CircuitState.Open,
            TimeSpan.FromSeconds(30));
    }

    private async Task OnCircuitResetAsync()
    {
        await _stateStore.SetCircuitStateAsync("producer-circuit", CircuitState.Closed);
    }
}