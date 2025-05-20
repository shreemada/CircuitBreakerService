using CircuitBreakerService.Contracts;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using RedLockNet.SERedis.Configuration;

namespace CircuitBreakerService.Infrastructure;

public class RedisCircuitStateStore : IDistributedCircuitStateStore
{
    private readonly IDatabase _redis;
    private readonly RedLockFactory _redlockFactory;

    public RedisCircuitStateStore(IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
        _redlockFactory = RedLockFactory.Create(new List<RedLockMultiplexer>
        {
            new RedLockMultiplexer(redis)
        });
    }

    public async Task<int> IncrementFailureCountAsync(string circuitId)
    {
        var key = $"circuit:{circuitId}:failures";
        return (int)await _redis.StringIncrementAsync(key);
    }

    public async Task ResetFailureCountAsync(string circuitId)
    {
        var key = $"circuit:{circuitId}:failures";
        await _redis.KeyDeleteAsync(key);
    }

    public async Task<CircuitState> GetCircuitStateAsync(string circuitId)
    {
        var state = await _redis.StringGetAsync($"circuit:{circuitId}:state");
        return state.HasValue ?
            (CircuitState)int.Parse(state) :
            CircuitState.Closed;
    }

    public async Task SetCircuitStateAsync(string circuitId, CircuitState state, TimeSpan? ttl = null)
    {
        var key = $"circuit:{circuitId}:state";
        await _redis.StringSetAsync(key, ((int)state).ToString());
        if (ttl.HasValue)
            await _redis.KeyExpireAsync(key, ttl);
    }

    public async Task<bool> AcquireProbeLeadershipAsync(string circuitId, string instanceId)
    {
        var resource = $"circuit:{circuitId}:leader";
        var expiry = TimeSpan.FromSeconds(10);

        using var redLock = await _redlockFactory.CreateLockAsync(
            resource,
            expiry,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2)
        );

        return redLock.IsAcquired;
    }
}