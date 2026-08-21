using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Api.Endpoints;

public static class TradePlanEndpoints
{
    public static RouteGroupBuilder MapTradePlanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/trade-plans")
            .WithTags("Immutable Trade Plan Review");

        group.MapGet("/latest", async (
            [FromServices] ITradePlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetLatestAsync(cancellationToken);
            if (result.IsFailure) return Results.BadRequest(new { error = result.Error });
            return result.Value is null
                ? Results.Content("null", "application/json")
                : Results.Ok(result.Value);
        })
        .WithName("GetLatestTradePlan")
        .WithSummary("Get the latest persisted immutable plan and refresh its expiry status");

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] ITradePlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(new { error = result.Error });
        })
        .WithName("GetTradePlan")
        .WithSummary("Get one persisted immutable trade plan by ID");

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            [FromBody] ApproveTradePlanRequest request,
            [FromServices] ITradePlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ApproveAsync(id, request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Conflict(new { error = result.Error });
        })
        .WithName("ApproveExactTradePlan")
        .WithSummary("Approve the exact hash-bound plan after policy, expiry, integrity, and account-drift checks; never submits orders");

        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            [FromBody] RejectTradePlanRequest request,
            [FromServices] ITradePlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RejectAsync(id, request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Conflict(new { error = result.Error });
        })
        .WithName("RejectExactTradePlan")
        .WithSummary("Reject the exact hash-bound plan with a persisted reason; never submits orders");

        group.MapGet("/{id:guid}/execution", async (
            Guid id,
            [FromServices] ILiveExecutionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByTradePlanAsync(id, cancellationToken);
            if (result.IsFailure) return Results.BadRequest(new { error = result.Error });
            return result.Value is null
                ? Results.Content("null", "application/json")
                : Results.Ok(result.Value);
        })
        .WithName("GetTradePlanLiveExecution")
        .WithSummary("Get the durable preflight, outbox, idempotency, and broker-acceptance state for an approved plan");

        group.MapPost("/{id:guid}/execute", async (
            Guid id,
            [FromBody] ExecuteApprovedTradePlanRequest request,
            [FromServices] ILiveExecutionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ExecuteApprovedPlanAsync(id, request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Conflict(new { error = result.Error });
        })
        .WithName("ExecuteApprovedTradePlan")
        .WithSummary("Run fresh deterministic broker preflight and a durable idempotent outbox; submission remains blocked until both persisted and application authority are enabled");

        return group;
    }
}
