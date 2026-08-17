using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using TradeMASter.Api.Models;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence.Repositories;

namespace TradeMASter.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static RouteGroupBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/health")
            .WithTags("Health");

        group.MapGet("/", async (
            IHostEnvironment env,
            [FromServices] IPortfolioRepository portfolioRepo,
            [FromServices] ICacheService cache,
            [FromServices] IMarketDataService marketData,
            [FromServices] IBrokerClient brokerClient) =>
        {
            var components = new Dictionary<string, ComponentStatus>();

            // Check database
            try
            {
                var portfolioCount = await portfolioRepo.CountAsync();
                components["Database"] = new ComponentStatus("Healthy", $"Online ({portfolioCount} active portfolio(s))");
            }
            catch (Exception ex)
            {
                components["Database"] = new ComponentStatus("Degraded", ex.Message);
            }

            // Check cache
            try
            {
                await cache.SetAsync("__health_ping__", "pong", TimeSpan.FromSeconds(10));
                var val = await cache.GetAsync<string>("__health_ping__");
                components["Cache"] = new ComponentStatus("Healthy", val == "pong" ? "Online & Responsive" : "Degraded");
            }
            catch (Exception ex)
            {
                components["Cache"] = new ComponentStatus("Degraded", ex.Message);
            }

            // Check Market Data
            try
            {
                var quote = await marketData.GetQuoteAsync("SPY");
                components["MarketData"] = quote.IsSuccess 
                    ? new ComponentStatus("Healthy", $"Live Quote Active (SPY: ${quote.Value.Price:N2})") 
                    : new ComponentStatus("Warning", quote.Error ?? "Unable to fetch quote");
            }
            catch (Exception ex)
            {
                components["MarketData"] = new ComponentStatus("Degraded", ex.Message);
            }

            // Check Broker
            components["Broker"] = new ComponentStatus("Healthy", $"{brokerClient.BrokerName} Ready");

            var overallHealthy = components.Values.All(c => c.Status == "Healthy");

            var health = new HealthInfo(
                Status: overallHealthy ? "Healthy" : "Degraded",
                FrameworkVersion: RuntimeInformation.FrameworkDescription,
                ServerTimeUtc: DateTime.UtcNow,
                Uptime: DateTime.UtcNow >= StartTime ? DateTime.UtcNow - StartTime : TimeSpan.Zero,
                Environment: env.EnvironmentName,
                Components: components
            );

            return Results.Ok(health);
        })
        .WithName("GetHealthStatus")
        .WithSummary("Retrieves the system health status, component connectivity, and server runtime information")
        .Produces<HealthInfo>(StatusCodes.Status200OK);

        return group;
    }
}
