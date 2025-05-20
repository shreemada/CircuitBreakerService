using CircuitBreakerService;
using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Services;
using Confluent.Kafka;
using StackExchange.Redis;

// Program.cs
var builder = Host.CreateApplicationBuilder(args);

// Redis Configuration
var redis = ConnectionMultiplexer.Connect("localhost");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddSingleton<IDistributedCircuitStateStore, RedisCircuitStateStore>();

// Kafka Consumers
builder.Services.AddSingleton<IConsumer<Ignore, string>>(sp =>
    new ConsumerBuilder<Ignore, string>(new ConsumerConfig
    {
        BootstrapServers = "localhost:9092",
        GroupId = $"consumer-group-{Guid.NewGuid()}",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false
    }).Build());

builder.Services.AddHostedService<KafkaConsumerService>();

// Kafka Producers
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = "localhost:9092",
        MessageSendMaxRetries = 3,
        EnableIdempotence = true
    }).Build());

builder.Services.AddSingleton<KafkaProducerService>();

var host = builder.Build();
host.Run();
