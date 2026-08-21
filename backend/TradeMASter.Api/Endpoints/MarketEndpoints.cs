using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Enums;
using TradeMASter.Infrastructure.MarketData;

namespace TradeMASter.Api.Endpoints;

public static class MarketEndpoints
{
    public static RouteGroupBuilder MapMarketEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/market").WithTags("Market Data");

        group.MapGet("/quote/{symbol}", async (string symbol, [FromServices] IMarketDataService marketData) =>
        {
            var result = await marketData.GetQuoteAsync(symbol);
            return result.IsSuccess 
                ? Results.Ok(result.Value) 
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetMarketQuote")
        .WithSummary("Fetch real-time market quote and tick for a symbol");

        group.MapGet("/candles/{symbol}", async (
            string symbol,
            [FromQuery] TimeFrame? timeframe,
            [FromQuery] int? limit,
            [FromServices] IMarketDataService marketData) =>
        {
            var tf = timeframe ?? TimeFrame.OneDay;
            var candleLimit = Math.Clamp(limit ?? 60, 10, 500);

            var result = await marketData.GetCandlesAsync(symbol, tf, candleLimit);
            return result.IsSuccess 
                ? Results.Ok(result.Value) 
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetMarketCandles")
        .WithSummary("Fetch historical OHLCV candlestick bars for a symbol");

        group.MapGet("/assets", async (
            [FromQuery] string? query,
            [FromServices] IMarketDataService marketData) =>
        {
            var result = string.IsNullOrWhiteSpace(query)
                ? await marketData.GetTradableAssetsAsync()
                : await marketData.SearchAssetsAsync(query);

            return result.IsSuccess 
                ? Results.Ok(result.Value) 
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetTradableAssets")
        .WithSummary("Search or list all tradable financial assets");

        group.MapGet("/watchlist", async ([FromServices] IMarketDataService marketData) =>
        {
            var defaultWatchlist = new[] { "NVDA", "AAPL", "MSFT", "TSLA", "BTC-USD", "ETH-USD", "SPY", "QQQ" };
            var quoteTasks = defaultWatchlist.Select(sym => marketData.GetQuoteAsync(sym));
            var results = await Task.WhenAll(quoteTasks);

            var quotes = results
                .Where(r => r.IsSuccess)
                .Select(r => r.Value)
                .ToList();

            return Results.Ok(quotes);
        })
        .WithName("GetMarketWatchlist")
        .WithSummary("Fetch batch live quotes for key market watchlist tickers");

        return group;
    }
}
