using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeMASter.Agents.Orchestration;
using TradeMASter.Core.Entities;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Api.Endpoints;

public record DeliberationRequestDto(string Symbol, bool? AutoExecute);

public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/agents").WithTags("Multi-Agent Committee");

        group.MapPost("/deliberate", async (
            [FromBody] DeliberationRequestDto dto,
            [FromServices] IDeliberationEngine engine) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Symbol))
            {
                return Results.BadRequest(new { error = "Symbol cannot be empty." });
            }

            var autoExec = dto.AutoExecute ?? false;
            var result = await engine.DeliberateAsync(dto.Symbol, autoExecute: autoExec);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("TriggerDeliberation")
        .WithSummary("Trigger multi-agent committee debate, cross-examination, and consensus verdict for an asset");

        group.MapGet("/history", async ([FromServices] TradeMASterDbContext db) =>
        {
            var sessions = await db.DeliberationSessions
                .Include(s => s.Decisions)
                .OrderByDescending(s => s.CreatedAt)
                .Take(30)
                .ToListAsync();

            return Results.Ok(sessions);
        })
        .WithName("GetDeliberationHistory")
        .WithSummary("Get recent multi-agent deliberation sessions and debate summaries");

        group.MapGet("/session/{id:guid}", async (Guid id, [FromServices] TradeMASterDbContext db) =>
        {
            var session = await db.DeliberationSessions
                .Include(s => s.Decisions)
                .FirstOrDefaultAsync(s => s.Id == id);

            return session is not null
                ? Results.Ok(session)
                : Results.NotFound(new { error = $"Deliberation session with ID '{id}' not found." });
        })
        .WithName("GetDeliberationSession")
        .WithSummary("Get a specific deliberation session with detailed agent decisions");

        return group;
    }
}
