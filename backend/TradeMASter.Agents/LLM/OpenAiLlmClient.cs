using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TradeMASter.Core.Common;

namespace TradeMASter.Agents.LLM;

public sealed class OpenAiLlmClient : ILlmClient
{
    public string ProviderName => "OpenAI";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly bool _enableWebSearch;

    public OpenAiLlmClient(
        HttpClient httpClient,
        string? apiKey = null,
        string defaultModel = "gpt-5.6-terra",
        bool enableWebSearch = true)
    {
        _httpClient = httpClient;
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        _defaultModel = defaultModel;
        _enableWebSearch = enableWebSearch;
        _httpClient.BaseAddress ??= new Uri("https://api.openai.com/v1/");
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<Result<LlmResponse>> GenerateCompletionAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return Result.Failure<LlmResponse>("OpenAI API key is missing. Set OPENAI_API_KEY.");
        }

        try
        {
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = request.Model ?? _defaultModel,
                ["input"] = request.Messages.Select(message => new
                {
                    role = message.Role.ToString().ToLowerInvariant(),
                    content = message.Content
                }).ToArray(),
                ["max_output_tokens"] = request.MaxTokens,
                ["reasoning"] = new { effort = "medium" },
                ["text"] = request.JsonMode
                    ? new { format = new { type = "json_object" }, verbosity = "medium" }
                    : (object)new { verbosity = "medium" }
            };

            if (_enableWebSearch && request.EnableWebSearch)
            {
                requestBody["tools"] = new[] { new { type = "web_search" } };
            }

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<LlmResponse>($"OpenAI error ({(int)response.StatusCode}): {SanitizeError(responseBody)}");
            }

            using var document = JsonDocument.Parse(responseBody);
            var content = ExtractOutputText(document.RootElement);
            var finishReason = document.RootElement.TryGetProperty("status", out var status)
                ? status.GetString()
                : null;

            int? promptTokens = null;
            int? completionTokens = null;
            if (document.RootElement.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("input_tokens", out var inputTokens)
                    ? inputTokens.GetInt32()
                    : null;
                completionTokens = usage.TryGetProperty("output_tokens", out var outputTokens)
                    ? outputTokens.GetInt32()
                    : null;
            }

            return string.IsNullOrWhiteSpace(content)
                ? Result.Failure<LlmResponse>("OpenAI returned no text output.")
                : Result.Success(new LlmResponse(content, promptTokens, completionTokens, finishReason));
        }
        catch (Exception ex)
        {
            return Result.Failure<LlmResponse>($"Exception calling OpenAI Responses API: {ex.Message}");
        }
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && contentItem.TryGetProperty("text", out var text)
                    && !string.IsNullOrWhiteSpace(text.GetString()))
                {
                    parts.Add(text.GetString()!);
                }
            }
        }
        return string.Join("\n", parts);
    }

    private static string SanitizeError(string body)
    {
        if (body.Length <= 1000) return body;
        return body[..1000] + "…";
    }
}
