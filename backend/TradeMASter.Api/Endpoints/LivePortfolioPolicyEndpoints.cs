using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Api.Endpoints;

public record EmergencyHaltRequest(string Reason);
public record ClearEmergencyHaltRequest(string Confirmation);

public static class LivePortfolioPolicyEndpoints
{
    public static RouteGroupBuilder MapLivePortfolioPolicyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/live-policy")
            .WithTags("Live Portfolio Safety Policy");

        group.MapGet("/", async (
            [FromServices] ILivePortfolioPolicyService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(cancellationToken)))
        .WithName("GetLivePortfolioPolicy")
        .WithSummary("Get the persisted non-bypassable live portfolio policy and emergency halt state");

        group.MapPut("/", async (
            [FromBody] UpdateLivePortfolioPolicyRequest request,
            [FromServices] ILivePortfolioPolicyService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("UpdateLivePortfolioPolicy")
        .WithSummary("Update deterministic safety limits; this endpoint cannot enable live trading");

        group.MapPost("/emergency-halt", async (
            [FromBody] EmergencyHaltRequest request,
            [FromServices] ILivePortfolioPolicyService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ActivateEmergencyHaltAsync(request.Reason, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("ActivateLiveTradingEmergencyHalt")
        .WithSummary("Persist an immediate circuit breaker that blocks all new live exposure");

        group.MapPost("/resume", async (
            [FromBody] ClearEmergencyHaltRequest request,
            [FromServices] ILivePortfolioPolicyService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ClearEmergencyHaltAsync(request.Confirmation, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("ClearLiveTradingEmergencyHalt")
        .WithSummary("Clear the emergency halt with an exact confirmation; live trading remains disabled");

        return group;
    }
}
