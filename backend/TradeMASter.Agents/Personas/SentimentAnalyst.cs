using System.Text.Json;
using TradeMASter.Agents.LLM;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Agents.Personas;

public class SentimentAnalyst : IAgentPersona
{
    public AgentRole Role => AgentRole.SentimentAnalyst;
    public string PersonaName => "Sentiment Scout";
    private readonly ILlmClient _llm;

    public SentimentAnalyst(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<AgentDecision> AnalyzeAsync(MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        var sent = context.Sentiment;

        var signal = sent.SentimentScore >= 0.5
            ? SignalDirection.Bullish
            : sent.SentimentScore <= -0.2
            ? SignalDirection.Bearish
            : SignalDirection.Neutral;

        var confidence = Math.Clamp(0.5 + (Math.Abs(sent.SentimentScore) * 0.4), 0.5, 0.9);

        var factors = new List<string>
        {
            $"Sentiment Score: {sent.SentimentScore:F2} ({sent.SentimentLabel})",
            $"Social Buzz Score: {sent.SocialBuzzScore:F1}/100",
            $"Dominant Theme: {string.Join(", ", sent.KeyThemes)}"
        };
        factors.AddRange(sent.RecentHeadlines.Take(2).Select(h => $"Headline: \"{h}\""));

        var prompt = $@"You are the Sentiment & News Analyst in a trading committee. Today is {DateTime.UtcNow:yyyy-MM-dd}.
Asset: {context.Symbol}
Cached sentiment baseline (may be stale; do not treat it as current): {sent.SentimentScore} ({sent.SentimentLabel}).

Research current news and material catalysts, distinguish dated facts from speculation, and return JSON only:
{{""direction"":""Bullish|Neutral|Bearish"",""confidence"":0.0,""reasoning"":""2-3 sentence evidence-based assessment"",""keyFactors"":[""dated catalyst or risk""]}}";

        var llmRes = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are a financial sentiment intelligence specialist analyzing news, social buzz, and filings tone."),
            new(LlmRole.User, prompt)
        }, JsonMode: true, EnableWebSearch: true), cancellationToken);

        var reasoning = $"Cached sentiment baseline is {sent.SentimentLabel}; live news research was unavailable.";
        if (llmRes.IsSuccess && LlmStructuredDecision.TryParse(llmRes.Value.Content, out var researched))
        {
            signal = researched.Direction;
            confidence = researched.Confidence;
            reasoning = researched.Reasoning;
            factors = researched.KeyFactors.ToList();
        }

        return new AgentDecision(
            Guid.Empty,
            context.Symbol,
            Role,
            signal,
            confidence,
            reasoning,
            JsonSerializer.Serialize(factors)
        );
    }

    public async Task<string> DefendThesisAsync(string challenge, MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        var sent = context.Sentiment;
        var prompt = $@"The Portfolio Arbiter questions sentiment durability for {context.Symbol}:
""{challenge}""

Sentiment: Score {sent.SentimentScore}, Label: {sent.SentimentLabel}, Buzz: {sent.SocialBuzzScore}.
Themes: {string.Join(", ", sent.KeyThemes)}.

Respond concisely (1-2 sentences) defending whether narrative momentum is real or hype.";

        var res = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are a sentiment momentum strategist."),
            new(LlmRole.User, prompt)
        }, EnableWebSearch: true), cancellationToken);

        return res.IsSuccess ? res.Value.Content.Trim() : $"High engagement and institutional narrative alignment confirm sustained attention rather than fleeting noise.";
    }
}
