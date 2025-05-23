using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Models;

namespace CircuitBreakerService.Contracts;

public interface IMessageProcessor
{
    Task<ProcessingResult> ProcessMessageAsync(KafkaMessage message, CancellationToken cancellationToken);
}

public interface IDownstreamService
{
    Task<ProcessingResult> SendToDownstreamAsync(KafkaMessage message, CancellationToken cancellationToken);
}

public interface ICircuitBreakerFactory
{
    DistributedCircuitBreaker CreateCircuitBreaker(string circuitName);
}