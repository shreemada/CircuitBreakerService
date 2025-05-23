using CircuitBreakerService.Contracts;
using CircuitBreakerService.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Confluent.Kafka;
using Polly.CircuitBreaker;

namespace CircuitBreakerService.Infrastructure
{
    public class KafkaHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<KafkaHealthCheck> _logger;

        public KafkaHealthCheck(IConfiguration configuration, ILogger<KafkaHealthCheck> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var bootstrapServers = _configuration.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092";

                using var adminClient = new AdminClientBuilder(new AdminClientConfig
                {
                    BootstrapServers = bootstrapServers,
                    ApiVersionRequestTimeoutMs = 5000
                }).Build();

                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));

                if (metadata.Brokers.Count == 0)
                {
                    return HealthCheckResult.Unhealthy("No Kafka brokers available");
                }

                var data = new Dictionary<string, object>
                {
                    ["brokers"] = metadata.Brokers.Count,
                    ["topics"] = metadata.Topics.Count
                };

                return HealthCheckResult.Healthy("Kafka is healthy", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kafka health check failed");
                return HealthCheckResult.Unhealthy("Kafka health check failed", ex);
            }
        }
    }

    public class CircuitBreakerHealthCheck : IHealthCheck
    {
        private readonly IDistributedCircuitStateStore _stateStore;
        private readonly ILogger<CircuitBreakerHealthCheck> _logger;

        public CircuitBreakerHealthCheck(
            IDistributedCircuitStateStore stateStore,
            ILogger<CircuitBreakerHealthCheck> logger)
        {
            _stateStore = stateStore;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var circuitNames = new[]
                {
                    "downstream-service",
                    "consumer-1", "consumer-2", "consumer-3", "consumer-4", "consumer-5",
                    "producer-1", "producer-2", "producer-3", "producer-4", "producer-5"
                };

                var circuitStates = new Dictionary<string, object>();
                var openCircuits = 0;
                var totalFailures = 0;

                foreach (var circuitName in circuitNames)
                {
                    try
                    {
                        var state = await _stateStore.GetCircuitStateAsync(circuitName);
                        var failureCount = await _stateStore.GetFailureCountAsync(circuitName);

                        circuitStates[circuitName] = new
                        {
                            state = state.ToString(),
                            failures = failureCount
                        };

                        if (state == CircuitState.Open)
                            openCircuits++;

                        totalFailures += failureCount;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to check circuit breaker {CircuitName}", circuitName);
                        circuitStates[circuitName] = "Error";
                    }
                }

                var data = new Dictionary<string, object>
                {
                    ["circuits"] = circuitStates,
                    ["openCircuits"] = openCircuits,
                    ["totalFailures"] = totalFailures
                };

                if (openCircuits > 0)
                {
                    return HealthCheckResult.Degraded($"{openCircuits} circuit breaker(s) are open", null, data);
                }

                return HealthCheckResult.Healthy("All circuit breakers are healthy", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Circuit breaker health check failed");
                return HealthCheckResult.Unhealthy("Circuit breaker health check failed", ex);
            }
        }
    }
}