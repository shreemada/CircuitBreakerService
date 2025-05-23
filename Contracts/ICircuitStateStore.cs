using CircuitBreakerService.Models;
using Polly.CircuitBreaker;

namespace CircuitBreakerService.Contracts;
public interface IDistributedCircuitStateStore
{
    Task<CircuitState> GetCircuitStateAsync(string circuitName);
    Task SetCircuitStateAsync(string circuitName, CircuitState state);
    Task<bool> TryLockAsync(string circuitName, TimeSpan lockDuration);
    Task ReleaseLockAsync(string circuitName);
    Task IncrementFailureCountAsync(string circuitName);
    Task ResetFailureCountAsync(string circuitName);
    Task<int> GetFailureCountAsync(string circuitName);
    Task SetLastFailureTimeAsync(string circuitName, DateTimeOffset timestamp);
    Task<DateTimeOffset?> GetLastFailureTimeAsync(string circuitName);
}
