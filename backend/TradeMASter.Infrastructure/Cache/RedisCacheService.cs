using System.Text.Json;
using StackExchange.Redis;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Cache;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ISubscriber _subscriber;

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _subscriber = redis.GetSubscriber();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        string str = value.ToString();
        return JsonSerializer.Deserialize<T>(str);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value);
        if (expiry.HasValue)
        {
            await _db.StringSetAsync(key, json, expiry.Value);
        }
        else
        {
            await _db.StringSetAsync(key, json);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(key);
    }

    public async Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);
        await _subscriber.PublishAsync(RedisChannel.Literal(channel), json);
    }

    public async Task SubscribeAsync<T>(string channel, Action<T> handler, CancellationToken cancellationToken = default)
    {
        await _subscriber.SubscribeAsync(RedisChannel.Literal(channel), (_, value) =>
        {
            if (!value.IsNullOrEmpty)
            {
                string str = value.ToString();
                var message = JsonSerializer.Deserialize<T>(str);
                if (message is not null)
                {
                    handler(message);
                }
            }
        });
    }
}
