using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace TradeMASter.Infrastructure.Brokers.Robinhood;

internal sealed record RobinhoodMcpTool(string Name, string? Description, JsonElement InputSchema);

public sealed class RobinhoodMcpClient
{
    private const string ProtocolVersion = "2025-03-26";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _serverUri;
    private int _nextRequestId;
    private string? _sessionId;
    private string? _accessToken;

    public RobinhoodMcpClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _serverUri = new Uri(configuration["Robinhood:McpServerUrl"]
            ?? "https://agent.robinhood.com/mcp/trading");
    }

    internal async Task InitializeAsync(string accessToken, CancellationToken cancellationToken)
    {
        _sessionId = null;
        _accessToken = accessToken;
        var result = await SendRequestAsync("initialize", new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { },
            clientInfo = new { name = "TradeMASter", version = "1.0.0" }
        }, cancellationToken);

        if (!result.TryGetProperty("protocolVersion", out _))
        {
            throw new InvalidOperationException("Robinhood MCP returned an invalid initialize response.");
        }

        await SendNotificationAsync("notifications/initialized", new { }, cancellationToken);
    }

    internal async Task<IReadOnlyList<RobinhoodMcpTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("tools/list", new { }, cancellationToken);
        if (!result.TryGetProperty("tools", out var toolsElement) || toolsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Robinhood MCP did not return a tools list.");
        }

        var tools = new List<RobinhoodMcpTool>();
        foreach (var tool in toolsElement.EnumerateArray())
        {
            var name = tool.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var description = tool.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;
            var schema = tool.TryGetProperty("inputSchema", out var schemaElement)
                ? schemaElement.Clone()
                : JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();

            tools.Add(new RobinhoodMcpTool(name, description, schema));
        }

        return tools;
    }

    internal Task<JsonElement> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        return SendRequestAsync("tools/call", new { name, arguments }, cancellationToken);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var payload = new { jsonrpc = "2.0", id, method, @params = parameters };
        using var response = await SendAsync(payload, cancellationToken);
        var envelope = await ReadEnvelopeAsync(response, cancellationToken);

        if (envelope.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : error.GetRawText();
            throw new InvalidOperationException($"Robinhood MCP {method} failed: {message}");
        }

        if (!envelope.TryGetProperty("result", out var result))
        {
            throw new InvalidOperationException($"Robinhood MCP {method} returned no result.");
        }

        return result.Clone();
    }

    private async Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var payload = new { jsonrpc = "2.0", method, @params = parameters };
        using var response = await SendAsync(payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Robinhood MCP {method} failed ({(int)response.StatusCode}): {body}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _serverUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new UnauthorizedAccessException("Robinhood MCP authorization expired or was revoked.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException(
                $"Robinhood MCP request failed ({(int)status}): {body}",
                null,
                status);
        }

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
        {
            _sessionId = sessionValues.FirstOrDefault() ?? _sessionId;
        }

        return response;
    }

    private static async Task<JsonElement> ReadEnvelopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Robinhood MCP returned an empty response.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var dataLines = body.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[5..].Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            foreach (var data in dataLines)
            {
                using var eventDocument = JsonDocument.Parse(data);
                if (eventDocument.RootElement.TryGetProperty("result", out _)
                    || eventDocument.RootElement.TryGetProperty("error", out _))
                {
                    return eventDocument.RootElement.Clone();
                }
            }

            throw new InvalidOperationException("Robinhood MCP returned no JSON-RPC event data.");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
