using System.Net.Http.Json;
using System.Text.Json;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Infrastructure.MarketData;

public class YahooFinanceMarketDataProvider : IMarketDataProvider
{
    public string ProviderName => "YahooFinance";
    private readonly HttpClient _httpClient;

    public YahooFinanceMarketDataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://query1.finance.yahoo.com/v8/finance/chart/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async Task<Result<PriceTick>> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{symbol}?interval=1m&range=1d";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<PriceTick>($"Yahoo Finance returned status {response.StatusCode} for {symbol}.");
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = result.GetProperty("meta");

            var regularMarketPrice = meta.GetProperty("regularMarketPrice").GetDecimal();
            var previousClose = meta.TryGetProperty("previousClose", out var prevProp) ? prevProp.GetDecimal() : regularMarketPrice;
            var change = regularMarketPrice - previousClose;
            var changePct = previousClose != 0 ? (change / previousClose) * 100m : 0m;
            var volume = meta.TryGetProperty("regularMarketVolume", out var volProp) ? volProp.GetDecimal() : 0m;

            var tick = new PriceTick(
                symbol,
                regularMarketPrice,
                volume,
                DateTime.UtcNow,
                regularMarketPrice * 0.9995m,
                regularMarketPrice * 1.0005m,
                change,
                changePct
            );

            return Result.Success(tick);
        }
        catch (Exception ex)
        {
            return Result.Failure<PriceTick>($"Failed to fetch Yahoo Finance quote for {symbol}: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<Candle>>> GetCandlesAsync(
        string symbol,
        TimeFrame timeFrame,
        int limit = 100,
        DateTime? start = null,
        DateTime? end = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var interval = MapTimeFrameToInterval(timeFrame);
            var range = MapTimeFrameToRange(timeFrame, limit);
            var url = $"{symbol}?interval={interval}&range={range}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<IReadOnlyList<Candle>>($"Yahoo Finance returned status {response.StatusCode} for {symbol}.");
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];

            if (!result.TryGetProperty("timestamp", out var timestampProp))
            {
                return Result.Failure<IReadOnlyList<Candle>>($"No timestamps found in Yahoo response for {symbol}.");
            }

            var timestamps = timestampProp.EnumerateArray().Select(t => t.GetInt64()).ToList();
            var indicators = result.GetProperty("indicators").GetProperty("quote")[0];
            var opens = indicators.GetProperty("open").EnumerateArray().ToList();
            var highs = indicators.GetProperty("high").EnumerateArray().ToList();
            var lows = indicators.GetProperty("low").EnumerateArray().ToList();
            var closes = indicators.GetProperty("close").EnumerateArray().ToList();
            var volumes = indicators.GetProperty("volume").EnumerateArray().ToList();

            var candles = new List<Candle>();
            for (int i = 0; i < timestamps.Count; i++)
            {
                if (opens[i].ValueKind == JsonValueKind.Null || closes[i].ValueKind == JsonValueKind.Null)
                    continue;

                var dt = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).UtcDateTime;
                var o = opens[i].GetDecimal();
                var h = highs[i].ValueKind != JsonValueKind.Null ? highs[i].GetDecimal() : o;
                var l = lows[i].ValueKind != JsonValueKind.Null ? lows[i].GetDecimal() : o;
                var c = closes[i].GetDecimal();
                var v = volumes[i].ValueKind != JsonValueKind.Null ? volumes[i].GetDecimal() : 0m;

                candles.Add(new Candle(symbol, timeFrame, o, h, l, c, v, dt));
            }

            if (candles.Count > limit)
            {
                candles = candles.TakeLast(limit).ToList();
            }

            return Result.Success<IReadOnlyList<Candle>>(candles);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Candle>>($"Failed to fetch Yahoo Finance candles for {symbol}: {ex.Message}");
        }
    }

    public Task<Result<IReadOnlyList<Asset>>> SearchAssetsAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<IReadOnlyList<Asset>>("Search not implemented directly on Yahoo API; using local cache/store."));
    }

    public Task<Result<IReadOnlyList<Asset>>> GetTradableAssetsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<IReadOnlyList<Asset>>("Tradable asset catalog retrieved from local registry."));
    }

    private static string MapTimeFrameToInterval(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => "1m",
        TimeFrame.FiveMinutes => "5m",
        TimeFrame.FifteenMinutes => "15m",
        TimeFrame.OneHour => "60m",
        TimeFrame.FourHours => "60m",
        TimeFrame.OneDay => "1d",
        TimeFrame.OneWeek => "1wk",
        _ => "1d"
    };

    private static string MapTimeFrameToRange(TimeFrame timeFrame, int limit) => timeFrame switch
    {
        TimeFrame.OneMinute => "1d",
        TimeFrame.FiveMinutes => "5d",
        TimeFrame.FifteenMinutes => "1mo",
        TimeFrame.OneHour => "1mo",
        TimeFrame.FourHours => "3mo",
        TimeFrame.OneDay => "1y",
        TimeFrame.OneWeek => "2y",
        _ => "1mo"
    };
}
