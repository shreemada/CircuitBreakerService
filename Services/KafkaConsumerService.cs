using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Models;
using Confluent.Kafka;

namespace CircuitBreakerService.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IMessageProcessor _messageProcessor;
    private readonly DistributedCircuitBreaker _circuitBreaker;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly string _topic;
    private readonly string _consumerName;

    public KafkaConsumerService(
        IConsumer<Ignore, string> consumer,
        IMessageProcessor messageProcessor,
        ICircuitBreakerFactory circuitBreakerFactory,
        ILogger<KafkaConsumerService> logger,
        IConfiguration configuration)
    {
        _consumer = consumer;
        _messageProcessor = messageProcessor;
        _logger = logger;
        _topic = configuration.GetValue<string>("Kafka:ConsumerTopic") ?? "default-topic";
        _consumerName = $"consumer-{Environment.MachineName}-{Guid.NewGuid():N}";
        _circuitBreaker = circuitBreakerFactory.CreateCircuitBreaker($"consumer-{_consumerName}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(_topic);
        _logger.LogInformation("Kafka consumer {ConsumerName} started, subscribed to topic {Topic}", _consumerName, _topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ConsumeMessages(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Kafka consumer {ConsumerName} stopping due to cancellation", _consumerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Kafka consumer {ConsumerName}", _consumerName);
        }
        finally
        {
            _consumer.Close();
            _consumer.Dispose();
            _logger.LogInformation("Kafka consumer {ConsumerName} stopped", _consumerName);
        }
    }

    private async Task ConsumeMessages(CancellationToken stoppingToken)
    {
        try
        {
            // Check if circuit breaker is open before consuming
            if (await _circuitBreaker.IsCircuitOpenAsync())
            {
                _logger.LogDebug("Circuit breaker is open, pausing consumption for {ConsumerName}", _consumerName);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                return;
            }

            var consumeResult = _consumer.Consume(TimeSpan.FromSeconds(1));

            if (consumeResult?.Message != null)
            {
                await ProcessMessage(consumeResult, stoppingToken);
            }
        }
        catch (ConsumeException ex)
        {
            _logger.LogError(ex, "Error consuming message in {ConsumerName}: {Error}", _consumerName, ex.Error.Reason);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in consumer {ConsumerName}", _consumerName);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task ProcessMessage(ConsumeResult<Ignore, string> consumeResult, CancellationToken stoppingToken)
    {
        var kafkaMessage = new KafkaMessage
        {
            Key = consumeResult.Message.Key?.ToString() ?? string.Empty,
            Value = consumeResult.Message.Value ?? string.Empty,
            Topic = consumeResult.Topic,
            Partition = consumeResult.Partition.Value,
            Offset = consumeResult.Offset.Value,
            Timestamp = consumeResult.Message.Timestamp.UtcDateTime
        };

        _logger.LogDebug("Processing message from {Topic}:{Partition}:{Offset}",
            kafkaMessage.Topic, kafkaMessage.Partition, kafkaMessage.Offset);

        try
        {
            var result = await _circuitBreaker.ExecuteAsync(async () =>
            {
                var processingResult = await _messageProcessor.ProcessMessageAsync(kafkaMessage, stoppingToken);

                if (!processingResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Message processing failed: {processingResult.ErrorMessage}");
                }

                return processingResult;
            });

            // Commit offset only on successful processing
            _consumer.Commit(consumeResult);
            _logger.LogDebug("Successfully processed and committed message from {Topic}:{Partition}:{Offset}",
                kafkaMessage.Topic, kafkaMessage.Partition, kafkaMessage.Offset);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning("Circuit breaker is open, will retry message from {Topic}:{Partition}:{Offset} later",
                kafkaMessage.Topic, kafkaMessage.Partition, kafkaMessage.Offset);

            // Don't commit offset when circuit breaker is open
            // Message will be reprocessed when circuit closes
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message from {Topic}:{Partition}:{Offset}",
                kafkaMessage.Topic, kafkaMessage.Partition, kafkaMessage.Offset);

            // Don't commit offset on processing failure
            // Message will be reprocessed
        }
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}