using CircuitBreakerService.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace CircuitBreakerService.Infrastructure;

public class InMemoryCircuitStateStore : IDistributedCircuitStateStore
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly TimeSpan _failureExpiration = TimeSpan.FromMinutes(5);

    public Task<int> IncrementFailureCountAsync(string circuitId)
    {
        var key = $"{circuitId}-failures";
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = _failureExpiration;
            return 0;
        });

        count++;
        _cache.Set(key, count, new MemoryCacheEntryOptions
        {
            SlidingExpiration = _failureExpiration
        });

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
        var options = new MemoryCacheEntryOptions();
        if (ttl.HasValue)
            options.AbsoluteExpirationRelativeToNow = ttl;

        _cache.Set($"{circuitId}-state", state, options);
        return Task.CompletedTask;
    }

    public Task<bool> AcquireProbeLeadershipAsync(string circuitId, string instanceId)
    {
        // Leadership not needed in single instance
        return Task.FromResult(true);
    }
}
