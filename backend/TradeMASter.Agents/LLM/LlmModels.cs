using TradeMASter.Core.Common;

namespace TradeMASter.Agents.LLM;

public enum LlmRole
{
    System,
    User,
    Assistant
}

public record ChatMessage(LlmRole Role, string Content);

public record LlmRequest(
    List<ChatMessage> Messages,
    string? Model = null,
    double Temperature = 0.2,
    int MaxTokens = 1500,
    bool JsonMode = false,
    bool EnableWebSearch = false);

public record LlmResponse(
    string Content,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    string? FinishReason = null);

public interface ILlmClient
{
    string ProviderName { get; }
    Task<Result<LlmResponse>> GenerateCompletionAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
