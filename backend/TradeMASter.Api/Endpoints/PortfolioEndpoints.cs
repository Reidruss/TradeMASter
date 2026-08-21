using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Interfaces;
using TradeMASter.Core.ValueObjects;
using TradeMASter.Infrastructure.Persistence.Repositories;

namespace TradeMASter.Api.Endpoints;

public record UpdateRiskParametersRequest(
    decimal MaxPositionSizePercent,
    decimal MaxPortfolioDrawdownPercent,
    decimal DefaultStopLossPercent,
    decimal DefaultTakeProfitPercent,
    bool RequireHumanApprovalForLive,
    decimal MaxDailyLossAmount);

public static class PortfolioEndpoints
{
    public static RouteGroupBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/portfolio").WithTags("Portfolio & Risk");

        group.MapGet("/", async (
            [FromServices] IPortfolioRepository portfolioRepo,
            [FromServices] IBrokerClient brokerClient,
            [FromServices] IRobinhoodService robinhoodService) =>
        {
            // Sync live holdings from Robinhood MCP / Broker
            await robinhoodService.SyncHoldingsToPortfolioAsync();

            var portfolio = await portfolioRepo.GetActivePortfolioWithDetailsAsync();
            if (portfolio is null)
            {
                return Results.NotFound(new { error = "No active portfolio found." });
            }

            var refreshedResult = await brokerClient.GetPortfolioAsync(portfolio.Id);
            return refreshedResult.IsSuccess
                ? Results.Ok(refreshedResult.Value)
                : Results.Ok(portfolio);
        })
        .WithName("GetActivePortfolio")
        .WithSummary("Get active portfolio overview with current market valuation, cash, and PnL");

        group.MapGet("/positions", async (
            [FromServices] IPortfolioRepository portfolioRepo,
            [FromServices] IBrokerClient brokerClient,
            [FromServices] IRobinhoodService robinhoodService) =>
        {
            await robinhoodService.SyncHoldingsToPortfolioAsync();

            var portfolio = await portfolioRepo.GetActivePortfolioWithDetailsAsync();
            if (portfolio is null)
            {
                return Results.NotFound(new { error = "No active portfolio found." });
            }

            var positionsResult = await brokerClient.GetPositionsAsync(portfolio.Id);
            return positionsResult.IsSuccess
                ? Results.Ok(positionsResult.Value)
                : Results.Ok(portfolio.Positions);
        })
        .WithName("GetPortfolioPositions")
        .WithSummary("Get all currently held open positions in the active portfolio");

        group.MapGet("/risk", async ([FromServices] IPortfolioRepository portfolioRepo) =>
        {
            var portfolio = await portfolioRepo.GetActivePortfolioWithDetailsAsync();
            if (portfolio is null)
            {
                return Results.NotFound(new { error = "No active portfolio found." });
            }

            return Results.Ok(portfolio.RiskConfig);
        })
        .WithName("GetPortfolioRiskParameters")
        .WithSummary("Get current risk limits and safety guardrails");

        group.MapPut("/risk", async (
            [FromBody] UpdateRiskParametersRequest request,
            [FromServices] IPortfolioRepository portfolioRepo) =>
        {
            var portfolio = await portfolioRepo.GetActivePortfolioWithDetailsAsync();
            if (portfolio is null)
            {
                return Results.NotFound(new { error = "No active portfolio found." });
            }

            portfolio.RiskConfig = new RiskParameters(
                request.MaxPositionSizePercent,
                request.MaxPortfolioDrawdownPercent,
                request.DefaultStopLossPercent,
                request.DefaultTakeProfitPercent,
                request.RequireHumanApprovalForLive,
                request.MaxDailyLossAmount
            );

            await portfolioRepo.UpdateAsync(portfolio);
            return Results.Ok(portfolio.RiskConfig);
        })
        .WithName("UpdatePortfolioRiskParameters")
        .WithSummary("Update risk tolerance and automated execution guardrails");

        return group;
    }
}
