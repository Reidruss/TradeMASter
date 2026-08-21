using System.Collections.Concurrent;
using System.Text.Json;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Cache;

public class InMemoryCacheService : ICacheService
{
    private record CacheEntry(object Value, DateTime? Expiry);

    private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
    private readonly ConcurrentDictionary<string, List<Delegate>> _subscribers = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.Expiry.HasValue && entry.Expiry.Value < DateTime.UtcNow)
            {
                _store.TryRemove(key, out _);
                return Task.FromResult<T?>(default);
            }

            if (entry.Value is T typedValue)
            {
                return Task.FromResult<T?>(typedValue);
            }

            if (entry.Value is string jsonStr && typeof(T) != typeof(string))
            {
                var deserialized = JsonSerializer.Deserialize<T>(jsonStr);
                return Task.FromResult(deserialized);
            }
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var expiryTime = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null;
        _store[key] = new CacheEntry(value!, expiryTime);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
    {
        if (_subscribers.TryGetValue(channel, out var handlers))
        {
            lock (handlers)
            {
                foreach (var handler in handlers)
                {
                    if (handler is Action<T> action)
                    {
                        Task.Run(() =>
                        {
                            try
                            {
                                action(message);
                            }
                            catch
                            {
                                // Log or swallow background subscriber exception
                            }
                        }, cancellationToken);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task SubscribeAsync<T>(string channel, Action<T> handler, CancellationToken cancellationToken = default)
    {
        var handlers = _subscribers.GetOrAdd(channel, _ => new List<Delegate>());
        lock (handlers)
        {
            handlers.Add(handler);
        }
        return Task.CompletedTask;
    }
}
