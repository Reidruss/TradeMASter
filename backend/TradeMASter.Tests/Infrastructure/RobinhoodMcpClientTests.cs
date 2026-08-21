using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TradeMASter.Infrastructure.Brokers.Robinhood;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class RobinhoodMcpClientTests
{
    [Fact]
    public async Task InitializeAndListTools_UsesBearerSessionAndParsesSse()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHandler((request, index) =>
        {
            requests.Add(CloneRequest(request));
            return index switch
            {
                0 => JsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""",
                    ("Mcp-Session-Id", "session-123")),
                1 => new HttpResponseMessage(HttpStatusCode.Accepted),
                2 => SseResponse("""{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"get_accounts","description":"Read accounts","inputSchema":{"type":"object"}}]}}"""),
                _ => throw new InvalidOperationException("Unexpected request")
            };
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Robinhood:McpServerUrl"] = "https://agent.robinhood.com/mcp/trading"
            })
            .Build();
        var client = new RobinhoodMcpClient(new HttpClient(handler), configuration);

        await client.InitializeAsync("secret-token", CancellationToken.None);
        var tools = await client.ListToolsAsync(CancellationToken.None);

        tools.Should().ContainSingle(tool => tool.Name == "get_accounts");
        requests.Should().HaveCount(3);
        requests.Should().OnlyContain(request => request.Headers.Authorization!.Scheme == "Bearer");
        requests.Should().OnlyContain(request => request.Headers.Authorization!.Parameter == "secret-token");
        requests[1].Headers.GetValues("Mcp-Session-Id").Should().ContainSingle("session-123");
        requests[2].Headers.GetValues("Mcp-Session-Id").Should().ContainSingle("session-123");
    }

    [Fact]
    public async Task Initialize_WhenUnauthorized_DoesNotExposeTokenInError()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new RobinhoodMcpClient(new HttpClient(handler), new ConfigurationBuilder().Build());

        var action = () => client.InitializeAsync("do-not-leak", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<UnauthorizedAccessException>();
        exception.Which.Message.Should().NotContain("do-not-leak");
    }

    private static HttpResponseMessage JsonResponse(string json, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    private static HttpResponseMessage SseResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"event: message\ndata: {json}\n\n", Encoding.UTF8, "text/event-stream")
    };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, _index++));
    }
}
