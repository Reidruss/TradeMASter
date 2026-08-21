using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Brokers.Robinhood;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class RobinhoodBrokerServiceTests
{
    [Fact]
    public async Task Connect_SelectsOnlyTheFundedRobinhoodAccount()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Robinhood:McpServerUrl"] = "https://agent.robinhood.com/mcp/trading"
            })
            .Build();
        var handler = new RobinhoodPayloadHandler();
        var mcpClient = new RobinhoodMcpClient(new HttpClient(handler), configuration);
        var dbOptions = new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new TradeMASterDbContext(dbOptions);
        var service = new RobinhoodBrokerService(
            new HttpClient(handler),
            mcpClient,
            Mock.Of<IMarketDataService>(),
            db,
            new EphemeralDataProtectionProvider(),
            configuration,
            NullLogger<RobinhoodBrokerService>.Instance);

        var result = await service.ConnectAsync(new RobinhoodAuthRequest(
            null, null, null, "test-token", RememberMe: true, UseDemoMode: false));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.TotalEquity.Should().Be(850m);
        result.Value.CashAvailable.Should().Be(850m);
        result.Value.BuyingPower.Should().Be(850m);
        result.Value.AccountNumber.Should().Be("837197250");
    }

    private sealed class RobinhoodPayloadHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();

            if (method == "notifications/initialized")
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (method == "initialize")
                return JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
            if (method == "tools/list")
                return JsonResponse("""
                    {"jsonrpc":"2.0","id":2,"result":{"tools":[
                      {"name":"get_accounts","inputSchema":{"type":"object","properties":{}}},
                      {"name":"get_portfolio","inputSchema":{"type":"object","properties":{"account_number":{"type":"string"}}}},
                      {"name":"get_equity_positions","inputSchema":{"type":"object","properties":{"account_number":{"type":"string"}}}}
                    ]}}
                    """);

            var parameters = root.GetProperty("params");
            var tool = parameters.GetProperty("name").GetString();
            if (tool == "get_accounts")
                return ToolResponse("""{"accounts":[{"account_number":"899993299","account_type":"cash"},{"account_number":"837197250","account_type":"cash"}]}""");

            var accountNumber = parameters.GetProperty("arguments").GetProperty("account_number").GetString();
            if (tool == "get_portfolio")
                return accountNumber == "899993299"
                    ? ToolResponse("""{"total_value":"0.07","cash":"0.07","buying_power":"0.0700"}""")
                    : ToolResponse("""{"total_value":"850","cash":"850","buying_power":"850.0000"}""");
            if (tool == "get_equity_positions")
                return ToolResponse("""{"positions":[]}""");

            throw new InvalidOperationException($"Unexpected MCP tool {tool}");
        }

        private static HttpResponseMessage ToolResponse(string structuredContent) =>
            JsonResponse(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new { structuredContent = JsonSerializer.Deserialize<JsonElement>(structuredContent) }
            }));

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
