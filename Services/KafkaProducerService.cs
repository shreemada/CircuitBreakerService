using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Models;
using Confluent.Kafka;
using System.Text.Json;

namespace CircuitBreakerService.Services;

public interface IKafkaProducerService
{
    Task<bool> PublishMessageAsync(string topic, string key, object message, CancellationToken cancellationToken = default);
    Task<bool> PublishMessageAsync(string topic, string message, CancellationToken cancellationToken = default);
}

public class KafkaProducerService : BackgroundService, IKafkaProducerService
{
    private readonly IProducer<string, string> _producer;
    private readonly DistributedCircuitBreaker _circuitBreaker;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly string _producerName;
    private readonly SemaphoreSlim _producerSemaphore;

    public KafkaProducerService(
        IProducer<string, string> producer,
        ICircuitBreakerFactory circuitBreakerFactory,
        ILogger<KafkaProducerService> logger)
    {
        _producer = producer;
        _logger = logger;
        _producerName = $"producer-{Environment.MachineName}-{Guid.NewGuid():N}";
        _circuitBreaker = circuitBreakerFactory.CreateCircuitBreaker($"producer-{_producerName}");
        _producerSemaphore = new SemaphoreSlim(10, 10); // Limit concurrent operations
    }

    public async Task<bool> PublishMessageAsync(string topic, string key, object message, CancellationToken cancellationToken = default)
    {
        var messageJson = JsonSerializer.Serialize(message);
        return await PublishMessageInternalAsync(topic, key, messageJson, cancellationToken);
    }

    public async Task<bool> PublishMessageAsync(string topic, string message, CancellationToken cancellationToken = default)
    {
        return await PublishMessageInternalAsync(topic, null, message, cancellationToken);
    }

    private async Task<bool> PublishMessageInternalAsync(string topic, string? key, string message, CancellationToken cancellationToken = default)
    {
        await _producerSemaphore.WaitAsync(cancellationToken);

        try
        {
            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                var kafkaMessage = new Message<string, string>
                {
                    Key = key,
                    Value = message,
                    Timestamp = new Timestamp(DateTimeOffset.UtcNow)
                };

                _logger.LogDebug("Publishing message to topic {Topic} with key {Key}", topic, key ?? "null");

                var deliveryResult = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);

                HandleDeliveryResult(deliveryResult);

                _logger.LogDebug("Successfully published message to {Topic}:{Partition}:{Offset}",
                    deliveryResult.Topic, deliveryResult.Partition.Value, deliveryResult.Offset.Value);

                return true;
            });
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException) //CircuitBreakerOpenException)
        {
            _logger.LogWarning("Circuit breaker is open, message publication to topic {Topic} failed", topic);
            return false;
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish message to topic {Topic}: {Error}", topic, ex.Error.Reason);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error publishing message to topic {Topic}", topic);
            return false;
        }
        finally
        {
            _producerSemaphore.Release();
        }
    }

    private void HandleDeliveryResult(DeliveryResult<string, string> deliveryResult)
    {
        if (deliveryResult.Status == PersistenceStatus.NotPersisted)
        {
            throw new InvalidOperationException($"Message was not persisted to topic {deliveryResult.Topic}");
        }

        if (deliveryResult.Status == PersistenceStatus.PossiblyPersisted)
        {
            _logger.LogWarning("Message to topic {Topic} may not have been persisted", deliveryResult.Topic);
        }

        // Log successful delivery
        _logger.LogDebug("Message delivered to {Topic}:{Partition}:{Offset} with status {Status}",
            deliveryResult.Topic,
            deliveryResult.Partition.Value,
            deliveryResult.Offset.Value,
            deliveryResult.Status);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kafka producer service {ProducerName} started", _producerName);

        try
        {
            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                // Optionally monitor circuit breaker state
                var circuitState = await _circuitBreaker.GetStateAsync();
                _logger.LogDebug("Producer {ProducerName} circuit breaker state: {State}", _producerName, circuitState);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Kafka producer service {ProducerName} stopping due to cancellation", _producerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Kafka producer service {ProducerName}", _producerName);
        }
        finally
        {
            _producer.Flush(TimeSpan.FromSeconds(10));
            _producer.Dispose();
            _producerSemaphore.Dispose();
            _logger.LogInformation("Kafka producer service {ProducerName} stopped", _producerName);
        }
    }

    public override void Dispose()
    {
        _producer?.Dispose();
        _producerSemaphore?.Dispose();
        base.Dispose();
    }
}