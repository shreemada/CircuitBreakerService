using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Models;
using System.Net.Http.Json;

namespace CircuitBreakerService.Services
{
    public class MessageProcessorService : IMessageProcessor
    {
        private readonly IDownstreamService _downstreamService;
        private readonly DistributedCircuitBreaker _circuitBreaker;
        private readonly ILogger<MessageProcessorService> _logger;

        public MessageProcessorService(
            IDownstreamService downstreamService,
            ICircuitBreakerFactory circuitBreakerFactory,
            ILogger<MessageProcessorService> logger,
            string circuitName = "downstream-service")
        {
            _downstreamService = downstreamService;
            _circuitBreaker = circuitBreakerFactory.CreateCircuitBreaker(circuitName);
            _logger = logger;
        }

        public async Task<ProcessingResult> ProcessMessageAsync(KafkaMessage message, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Processing message from topic {Topic}, partition {Partition}, offset {Offset}",
                    message.Topic, message.Partition, message.Offset);

                var result = await _circuitBreaker.ExecuteAsync(async () =>
                {
                    return await _downstreamService.SendToDownstreamAsync(message, cancellationToken);
                });

                _logger.LogDebug("Successfully processed message from topic {Topic}, partition {Partition}, offset {Offset}",
                    message.Topic, message.Partition, message.Offset);

                return result;
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)// .CircuitBreakerOpenException)
            {
                _logger.LogWarning("Circuit breaker is open, skipping message processing for topic {Topic}, partition {Partition}, offset {Offset}",
                    message.Topic, message.Partition, message.Offset);

                return ProcessingResult.Failure("Circuit breaker is open");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from topic {Topic}, partition {Partition}, offset {Offset}",
                    message.Topic, message.Partition, message.Offset);

                return ProcessingResult.Failure(ex.Message, ex);
            }
        }
    }

    public class DownstreamService : IDownstreamService
    {
        private readonly ILogger<DownstreamService> _logger;
        private readonly HttpClient _httpClient;

        public DownstreamService(ILogger<DownstreamService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<ProcessingResult> SendToDownstreamAsync(KafkaMessage message, CancellationToken cancellationToken)
        {
            try
            {
                // Simulate downstream service call
                // Replace this with your actual downstream service logic
                var response = await _httpClient.PostAsJsonAsync("/api/process", message, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Successfully sent message to downstream service");
                    return ProcessingResult.Success();
                }
                else
                {
                    var error = $"Downstream service returned {response.StatusCode}";
                    _logger.LogWarning(error);
                    return ProcessingResult.Failure(error);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error sending message to downstream service");
                return ProcessingResult.Failure(ex.Message, ex);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "Timeout sending message to downstream service");
                return ProcessingResult.Failure("Timeout", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending message to downstream service");
                return ProcessingResult.Failure(ex.Message, ex);
            }
        }
    }
}