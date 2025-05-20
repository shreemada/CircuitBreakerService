namespace CircuitBreakerService.Contracts;

// Contracts/ICircuitStateStore.cs
public interface IDistributedCircuitStateStore
{
    Task<int> IncrementFailureCountAsync(string circuitId);
    Task ResetFailureCountAsync(string circuitId);
    Task<CircuitState> GetCircuitStateAsync(string circuitId);
    Task SetCircuitStateAsync(string circuitId, CircuitState state, TimeSpan? ttl = null);
    Task<bool> AcquireProbeLeadershipAsync(string circuitId, string instanceId);
}

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}