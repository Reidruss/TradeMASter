using System.Text.Json;
using TradeMASter.Agents.LLM;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Agents.Personas;

public record DebateMessage(string SpeakerRole, string SpeakerName, string Content, DateTime TimestampUtc);

public record ConsensusSynthesis(
    DecisionVerdict Verdict,
    double CompositeConfidence,
    string ConsensusSummary,
    OrderRequest? ProposedOrder,
    IReadOnlyList<DebateMessage> DebateLog);

public class PortfolioArbiter : IAgentPersona
{
    public AgentRole Role => AgentRole.PortfolioArbiter;
    public string PersonaName => "Asset Selection & Candidate Screener";
    private readonly ILlmClient _llm;

    public PortfolioArbiter(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<AgentDecision> AnalyzeAsync(MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        return new AgentDecision(
            Guid.Empty,
            context.Symbol,
            Role,
            SignalDirection.Neutral,
            0.80,
            "Portfolio Arbiter initialized for committee debate facilitation and synthesis.",
            "[]"
        );
    }

    public async Task<ConsensusSynthesis> SynthesizeConsensusAsync(
        MarketAnalysisContext context,
        AgentDecision techDecision,
        AgentDecision fundDecision,
        AgentDecision sentDecision,
        RiskAuditEvaluation riskAudit,
        IReadOnlyList<DebateMessage> crossExamLog,
        CancellationToken cancellationToken = default)
    {
        // 1. Calculate numeric score (-1.0 to 1.0)
        var techScore = DirectionToScore(techDecision.Direction);
        var fundScore = DirectionToScore(fundDecision.Direction);
        var sentScore = DirectionToScore(sentDecision.Direction);

        // Weights: Tech 35%, Fund 35%, Sent 30%
        var weightedScore = (techScore * 0.35) + (fundScore * 0.35) + (sentScore * 0.30);
        var avgConfidence = (techDecision.ConfidenceScore + fundDecision.ConfidenceScore + sentDecision.ConfidenceScore) / 3.0;

        DecisionVerdict verdict;
        if (!riskAudit.IsApproved)
        {
            verdict = DecisionVerdict.Vetoed;
        }
        else if (weightedScore >= 0.30)
        {
            verdict = DecisionVerdict.Buy;
        }
        else if (weightedScore <= -0.30)
        {
            verdict = DecisionVerdict.Sell;
        }
        else
        {
            verdict = DecisionVerdict.Hold;
        }

        // Generate Arbiter Synthesis
        var prompt = $@"You are the Lead Portfolio Arbiter moderating the trading committee for {context.Symbol}.

Agent Assessments:
- Technical Analyst: {techDecision.Direction} (Confidence: {techDecision.ConfidenceScore:P0}) - {techDecision.Reasoning}
- Fundamental Analyst: {fundDecision.Direction} (Confidence: {fundDecision.ConfidenceScore:P0}) - {fundDecision.Reasoning}
- Sentiment Analyst: {sentDecision.Direction} (Confidence: {sentDecision.ConfidenceScore:P0}) - {sentDecision.Reasoning}
- Risk Guard: {(riskAudit.IsApproved ? "APPROVED" : "VETOED")} - {riskAudit.RiskNotes}

Weighted Committee Score: {weightedScore:F2}
Final Verdict: {verdict}

Provide a crisp, authoritative executive consensus summary (2-3 sentences) balancing all perspectives and outlining the committee's final directive.";

        var llmRes = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are an elite hedge fund chief investment officer synthesizing multi-analyst research."),
            new(LlmRole.User, prompt)
        }), cancellationToken);

        var summary = llmRes.IsSuccess && !string.IsNullOrWhiteSpace(llmRes.Value.Content)
            ? llmRes.Value.Content.Trim()
            : (verdict == DecisionVerdict.Vetoed)
            ? $"Committee favored action, but order was VETOED by Risk Guard: {riskAudit.VetoReason}"
            : $"Committee consensus reached verdict {verdict} (Composite confidence: {avgConfidence:P0}). Technical alignment and fundamental backing support execution with risk parameters configured.";

        OrderRequest? proposedOrder = null;
        if (verdict == DecisionVerdict.Buy && riskAudit.IsApproved)
        {
            // Sizing: up to 5% of equity or max allowed quantity
            var targetSizingVal = context.Portfolio.TotalEquity * 0.05m;
            var calculatedQty = Math.Max(1m, Math.Floor(targetSizingVal / context.Quote.Price));
            var finalQty = Math.Min(calculatedQty, riskAudit.MaxAllowedQuantity);

            if (finalQty > 0)
            {
                proposedOrder = new OrderRequest(
                    context.Portfolio.Id,
                    context.Symbol,
                    OrderSide.Buy,
                    OrderType.Market,
                    finalQty,
                    StopPrice: riskAudit.RecommendedStopLossPrice
                );
            }
        }
        else if (verdict == DecisionVerdict.Sell && riskAudit.IsApproved)
        {
            var existingPos = context.Portfolio.Positions.FirstOrDefault(p => p.Symbol.Equals(context.Symbol, StringComparison.OrdinalIgnoreCase));
            if (existingPos != null && existingPos.Quantity > 0)
            {
                proposedOrder = new OrderRequest(
                    context.Portfolio.Id,
                    context.Symbol,
                    OrderSide.Sell,
                    OrderType.Market,
                    existingPos.Quantity
                );
            }
        }

        var fullDebateLog = new List<DebateMessage>();
        fullDebateLog.Add(new DebateMessage("TechnicalAnalyst", "Technical Analyst", $"Initial Thesis: {techDecision.Reasoning}", DateTime.UtcNow.AddSeconds(-20)));
        fullDebateLog.Add(new DebateMessage("FundamentalAnalyst", "Fundamental Analyst", $"Initial Thesis: {fundDecision.Reasoning}", DateTime.UtcNow.AddSeconds(-16)));
        fullDebateLog.Add(new DebateMessage("SentimentAnalyst", "Sentiment Analyst", $"Initial Thesis: {sentDecision.Reasoning}", DateTime.UtcNow.AddSeconds(-12)));
        fullDebateLog.AddRange(crossExamLog);
        fullDebateLog.Add(new DebateMessage("RiskAuditor", "Risk Guard", $"Audit Result: {(riskAudit.IsApproved ? "APPROVED" : "VETOED")} - {riskAudit.RiskNotes}", DateTime.UtcNow.AddSeconds(-4)));
        fullDebateLog.Add(new DebateMessage("PortfolioArbiter", "Portfolio Arbiter", $"Final Synthesis: {summary}", DateTime.UtcNow));

        return new ConsensusSynthesis(
            verdict,
            avgConfidence,
            summary,
            proposedOrder,
            fullDebateLog
        );
    }

    public Task<string> DefendThesisAsync(string challenge, MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Portfolio Arbiter resolves cross-examination conflicts and establishes alignment with macro risk targets.");
    }

    private static double DirectionToScore(SignalDirection dir) => dir switch
    {
        SignalDirection.StrongBuy => 1.0,
        SignalDirection.Bullish => 0.6,
        SignalDirection.Neutral => 0.0,
        SignalDirection.Bearish => -0.6,
        SignalDirection.StrongSell => -1.0,
        _ => 0.0
    };
}
