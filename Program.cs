using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Models;
using CircuitBreakerService.Services;
using Confluent.Kafka;

var builder = Host.CreateApplicationBuilder(args);

// Circuit Breaker Configuration
builder.Services.AddSingleton<CircuitBreakerConfiguration>(provider =>
{
    var config = provider.GetService<IConfiguration>();
    return new CircuitBreakerConfiguration
    {
        FailureThreshold = config?.GetValue<int>("CircuitBreaker:FailureThreshold") ?? 5,
        DurationOfBreak = TimeSpan.FromSeconds(config?.GetValue<int>("CircuitBreaker:DurationOfBreakSeconds") ?? 30),
        SamplingDuration = TimeSpan.FromMinutes(config?.GetValue<int>("CircuitBreaker:SamplingDurationMinutes") ?? 1),
        MinimumThroughput = config?.GetValue<int>("CircuitBreaker:MinimumThroughput") ?? 3
    };
});

// Circuit Breaker State Management
builder.Services.AddSingleton<IDistributedCircuitStateStore, InMemoryCircuitStateStore>();
builder.Services.AddSingleton<ICircuitBreakerFactory, CircuitBreakerFactory>();

// HTTP Client for downstream service
builder.Services.AddHttpClient<IDownstreamService, DownstreamService>(client =>
{
    var config = builder.Configuration;
    client.BaseAddress = new Uri(config.GetValue<string>("DownstreamService:BaseUrl") ?? "http://localhost:8080");
    client.Timeout = TimeSpan.FromSeconds(config.GetValue<int>("DownstreamService:TimeoutSeconds"));
});

// Message Processing Services
builder.Services.AddTransient<IMessageProcessor, MessageProcessorService>();

// Kafka Consumer Configuration - Create multiple consumers (5 drainers)
for (int i = 0; i < 5; i++)
{
    var consumerId = i;
    builder.Services.AddSingleton<IConsumer<Ignore, string>>(provider =>
    {
        var config = provider.GetService<IConfiguration>();
        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = config?.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092",
            GroupId = $"circuit-breaker-group-{consumerId}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            SessionTimeoutMs = 30000,
            HeartbeatIntervalMs = 10000,
            MaxPollIntervalMs = 300000
        };

        return new ConsumerBuilder<Ignore, string>(kafkaConfig)
            .SetErrorHandler((_, e) =>
            {
                var logger = provider.GetService<ILogger<Program>>();
                logger?.LogError("Kafka consumer error: {Error}", e.Reason);
            })
            .Build();
    });

    builder.Services.AddHostedService<KafkaConsumerService>();
}

// Kafka Producer Configuration - Create multiple producers (5 producers)
for (int i = 0; i < 5; i++)
{
    builder.Services.AddSingleton<IProducer<string, string>>(provider =>
    {
        var config = provider.GetService<IConfiguration>();
        var kafkaConfig = new ProducerConfig
        {
            BootstrapServers = config?.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092",
            MessageSendMaxRetries = 3,
            EnableIdempotence = true,
            Acks = Acks.All,
            RequestTimeoutMs = 30000,
            LingerMs = 5
        };

        return new ProducerBuilder<string, string>(kafkaConfig)
            .SetErrorHandler((_, e) =>
            {
                var logger = provider.GetService<ILogger<Program>>();
                logger?.LogError("Kafka producer error: {Error}", e.Reason);
            })
            .Build();
    });

    builder.Services.AddHostedService<KafkaProducerService>();
}

// Register the producer service interface
builder.Services.AddSingleton<IKafkaProducerService>(provider =>
    provider.GetServices<KafkaProducerService>().First());

// Circuit Breaker Monitoring Service
builder.Services.AddHostedService<CircuitBreakerMonitoringService>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<KafkaHealthCheck>("kafka")
    .AddCheck<CircuitBreakerHealthCheck>("circuit-breaker");

var host = builder.Build();

// Log startup information
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting Circuit Breaker Service with 5 consumers and 5 producers");
logger.LogInformation("Kafka Bootstrap Servers: {BootstrapServers}",
    host.Services.GetRequiredService<IConfiguration>().GetValue<string>("Kafka:BootstrapServers"));

host.Run();