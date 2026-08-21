using System.Text.Json;
using TradeMASter.Agents.LLM;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Agents.Personas;

public record RiskAuditEvaluation(
    bool IsApproved,
    string? VetoReason,
    decimal MaxAllowedQuantity,
    decimal RecommendedStopLossPrice,
    decimal RecommendedTakeProfitPrice,
    string RiskNotes);

public class RiskAuditor : IAgentPersona
{
    public AgentRole Role => AgentRole.RiskAuditor;
    public string PersonaName => "Risk & Compliance Auditor";
    private readonly ILlmClient _llm;

    public RiskAuditor(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<AgentDecision> AnalyzeAsync(MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        var audit = EvaluateRisk(context, OrderSide.Buy, 10m);

        var signal = audit.IsApproved ? SignalDirection.Bullish : SignalDirection.Bearish;
        var confidence = audit.IsApproved ? 0.90 : 0.99;

        var factors = new List<string>
        {
            $"Max Position Sizing Limit: {context.RiskConfig.MaxPositionSizePercent}% of Total Equity",
            $"Drawdown Ceiling: {context.RiskConfig.MaxPortfolioDrawdownPercent}%",
            $"Calculated Stop-Loss Target: ${audit.RecommendedStopLossPrice:F2}",
            $"Calculated Take-Profit Target: ${audit.RecommendedTakeProfitPrice:F2}",
            $"Veto Status: {(audit.IsApproved ? "CLEARED" : "VETOED")}"
        };

        var reasoning = audit.IsApproved
            ? $"Risk parameters verified. Proposal is within the {context.RiskConfig.MaxPositionSizePercent}% position allocation limit. Recommended stop-loss placed at ${audit.RecommendedStopLossPrice:F2} (based on 1.5x ATR volatility buffer)."
            : $"VETO TRIGGERED: {audit.VetoReason}";

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

    public RiskAuditEvaluation EvaluateRisk(MarketAnalysisContext context, OrderSide side, decimal proposedQuantity)
    {
        var quote = context.Quote;
        var portfolio = context.Portfolio;
        var risk = context.RiskConfig;
        var ind = context.Indicators;

        var currentPrice = quote.Price;
        var totalOrderValue = proposedQuantity * currentPrice;
        var maxPosValue = portfolio.TotalEquity * (risk.MaxPositionSizePercent / 100m);

        var existingPos = portfolio.Positions.FirstOrDefault(p => p.Symbol.Equals(context.Symbol, StringComparison.OrdinalIgnoreCase));
        var projectedPosValue = (existingPos?.CurrentMarketValue ?? 0m) + totalOrderValue;

        // ATR-based dynamic stop loss buffer
        var atrBuffer = ind.Atr14 > 0 ? ind.Atr14 * 1.5m : currentPrice * (risk.DefaultStopLossPercent / 100m);
        var stopLoss = Math.Max(0.01m, currentPrice - atrBuffer);
        var takeProfit = currentPrice + (atrBuffer * 2.0m);

        if (side == OrderSide.Buy)
        {
            if (totalOrderValue > portfolio.CashBalance)
            {
                return new RiskAuditEvaluation(
                    IsApproved: false,
                    VetoReason: $"Order value (${totalOrderValue:N2}) exceeds liquid cash reserves (${portfolio.CashBalance:N2}).",
                    MaxAllowedQuantity: Math.Floor(portfolio.CashBalance / currentPrice),
                    RecommendedStopLossPrice: stopLoss,
                    RecommendedTakeProfitPrice: takeProfit,
                    RiskNotes: "Insufficient cash reserves"
                );
            }

            if (projectedPosValue > maxPosValue && portfolio.TotalEquity > 1000m)
            {
                var remainingCap = Math.Max(0, maxPosValue - (existingPos?.CurrentMarketValue ?? 0m));
                var maxQty = Math.Floor(remainingCap / currentPrice);

                return new RiskAuditEvaluation(
                    IsApproved: false,
                    VetoReason: $"Projected position (${projectedPosValue:N2}) exceeds risk limit of {risk.MaxPositionSizePercent}% of equity (${maxPosValue:N2}).",
                    MaxAllowedQuantity: maxQty,
                    RecommendedStopLossPrice: stopLoss,
                    RecommendedTakeProfitPrice: takeProfit,
                    RiskNotes: "Hard position sizing threshold breached"
                );
            }
        }

        return new RiskAuditEvaluation(
            IsApproved: true,
            VetoReason: null,
            MaxAllowedQuantity: proposedQuantity,
            RecommendedStopLossPrice: Math.Round(stopLoss, 2),
            RecommendedTakeProfitPrice: Math.Round(takeProfit, 2),
            RiskNotes: $"Approved under standard risk guardrails (Stop: ${stopLoss:F2}, Target: ${takeProfit:F2})"
        );
    }

    public Task<string> DefendThesisAsync(string challenge, MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Compliance rule: Safety limits and drawdown guardrails are non-negotiable. Hard risk parameters cannot be overridden without explicit administrative bypass.");
    }
}
