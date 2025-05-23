using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Services;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Polly;

var builder = Host.CreateApplicationBuilder(args);

// Circuit Breaker State Management
builder.Services.AddSingleton<IDistributedCircuitStateStore, InMemoryCircuitStateStore>();

// Kafka Consumer Configuration
builder.Services.AddSingleton<IConsumer<Ignore, string>>(_ =>
    new ConsumerBuilder<Ignore, string>(new ConsumerConfig
    {
        BootstrapServers = "localhost:9092",
        GroupId = "circuit-breaker-group",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false
    }).Build());

builder.Services.AddHostedService<KafkaConsumerService>();

// Kafka Producer Configuration
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = "localhost:9092",
        MessageSendMaxRetries = 3,
        EnableIdempotence = true
    }).Build());

builder.Services.AddHostedService<KafkaProducerService>();

var host = builder.Build();
host.Run();