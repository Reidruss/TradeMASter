using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Brokers.Robinhood;
using TradeMASter.Infrastructure.Persistence;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class RobinhoodLiveExecutionAdapterTests
{
    [Fact]
    public async Task OrderHistory_ParsesLifecycleFieldsAndSanitizesBrokerPayload()
    {
        var clientOrderId = Guid.NewGuid();
        var handler = new McpHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var method = document.RootElement.GetProperty("method").GetString();
            if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (method == "initialize") return JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
            if (method == "tools/list") return JsonResponse("""
                {"jsonrpc":"2.0","id":2,"result":{"tools":[
                  {"name":"get_equity_orders","inputSchema":{"type":"object","properties":{"account_number":{"type":"string"},"created_at_start":{"type":"string"}}}}
                ]}}
                """);
            return JsonResponse($$$$"""
                {"jsonrpc":"2.0","id":3,"result":{"structuredContent":{"orders":[{
                  "order_id":"broker-42","client_order_id":"{{{{clientOrderId}}}}","symbol":"F","side":"buy",
                  "quantity":"2","filled_quantity":"1.25","average_price":"20.01","limit_price":"20.05",
                  "state":"partially_filled","created_at":"2026-08-20T14:00:00Z","updated_at":"2026-08-20T14:01:00Z",
                  "account_number":"837197250","private_note":"never persist"
                }]}}}
                """);
        });
        var (adapter, db) = await AdapterAsync(handler);
        await using var ownedDb = db;

        var result = await adapter.GetOrderHistoryAsync(DateTime.Parse("2026-08-20T13:00:00Z"));

        result.IsSuccess.Should().BeTrue(result.Error);
        var order = result.Value.Orders.Should().ContainSingle().Subject;
        order.BrokerOrderId.Should().Be("broker-42");
        order.ClientOrderId.Should().Be(clientOrderId);
        order.FilledQuantity.Should().Be(1.25m);
        order.AverageFillPrice.Should().Be(20.01m);
        order.State.Should().Be("partially_filled");
        order.SanitizedPayloadJson.Should().NotContain("837197250").And.NotContain("private_note");
    }

    [Fact]
    public async Task CancelOrder_BindsExactBrokerOrderIdAndReturnsSanitizedState()
    {
        JsonElement capturedArguments = default;
        var handler = new McpHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var method = document.RootElement.GetProperty("method").GetString();
            if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (method == "initialize") return JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
            if (method == "tools/list") return JsonResponse("""
                {"jsonrpc":"2.0","id":2,"result":{"tools":[
                  {"name":"cancel_equity_order","inputSchema":{"type":"object","required":["account_number","order_id"],"properties":{"account_number":{"type":"string"},"order_id":{"type":"string"}}}}
                ]}}
                """);
            capturedArguments = document.RootElement.GetProperty("params").GetProperty("arguments").Clone();
            return JsonResponse("""{"jsonrpc":"2.0","id":3,"result":{"structuredContent":{"order_id":"broker-42","state":"cancel_pending","account_number":"837197250"}}}""");
        });
        var (adapter, db) = await AdapterAsync(handler);
        await using var ownedDb = db;

        var result = await adapter.CancelOrderAsync("broker-42");

        result.Outcome.Should().Be(BrokerSubmissionOutcome.Accepted);
        result.BrokerState.Should().Be("cancel_pending");
        capturedArguments.GetProperty("order_id").GetString().Should().Be("broker-42");
        result.SanitizedResponseJson.Should().NotContain("837197250");
    }

    [Fact]
    public async Task PlaceOrder_SendsStableClientOrderIdAndReturnsOnlySanitizedReceipt()
    {
        JsonElement capturedArguments = default;
        var handler = new McpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();
            if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (method == "initialize") return JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
            if (method == "tools/list") return JsonResponse("""
                {"jsonrpc":"2.0","id":2,"result":{"tools":[
                  {"name":"place_equity_order","inputSchema":{"type":"object","required":["account_number","client_order_id","symbol","side","type","quantity","limit_price","time_in_force"],"properties":{
                    "account_number":{"type":"string"},"client_order_id":{"type":"string"},"symbol":{"type":"string"},
                    "side":{"type":"string"},"type":{"type":"string"},"quantity":{"type":"string"},
                    "limit_price":{"type":"string"},"time_in_force":{"type":"string"}}}}
                ]}}
                """);
            capturedArguments = root.GetProperty("params").GetProperty("arguments").Clone();
            return JsonResponse("""{"jsonrpc":"2.0","id":3,"result":{"structuredContent":{"order_id":"broker-123","state":"open","account_number":"837197250","internal_notes":"do-not-persist"}}}""");
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Robinhood:McpServerUrl"] = "https://agent.robinhood.com/mcp/trading"
        }).Build();
        var provider = new EphemeralDataProtectionProvider();
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var token = provider.CreateProtector("TradeMASter.RobinhoodOAuthTokens.v1").Protect("secret-token");
        db.RobinhoodSessions.Add(new RobinhoodSession("837197250", token, null, "client", DateTime.UtcNow.AddHours(1), "Agentic", false));
        await db.SaveChangesAsync();
        await using var ownedDb = db;
        var adapter = new RobinhoodLiveExecutionAdapter(
            Mock.Of<IRobinhoodService>(),
            new RobinhoodMcpClient(new HttpClient(handler), configuration),
            db,
            provider);
        var clientOrderId = Guid.NewGuid();
        var key = new string('a', 64);

        var receipt = await adapter.PlaceOrderAsync(new BrokerOrderCommand(
            "837197250", clientOrderId, key, "F", OrderSide.Buy, OrderType.Limit, 2m, 20m));

        receipt.Outcome.Should().Be(BrokerSubmissionOutcome.Accepted);
        receipt.BrokerOrderId.Should().Be("broker-123");
        capturedArguments.GetProperty("client_order_id").GetString().Should().Be(clientOrderId.ToString());
        capturedArguments.GetProperty("quantity").GetString().Should().Be("2");
        capturedArguments.GetProperty("limit_price").GetString().Should().Be("20");
        receipt.SanitizedResponseJson.Should().Contain("broker-123")
            .And.NotContain("837197250")
            .And.NotContain("internal_notes")
            .And.NotContain("secret-token");
    }

    [Fact]
    public async Task PlaceOrder_FailsClosedWhenBrokerSchemaHasNoClientOrderId()
    {
        var handler = new McpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (method == "initialize") return JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
            return JsonResponse("""{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"place_equity_order","inputSchema":{"type":"object","properties":{"symbol":{"type":"string"}}}}]}}""");
        });
        var provider = new EphemeralDataProtectionProvider();
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.RobinhoodSessions.Add(new RobinhoodSession(
            "837197250", provider.CreateProtector("TradeMASter.RobinhoodOAuthTokens.v1").Protect("secret"),
            null, "client", DateTime.UtcNow.AddHours(1), "Agentic", false));
        await db.SaveChangesAsync();
        await using var ownedDb = db;
        var adapter = new RobinhoodLiveExecutionAdapter(
            Mock.Of<IRobinhoodService>(),
            new RobinhoodMcpClient(new HttpClient(handler), new ConfigurationBuilder().Build()), db, provider);

        var receipt = await adapter.PlaceOrderAsync(new BrokerOrderCommand(
            "837197250", Guid.NewGuid(), new string('a', 64), "F", OrderSide.Buy, OrderType.Limit, 1m, 20m));

        receipt.Outcome.Should().Be(BrokerSubmissionOutcome.Unknown);
        receipt.Message.Should().Contain("idempotency capability disappeared");
    }

    [Fact]
    public async Task PlaceOrder_TimeoutIsAmbiguousAndNeverReportedAsRejectedOrSafeToRetry()
    {
        var handler = new McpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (method == "initialize") return JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
            if (method == "tools/list") return JsonResponse("""
                {"jsonrpc":"2.0","id":2,"result":{"tools":[
                  {"name":"place_equity_order","inputSchema":{"type":"object","required":["account_number","client_order_id","symbol","side","type","quantity","limit_price","time_in_force"],"properties":{
                    "account_number":{"type":"string"},"client_order_id":{"type":"string"},"symbol":{"type":"string"},
                    "side":{"type":"string"},"type":{"type":"string"},"quantity":{"type":"string"},
                    "limit_price":{"type":"string"},"time_in_force":{"type":"string"}}}}
                ]}}
                """);
            throw new TaskCanceledException("simulated timeout after the request left the process");
        });
        var provider = new EphemeralDataProtectionProvider();
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.RobinhoodSessions.Add(new RobinhoodSession(
            "837197250", provider.CreateProtector("TradeMASter.RobinhoodOAuthTokens.v1").Protect("secret"),
            null, "client", DateTime.UtcNow.AddHours(1), "Agentic", false));
        await db.SaveChangesAsync();
        await using var ownedDb = db;
        var adapter = new RobinhoodLiveExecutionAdapter(
            Mock.Of<IRobinhoodService>(),
            new RobinhoodMcpClient(new HttpClient(handler), new ConfigurationBuilder().Build()), db, provider);

        var receipt = await adapter.PlaceOrderAsync(new BrokerOrderCommand(
            "837197250", Guid.NewGuid(), new string('a', 64), "F", OrderSide.Buy, OrderType.Limit, 1m, 20m));

        receipt.Outcome.Should().Be(BrokerSubmissionOutcome.Unknown);
        receipt.BrokerOrderId.Should().BeNull();
        receipt.Message.Should().Contain("could not be proven").And.Contain("reconciliation");
        receipt.SanitizedResponseJson.Should().NotContain("837197250").And.NotContain("simulated timeout");
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static async Task<(RobinhoodLiveExecutionAdapter Adapter, TradeMASterDbContext Db)> AdapterAsync(McpHandler handler)
    {
        var provider = new EphemeralDataProtectionProvider();
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.RobinhoodSessions.Add(new RobinhoodSession(
            "837197250", provider.CreateProtector("TradeMASter.RobinhoodOAuthTokens.v1").Protect("secret"),
            null, "client", DateTime.UtcNow.AddHours(1), "Agentic", false));
        await db.SaveChangesAsync();
        var service = new Mock<IRobinhoodService>();
        service.Setup(item => item.GetExecutionAccountSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RobinhoodExecutionAccountSnapshot(
                "837197250", "cash", 850m, 850m, 850m, DateTime.UtcNow, false, [])));
        var adapter = new RobinhoodLiveExecutionAdapter(
            service.Object,
            new RobinhoodMcpClient(new HttpClient(handler), new ConfigurationBuilder().Build()),
            db,
            provider);
        return (adapter, db);
    }

    private sealed class McpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request);
    }
}
