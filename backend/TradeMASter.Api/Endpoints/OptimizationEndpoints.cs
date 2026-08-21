using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Api.Endpoints;

public record RunOptimizationRequest(Guid? PortfolioId);

public static class OptimizationEndpoints
{
    public static RouteGroupBuilder MapOptimizationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/optimizer").WithTags("Bi-Weekly Portfolio Optimizer");

        group.MapPost("/run", async (
            [FromBody] RunOptimizationRequest? request,
            [FromServices] IPortfolioOptimizerService optimizer) =>
        {
            var result = await optimizer.GenerateBiWeeklyOptimizationPlanAsync(request?.PortfolioId);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("RunPortfolioOptimization")
        .WithSummary("Trigger multi-agent committee review of all portfolio holdings and generate optimal target weights and rebalance orders");

        group.MapPost("/execute", async (
            [FromBody] OptimizationPlan plan,
            [FromServices] IPortfolioOptimizerService optimizer) =>
        {
            var result = await optimizer.ExecuteOptimizationPlanAsync(plan);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("ExecuteOptimizationPlan")
        .WithSummary("Approve and execute proposed rebalancing trades in the local paper broker; live Robinhood trading is disabled");

        group.MapGet("/schedule", async ([FromServices] IPortfolioOptimizerService optimizer) =>
        {
            var result = await optimizer.GetNextScheduledRebalanceTimeAsync();
            return result.IsSuccess
                ? Results.Ok(new
                {
                    nextScheduledRebalanceUtc = result.Value,
                    intervalDays = 14,
                    frequency = "Bi-Weekly"
                })
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetRebalanceSchedule")
        .WithSummary("Get next scheduled automated bi-weekly rebalance timestamp and recurrence configuration");

        return group;
    }
}
