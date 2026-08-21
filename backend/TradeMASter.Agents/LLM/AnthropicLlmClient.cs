using System.Text;
using System.Text.Json;
using TradeMASter.Core.Common;

namespace TradeMASter.Agents.LLM;

public class AnthropicLlmClient : ILlmClient
{
    public string ProviderName => "Anthropic";
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _defaultModel;

    public AnthropicLlmClient(HttpClient httpClient, string? apiKey = null, string defaultModel = "claude-3-5-sonnet-20241022")
    {
        _httpClient = httpClient;
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty;
        _defaultModel = defaultModel;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        }
    }

    public async Task<Result<LlmResponse>> GenerateCompletionAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return Result.Failure<LlmResponse>("Anthropic API Key is missing. Set ANTHROPIC_API_KEY environment variable.");
        }

        try
        {
            var systemMsg = request.Messages.FirstOrDefault(m => m.Role == LlmRole.System)?.Content;
            var nonSystemMsgs = request.Messages
                .Where(m => m.Role != LlmRole.System)
                .Select(m => new
                {
                    role = m.Role == LlmRole.User ? "user" : "assistant",
                    content = m.Content
                }).ToList();

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = request.Model ?? _defaultModel,
                ["messages"] = nonSystemMsgs,
                ["max_tokens"] = request.MaxTokens,
                ["temperature"] = request.Temperature
            };

            if (!string.IsNullOrWhiteSpace(systemMsg))
            {
                requestBody["system"] = systemMsg;
            }

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Add("x-api-key", _apiKey);
            requestMessage.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<LlmResponse>($"Anthropic error ({response.StatusCode}): {errorBody}");
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var contentBlocks = doc.RootElement.GetProperty("content");
            var text = string.Join("\n", contentBlocks.EnumerateArray()
                .Where(b => b.GetProperty("type").GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()));

            return Result.Success(new LlmResponse(text));
        }
        catch (Exception ex)
        {
            return Result.Failure<LlmResponse>($"Exception calling Anthropic API: {ex.Message}");
        }
    }
}
