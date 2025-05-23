using CircuitBreakerService.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace CircuitBreakerService.Infrastructure;

public class InMemoryCircuitStateStore : IDistributedCircuitStateStore
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(5);

    public Task<int> IncrementFailureCountAsync(string circuitId)
    {
        var key = $"{circuitId}-failures";
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _defaultTtl;
            return 0;
        }) + 1;

        _cache.Set(key, count, _defaultTtl);
        return Task.FromResult(count);
    }

    public Task ResetFailureCountAsync(string circuitId)
    {
        _cache.Remove($"{circuitId}-failures");
        return Task.CompletedTask;
    }

    public Task<CircuitState> GetCircuitStateAsync(string circuitId)
    {
        var state = _cache.Get<CircuitState?>($"{circuitId}-state");
        return Task.FromResult(state ?? CircuitState.Closed);
    }

    public Task SetCircuitStateAsync(string circuitId, CircuitState state, TimeSpan? ttl = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? _defaultTtl
        };

        _cache.Set($"{circuitId}-state", state, options);
        return Task.CompletedTask;
    }

    public Task<bool> AcquireProbeLeadershipAsync(string circuitId, string instanceId)
    {
        // Leadership not needed in single instance
        return Task.FromResult(true);
    }
}
