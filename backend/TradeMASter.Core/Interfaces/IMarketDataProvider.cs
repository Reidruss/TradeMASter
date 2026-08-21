using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Core.Interfaces;

public interface IMarketDataProvider
{
    string ProviderName { get; }
    Task<Result<PriceTick>> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Candle>>> GetCandlesAsync(
        string symbol,
        TimeFrame timeFrame,
        int limit = 100,
        DateTime? start = null,
        DateTime? end = null,
        CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Asset>>> SearchAssetsAsync(string query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Asset>>> GetTradableAssetsAsync(CancellationToken cancellationToken = default);
}
