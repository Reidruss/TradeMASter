using System.Diagnostics;
using System.Runtime.InteropServices;
using DotnetSvelte.Api.Models;

namespace DotnetSvelte.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static RouteGroupBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/health")
            .WithTags("Health");

        group.MapGet("/", (IHostEnvironment env) =>
        {
            var health = new HealthInfo(
                Status: "Healthy",
                FrameworkVersion: RuntimeInformation.FrameworkDescription,
                ServerTimeUtc: DateTime.UtcNow,
                Uptime: DateTime.UtcNow >= StartTime ? DateTime.UtcNow - StartTime : TimeSpan.Zero,
                Environment: env.EnvironmentName
            );

            return Results.Ok(health);
        })
        .WithName("GetHealthStatus")
        .WithSummary("Retrieves the system health status and server runtime information")
        .Produces<HealthInfo>(StatusCodes.Status200OK);

        return group;
    }
}
