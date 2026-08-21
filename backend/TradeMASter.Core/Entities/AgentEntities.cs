using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Entities;

public class AgentDecision : BaseEntity
{
    public Guid DeliberationSessionId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public AgentRole Role { get; set; }
    public SignalDirection Direction { get; set; }
    public double ConfidenceScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string KeyFactorsJson { get; set; } = "[]";
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    public AgentDecision() { }

    public AgentDecision(
        Guid deliberationSessionId,
        string symbol,
        AgentRole role,
        SignalDirection direction,
        double confidenceScore,
        string reasoning,
        string keyFactorsJson = "[]")
    {
        DeliberationSessionId = deliberationSessionId;
        Symbol = symbol.ToUpperInvariant();
        Role = role;
        Direction = direction;
        ConfidenceScore = Math.Clamp(confidenceScore, 0.0, 1.0);
        Reasoning = reasoning;
        KeyFactorsJson = keyFactorsJson;
        EvaluatedAt = DateTime.UtcNow;
    }
}

public class DeliberationSession : BaseEntity
{
    public string Symbol { get; set; } = string.Empty;
    public List<AgentDecision> Decisions { get; set; } = new();
    public string FinalConsensusSummary { get; set; } = string.Empty;
    public DecisionVerdict FinalVerdict { get; set; } = DecisionVerdict.Hold;
    public double OverallConfidence { get; set; }
    public bool IsRiskApproved { get; set; }
    public string? RiskNotes { get; set; }
    public Guid? ExecutedOrderId { get; set; }

    public DeliberationSession() { }

    public DeliberationSession(string symbol)
    {
        Symbol = symbol.ToUpperInvariant();
    }

    public void AddDecision(AgentDecision decision)
    {
        decision.DeliberationSessionId = Id;
        Decisions.Add(decision);
    }
}
