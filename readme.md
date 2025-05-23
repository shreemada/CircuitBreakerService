# Circuit Breaker Service for Kafka Consumers and Producers

This .NET Core application implements a distributed circuit breaker pattern for Kafka consumers and producers using the Polly library, following SOLID principles and design patterns similar to the Polly.Contrib.AzureFunctions.CircuitBreaker implementation.

## Features

- **5 Kafka Consumers (Drainers)**: Each consumer operates independently with its own circuit breaker
- **5 Kafka Producers**: Each producer has circuit breaker protection for publishing messages
- **Distributed Circuit Breaker State Management**: Centralized state storage for all circuit breakers
- **Automatic Pause/Resume**: Consumers automatically pause when circuit breakers open and resume when they close
- **Health Checks**: Built-in health monitoring for Kafka connectivity and circuit breaker states
- **Comprehensive Logging**: Detailed logging for monitoring and debugging

## Architecture

### Key Components

1. **IDistributedCircuitStateStore**: Interface for managing circuit breaker states across the application
2. **DistributedCircuitBreaker**: Core circuit breaker implementation with state management
3. **CircuitBreakerFactory**: Factory for creating circuit breaker instances
4. **KafkaConsumerService**: Background service that consumes messages with circuit breaker protection
5. **KafkaProducerService**: Service for publishing messages with circuit breaker protection
6. **MessageProcessorService**: Processes messages with downstream service calls protected by circuit breakers

### Circuit Breaker States

- **Closed**: Normal operation, requests are allowed through
- **Open**: Circuit breaker is triggered, requests are blocked
- **Half-Open**: Testing state to check if the downstream service has recovered

## Configuration

### appsettings.json

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ConsumerTopic": "input-topic",
    "ProducerTopic": "output-topic"
  },
  "DownstreamService": {
    "BaseUrl": "http://localhost:8080",
    "TimeoutSeconds": 30
  },
  "CircuitBreaker": {
    "FailureThreshold": 5,
    "DurationOfBreakSeconds": 30,
    "SamplingDurationMinutes": 1,
    "MinimumThroughput": 3,
    "MonitoringIntervalSeconds": 30
  }
}
```

### Configuration Parameters

- **FailureThreshold**: Number of consecutive failures before opening the circuit
- **DurationOfBreakSeconds**: How long the circuit stays open before attempting to close
- **SamplingDurationMinutes**: Time window for measuring failures
- **MinimumThroughput**: Minimum number of requests needed before circuit breaker activates
- **MonitoringIntervalSeconds**: Interval for circuit breaker status monitoring

## How It Works

### Consumer Circuit Breaker Flow

1. **Message Consumption**: Each consumer checks its circuit breaker state before processing messages
2. **Circuit Open**: When open, consumers pause message consumption and wait for the circuit to close
3. **Message Processing**: Messages are processed through the circuit breaker with downstream service calls
4. **Failure Handling**: Failures increment the failure count and may trigger circuit opening
5. **Recovery**: When the circuit closes, consumers resume normal operation

### Producer Circuit Breaker Flow

1. **Message Publishing**: Each publish operation is wrapped in a circuit breaker
2. **Delivery Confirmation**: Success/failure is determined by Kafka delivery results
3. **Failure Handling**: Failed deliveries trigger circuit breaker failure counting
4. **Retry Logic**: Built-in Kafka retry mechanisms work alongside circuit breaker protection

### Circuit State Management

- **Distributed State**: All circuit breaker states are stored centrally
- **Thread-Safe Operations**: Concurrent access to circuit states is handled safely
- **Automatic Transitions**: Circuits automatically transition between states based on failure patterns
- **Monitoring**: Continuous monitoring of all circuit breaker states

## Usage Examples

### Publishing Messages

```csharp
// Inject IKafkaProducerService
var success = await producerService.PublishMessageAsync("my-topic", "message-key", messageObject);
if (!success)
{
    // Handle circuit breaker open or other failures
}
```

### Custom Message Processing

```csharp
// Implement IMessageProcessor for custom processing logic
public class CustomMessageProcessor : IMessageProcessor
{
    public async Task<ProcessingResult> ProcessMessageAsync(KafkaMessage message, CancellationToken cancellationToken)
    {
        // Your custom processing logic here
        return ProcessingResult.Success();
    }
}
```

## Monitoring and Health Checks

The application includes built-in health checks accessible at `/health`:

- **Kafka Health Check**: Monitors Kafka broker connectivity
- **Circuit Breaker Health Check**: Reports status of all circuit breakers

### Health Check Responses

- **Healthy**: All systems operational
- **Degraded**: Some circuit breakers are open but system is functional
- **Unhealthy**: Critical failures detected

## Logging

The application provides comprehensive logging:

- **Consumer Activity**: Message consumption, processing, and circuit breaker state changes
- **Producer Activity**: Message publishing attempts and delivery results
- **Circuit Breaker Events**: State transitions, failure counts, and recovery attempts
- **Health Monitoring**: Periodic status reports and error conditions

## Running the Application

1. **Prerequisites**:
   - .NET 8.0 SDK
   - Kafka cluster running on localhost:9092 (or update configuration)
   - Downstream service running on localhost:8080 (or update configuration)

2. **Start the Application**:
   ```bash
   dotnet run
   ```

3. **Monitor Logs**: The application will log circuit breaker state changes and processing activities

## Customization

### Adding More Consumers/Producers

Modify the Program.cs file to add more consumer/producer instances by adjusting the loop counts.

### Custom State Store

Implement `IDistributedCircuitStateStore` for Redis, database, or other distributed storage solutions.

### Custom Downstream Services

Implement `IDownstreamService` for your specific downstream service integration.

## Design Principles

This implementation follows SOLID principles:

- **Single Responsibility**: Each class has a focused responsibility
- **Open/Closed**: Extensible through interfaces without modifying existing code
- **Liskov Substitution**: Interface implementations are interchangeable
- **Interface Segregation**: Focused, role-based interfaces
- **Dependency Inversion**: Dependencies injected through abstractions

The design patterns used include:

- **Factory Pattern**: For creating circuit breaker instances
- **Strategy Pattern**: For different message processing strategies
- **Observer Pattern**: For circuit breaker state monitoring
- **Command Pattern**: For encapsulating message processing operations