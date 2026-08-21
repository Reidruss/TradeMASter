using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Api.Endpoints;

public static class MarketIntelligenceEndpoints
{
    public static RouteGroupBuilder MapMarketIntelligenceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/market-intelligence").WithTags("Market Intelligence");

        group.MapPost("/scan", async (
            [FromBody] MarketScanRequest? request,
            [FromServices] IMarketIntelligenceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RunMarketScanAsync(request ?? new MarketScanRequest(), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("RunFullMarketIntelligenceScan")
        .WithSummary("Scan the broad U.S. listing universe, deeply research finalists, optimize weights, and run portfolio risk controls");

        group.MapGet("/latest", async (
            [FromServices] IMarketIntelligenceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetLatestRunAsync(cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetLatestMarketIntelligenceRun")
        .WithSummary("Get the latest completed market-wide intelligence and allocation run");

        return group;
    }
}
