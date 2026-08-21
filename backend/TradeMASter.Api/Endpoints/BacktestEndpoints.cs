using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Backtesting;

namespace TradeMASter.Api.Endpoints;

public static class BacktestEndpoints
{
    public static RouteGroupBuilder MapBacktestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/backtest").WithTags("Backtesting & Quantitative Simulation");

        group.MapPost("/run", async (
            [FromBody] BacktestRequest request,
            [FromServices] IBacktestEngine engine) =>
        {
            var result = await engine.RunBacktestAsync(request);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("RunBacktest")
        .WithSummary("Run historical strategy backtest with trade ledger and Sharpe/Drawdown metrics");

        group.MapGet("/strategies", ([FromServices] IBacktestEngine engine) =>
        {
            var strategies = engine.GetAvailableStrategies();
            return Results.Ok(strategies);
        })
        .WithName("GetBacktestStrategies")
        .WithSummary("List all registered trading strategies available for simulation");

        return group;
    }
}
