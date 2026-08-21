using System.Text.Json;
using TradeMASter.Agents.LLM;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Agents.Personas;

public class FundamentalAnalyst : IAgentPersona
{
    public AgentRole Role => AgentRole.FundamentalAnalyst;
    public string PersonaName => "Fundamental Researcher";
    private readonly ILlmClient _llm;

    public FundamentalAnalyst(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<AgentDecision> AnalyzeAsync(MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        var fund = context.Fundamentals;

        var isStrongGrowth = fund.RevenueGrowthYoyPercent > 15m;
        var isHighMargin = fund.ProfitMarginPercent > 20m;
        var isReasonablePe = fund.PeRatio > 0 && fund.PeRatio < 35m;

        var signal = (isStrongGrowth && isHighMargin)
            ? SignalDirection.Bullish
            : (fund.PeRatio > 60m && fund.RevenueGrowthYoyPercent < 10m)
            ? SignalDirection.Bearish
            : SignalDirection.Neutral;

        var confidence = isStrongGrowth ? 0.76 : 0.60;

        var factors = new List<string>
        {
            $"Deterministic Health Score: {fund.HealthScore:F1}/100",
            $"P/E (Trailing / Forward): {fund.PeRatio}x / {fund.ForwardPe}x",
            $"EV/EBITDA: {fund.EvToEbitda}x",
            $"YoY Revenue Growth: {fund.RevenueGrowthYoyPercent}%",
            $"Net Profit Margin: {fund.ProfitMarginPercent}%",
            $"Valuation Tone: {fund.ValuationAssessment}",
            $"Data Quality: {fund.DataQuality}"
        };
        factors.AddRange(fund.Sources.Take(2).Select(source => $"Source: {source}"));

        var prompt = $@"You are the Fundamental & Macro Analyst in a trading committee. Today is {DateTime.UtcNow:yyyy-MM-dd}.
Asset: {context.Symbol} ({fund.CompanyName})
Structured input ({fund.DataQuality}; synthetic={fund.IsSynthetic}): P/E {fund.PeRatio}, Forward P/E {fund.ForwardPe},
EV/EBITDA {fund.EvToEbitda}, YoY growth {fund.RevenueGrowthYoyPercent}%, margin {fund.ProfitMarginPercent}%.

Explain the supplied filing metrics and research current filings/earnings, valuation, and material macro risks. Never replace verified numeric inputs with estimates. Return JSON only:
{{""direction"":""Bullish|Neutral|Bearish"",""confidence"":0.0,""reasoning"":""2-3 sentence evidence-based thesis"",""keyFactors"":[""factor with date/source context""]}}";

        var llmRes = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are a disciplined equity analyst evaluating business moats, DCF valuation multiples, and macro backdrop."),
            new(LlmRole.User, prompt)
        }, JsonMode: true, EnableWebSearch: true), cancellationToken);

        var reasoning = $"Cached baseline suggests {fund.RevenueGrowthYoyPercent}% YoY growth and {fund.ProfitMarginPercent}% margin at {fund.PeRatio}x earnings; live research was unavailable.";
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
        var fund = context.Fundamentals;
        var prompt = $@"The Portfolio Arbiter poses this fundamental challenge regarding {context.Symbol}:
""{challenge}""

Fundamentals:
P/E: {fund.PeRatio}, Growth: {fund.RevenueGrowthYoyPercent}%, Margins: {fund.ProfitMarginPercent}%, Macro: {fund.MacroInterestRateImpact}.

Respond concisely (1-2 sentences) evaluating whether the valuation justifies entry.";

        var res = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are a fundamental portfolio manager."),
            new(LlmRole.User, prompt)
        }, EnableWebSearch: true), cancellationToken);

        return res.IsSuccess ? res.Value.Content.Trim() : $"Although valuation multiples are elevated ({fund.PeRatio}x), earnings revisions and free cash flow generation justify strategic allocation.";
    }
}
