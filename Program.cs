using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Services;
using Confluent.Kafka;

var builder = Host.CreateApplicationBuilder(args);

// Add in-memory state store
builder.Services.AddSingleton<IDistributedCircuitStateStore, InMemoryCircuitStateStore>();

// Add Kafka consumer (hosted service)
builder.Services.AddSingleton<IConsumer<Ignore, string>>(sp =>
    new ConsumerBuilder<Ignore, string>(new ConsumerConfig
    {
        BootstrapServers = "localhost:9092",
        GroupId = "consumer-group",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false
    }).Build());

builder.Services.AddHostedService<KafkaConsumerService>();

// Add Kafka producer
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = "localhost:9092"
    }).Build());

builder.Services.AddSingleton<KafkaProducerService>();

var host = builder.Build();
host.Run();
