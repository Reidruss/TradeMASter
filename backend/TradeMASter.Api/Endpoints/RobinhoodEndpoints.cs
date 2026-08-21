using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Api.Endpoints;

public static class RobinhoodEndpoints
{
    public static RouteGroupBuilder MapRobinhoodEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/robinhood").WithTags("Robinhood Integration");

        group.MapGet("/oauth/url", async (
            [FromQuery] string? redirectUri,
            [FromServices] IRobinhoodService service) =>
        {
            var uri = redirectUri ?? "http://localhost:5173/auth/callback";
            var result = await service.GetOAuthAuthorizationUrlAsync(uri);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetRobinhoodOAuthUrl")
        .WithSummary("Generate PKCE OAuth 2.0 authorization URL for official Robinhood Agentic Trading");

        group.MapPost("/oauth/exchange", async (
            [FromBody] RobinhoodOAuthExchangeRequest request,
            [FromServices] IRobinhoodService service) =>
        {
            var result = await service.ExchangeOAuthCodeAsync(request);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("ExchangeRobinhoodOAuthCode")
        .WithSummary("Exchange authorization code for access token, persist session and initialize Robinhood Agent account");

        group.MapPost("/connect", async (
            [FromBody] RobinhoodAuthRequest request,
            [FromServices] IRobinhoodService service) =>
        {
            var result = await service.ConnectAsync(request);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("ConnectRobinhood")
        .WithSummary("Connect with an MCP bearer token or initialize the local demo sandbox; password login is disabled");

        group.MapPost("/disconnect", async ([FromServices] IRobinhoodService service) =>
        {
            var result = await service.DisconnectAsync();
            return result.IsSuccess
                ? Results.Ok(new { message = "Disconnected successfully." })
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("DisconnectRobinhood")
        .WithSummary("Disconnect Robinhood account and clear saved credentials");

        group.MapGet("/session", async ([FromServices] IRobinhoodService service) =>
        {
            var result = await service.GetSavedSessionAsync();
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetSavedRobinhoodSession")
        .WithSummary("Get saved auto-login session status");

        group.MapGet("/status", async ([FromServices] IRobinhoodService service) =>
        {
            var result = await service.GetAccountStatusAsync();
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetRobinhoodStatus")
        .WithSummary("Get live Robinhood connection status, total equity, cash, and buying power");

        group.MapGet("/holdings", async ([FromServices] IRobinhoodService service) =>
        {
            var result = await service.GetLiveHoldingsAsync();
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("GetRobinhoodHoldings")
        .WithSummary("Get real-time holdings, quantities, cost basis, and portfolio weights from Robinhood");

        group.MapPost("/holdings/custom", async (
            [FromBody] List<RobinhoodHoldingItem> customHoldings,
            [FromServices] IRobinhoodService service) =>
        {
            var result = await service.SetCustomHoldingsAsync(customHoldings);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("SetCustomRobinhoodHoldings")
        .WithSummary("Configure custom portfolio holdings directly");

        group.MapPost("/sync", async ([FromServices] IRobinhoodService service) =>
        {
            var result = await service.SyncHoldingsToPortfolioAsync();
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("SyncRobinhoodPortfolio")
        .WithSummary("Synchronize live Robinhood investments into TradeMASter database portfolio");

        return group;
    }
}
