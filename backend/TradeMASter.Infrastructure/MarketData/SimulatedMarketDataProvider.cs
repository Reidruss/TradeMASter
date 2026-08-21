using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Infrastructure.MarketData;

public class SimulatedMarketDataProvider : IMarketDataProvider
{
    public string ProviderName => "SimulatedMarketData";

    private static readonly Dictionary<string, (string Name, AssetType Type, decimal BasePrice, decimal Volatility)> SeedAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVDA"] = ("NVIDIA Corporation", AssetType.Stock, 132.50m, 0.025m),
        ["AAPL"] = ("Apple Inc.", AssetType.Stock, 228.40m, 0.015m),
        ["MSFT"] = ("Microsoft Corporation", AssetType.Stock, 445.80m, 0.012m),
        ["TSLA"] = ("Tesla, Inc.", AssetType.Stock, 218.90m, 0.035m),
        ["AMZN"] = ("Amazon.com, Inc.", AssetType.Stock, 186.20m, 0.018m),
        ["GOOGL"] = ("Alphabet Inc.", AssetType.Stock, 178.60m, 0.016m),
        ["META"] = ("Meta Platforms, Inc.", AssetType.Stock, 510.40m, 0.022m),
        ["BTC-USD"] = ("Bitcoin USD", AssetType.Crypto, 64250.00m, 0.030m),
        ["ETH-USD"] = ("Ethereum USD", AssetType.Crypto, 3480.00m, 0.038m),
        ["SOL-USD"] = ("Solana USD", AssetType.Crypto, 148.50m, 0.045m),
        ["SPY"] = ("SPDR S&P 500 ETF Trust", AssetType.Etf, 552.30m, 0.008m),
        ["QQQ"] = ("Invesco QQQ Trust", AssetType.Etf, 480.10m, 0.011m)
    };

    private readonly Random _random = new();

    public Task<Result<PriceTick>> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var upper = symbol.ToUpperInvariant();
        var baseInfo = SeedAssets.TryGetValue(upper, out var info) 
            ? info 
            : (Name: upper, Type: AssetType.Stock, BasePrice: 100.0m, Volatility: 0.02m);

        // Add a micro random variation to base price
        var deltaPct = (decimal)(_random.NextDouble() * 2 - 1) * (baseInfo.Volatility * 0.5m);
        var currentPrice = Math.Round(baseInfo.BasePrice * (1m + deltaPct), 2);
        var change24h = Math.Round(currentPrice - baseInfo.BasePrice, 2);
        var changePct = Math.Round((change24h / baseInfo.BasePrice) * 100m, 2);

        var spread = Math.Max(0.01m, Math.Round(currentPrice * 0.0005m, 2));
        var bid = currentPrice - (spread / 2m);
        var ask = currentPrice + (spread / 2m);
        var volume = (decimal)(_random.Next(500_000, 15_000_000));

        var tick = new PriceTick(
            upper,
            currentPrice,
            volume,
            DateTime.UtcNow,
            bid,
            ask,
            change24h,
            changePct
        );

        return Task.FromResult(Result.Success(tick));
    }

    public Task<Result<IReadOnlyList<Candle>>> GetCandlesAsync(
        string symbol,
        TimeFrame timeFrame,
        int limit = 100,
        DateTime? start = null,
        DateTime? end = null,
        CancellationToken cancellationToken = default)
    {
        var upper = symbol.ToUpperInvariant();
        var baseInfo = SeedAssets.TryGetValue(upper, out var info) 
            ? info 
            : (Name: upper, Type: AssetType.Stock, BasePrice: 100.0m, Volatility: 0.02m);

        var candles = new List<Candle>();
        var endTime = end ?? DateTime.UtcNow;
        var intervalSpan = GetTimeSpan(timeFrame);
        var currentPointTime = endTime.Subtract(TimeSpan.FromTicks(intervalSpan.Ticks * limit));
        
        var currentPrice = baseInfo.BasePrice * 0.92m; // Start slightly lower to simulate trend

        for (int i = 0; i < limit; i++)
        {
            var drift = 0.0003;
            var shock = (_random.NextDouble() * 2 - 1) * (double)baseInfo.Volatility;
            var pctChange = (decimal)(drift + shock);
            
            var open = currentPrice;
            var close = Math.Round(Math.Max(0.01m, open * (1m + pctChange)), 2);
            
            var intraHigh = Math.Max(open, close) * (1m + (decimal)(_random.NextDouble() * 0.008));
            var intraLow = Math.Min(open, close) * (1m - (decimal)(_random.NextDouble() * 0.008));

            var high = Math.Round(intraHigh, 2);
            var low = Math.Round(Math.Max(0.01m, intraLow), 2);
            var volume = (decimal)_random.Next(10_000, 500_000);

            candles.Add(new Candle(upper, timeFrame, open, high, low, close, volume, currentPointTime));

            currentPrice = close;
            currentPointTime = currentPointTime.Add(intervalSpan);
        }

        return Task.FromResult(Result.Success<IReadOnlyList<Candle>>(candles));
    }

    public Task<Result<IReadOnlyList<Asset>>> SearchAssetsAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToUpperInvariant();
        var matches = SeedAssets
            .Where(kvp => kvp.Key.Contains(q) || kvp.Value.Name.ToUpperInvariant().Contains(q))
            .Select(kvp => new Asset(kvp.Key, kvp.Value.Name, kvp.Value.Type, "SIMULATED", "USD", true, kvp.Value.BasePrice))
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<Asset>>(matches));
    }

    public Task<Result<IReadOnlyList<Asset>>> GetTradableAssetsAsync(CancellationToken cancellationToken = default)
    {
        var list = SeedAssets
            .Select(kvp => new Asset(kvp.Key, kvp.Value.Name, kvp.Value.Type, "SIMULATED", "USD", true, kvp.Value.BasePrice))
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<Asset>>(list));
    }

    private static TimeSpan GetTimeSpan(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => TimeSpan.FromMinutes(1),
        TimeFrame.FiveMinutes => TimeSpan.FromMinutes(5),
        TimeFrame.FifteenMinutes => TimeSpan.FromMinutes(15),
        TimeFrame.OneHour => TimeSpan.FromHours(1),
        TimeFrame.FourHours => TimeSpan.FromHours(4),
        TimeFrame.OneDay => TimeSpan.FromDays(1),
        TimeFrame.OneWeek => TimeSpan.FromDays(7),
        _ => TimeSpan.FromMinutes(1)
    };
}
