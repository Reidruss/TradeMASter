using System.Text.Json;
using TradeMASter.Core.Enums;

namespace TradeMASter.Agents.LLM;

internal sealed record StructuredAgentDecision(
    SignalDirection Direction,
    double Confidence,
    string Reasoning,
    IReadOnlyList<string> KeyFactors);

internal static class LlmStructuredDecision
{
    public static bool TryParse(string content, out StructuredAgentDecision decision)
    {
        decision = default!;
        try
        {
            var text = content.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = text.IndexOf('\n');
                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline) text = text[(firstNewline + 1)..lastFence].Trim();
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var directionText = root.GetProperty("direction").GetString();
            if (!Enum.TryParse<SignalDirection>(directionText, true, out var direction)) return false;
            var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                ? confidenceElement.GetDouble()
                : 0.5;
            var reasoning = root.TryGetProperty("reasoning", out var reasoningElement)
                ? reasoningElement.GetString() ?? string.Empty
                : string.Empty;
            var factors = root.TryGetProperty("keyFactors", out var factorsElement)
                && factorsElement.ValueKind == JsonValueKind.Array
                    ? factorsElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty)
                        .Where(item => !string.IsNullOrWhiteSpace(item)).ToList()
                    : new List<string>();
            if (string.IsNullOrWhiteSpace(reasoning)) return false;
            decision = new StructuredAgentDecision(direction, Math.Clamp(confidence, 0, 1), reasoning, factors);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
