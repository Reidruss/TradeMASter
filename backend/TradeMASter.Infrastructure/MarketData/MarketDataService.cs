using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Core.ValueObjects;
using TradeMASter.Infrastructure.Persistence.Repositories;

namespace TradeMASter.Infrastructure.MarketData;

public interface IMarketDataService
{
    Task<Result<PriceTick>> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Candle>>> GetCandlesAsync(string symbol, TimeFrame timeFrame, int limit = 100, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Asset>>> SearchAssetsAsync(string query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Asset>>> GetTradableAssetsAsync(CancellationToken cancellationToken = default);
}

public class MarketDataService : IMarketDataService
{
    private readonly YahooFinanceMarketDataProvider _yahooProvider;
    private readonly SimulatedMarketDataProvider _simulatedProvider;
    private readonly IAssetRepository _assetRepository;
    private readonly ICacheService _cache;

    public MarketDataService(
        YahooFinanceMarketDataProvider yahooProvider,
        SimulatedMarketDataProvider simulatedProvider,
        IAssetRepository assetRepository,
        ICacheService cache)
    {
        _yahooProvider = yahooProvider;
        _simulatedProvider = simulatedProvider;
        _assetRepository = assetRepository;
        _cache = cache;
    }

    public async Task<Result<PriceTick>> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"quote:{symbol.ToUpperInvariant()}";
        var cached = await _cache.GetAsync<PriceTick>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return Result.Success(cached);
        }

        // Try Yahoo Finance first
        var quoteResult = await _yahooProvider.GetQuoteAsync(symbol, cancellationToken);
        if (quoteResult.IsFailure)
        {
            // Fallback to Simulated Provider
            quoteResult = await _simulatedProvider.GetQuoteAsync(symbol, cancellationToken);
        }

        if (quoteResult.IsSuccess)
        {
            await _cache.SetAsync(cacheKey, quoteResult.Value, TimeSpan.FromSeconds(5), cancellationToken);
        }

        return quoteResult;
    }

    public async Task<Result<IReadOnlyList<Candle>>> GetCandlesAsync(
        string symbol,
        TimeFrame timeFrame,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"candles:{symbol.ToUpperInvariant()}:{timeFrame}:{limit}";
        var cached = await _cache.GetAsync<IReadOnlyList<Candle>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var candlesResult = await _yahooProvider.GetCandlesAsync(symbol, timeFrame, limit, cancellationToken: cancellationToken);
        if (candlesResult.IsFailure || candlesResult.Value.Count == 0)
        {
            candlesResult = await _simulatedProvider.GetCandlesAsync(symbol, timeFrame, limit, cancellationToken: cancellationToken);
        }

        if (candlesResult.IsSuccess && candlesResult.Value.Count > 0)
        {
            await _cache.SetAsync(cacheKey, candlesResult.Value, TimeSpan.FromSeconds(15), cancellationToken);
        }

        return candlesResult;
    }

    public async Task<Result<IReadOnlyList<Asset>>> SearchAssetsAsync(string query, CancellationToken cancellationToken = default)
    {
        var dbResults = await _assetRepository.SearchAsync(query, cancellationToken);
        if (dbResults.Count > 0)
        {
            return Result.Success(dbResults);
        }

        return await _simulatedProvider.SearchAssetsAsync(query, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<Asset>>> GetTradableAssetsAsync(CancellationToken cancellationToken = default)
    {
        var dbAssets = await _assetRepository.ListAsync(a => a.IsTradable, cancellationToken);
        if (dbAssets.Count > 0)
        {
            return Result.Success(dbAssets);
        }

        return await _simulatedProvider.GetTradableAssetsAsync(cancellationToken);
    }
}
