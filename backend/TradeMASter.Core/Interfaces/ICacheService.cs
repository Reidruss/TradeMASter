namespace TradeMASter.Core.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default);
    Task SubscribeAsync<T>(string channel, Action<T> handler, CancellationToken cancellationToken = default);
}
