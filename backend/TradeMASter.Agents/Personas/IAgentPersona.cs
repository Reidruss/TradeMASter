using TradeMASter.Agents.Tools;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Agents.Personas;

public record MarketAnalysisContext(
    string Symbol,
    PriceTick Quote,
    IReadOnlyList<Candle> Candles,
    TechnicalIndicatorSnapshot Indicators,
    FundamentalDataSnapshot Fundamentals,
    SentimentSnapshot Sentiment,
    Portfolio Portfolio,
    RiskParameters RiskConfig);

public interface IAgentPersona
{
    AgentRole Role { get; }
    string PersonaName { get; }
    Task<AgentDecision> AnalyzeAsync(MarketAnalysisContext context, CancellationToken cancellationToken = default);
    Task<string> DefendThesisAsync(string challenge, MarketAnalysisContext context, CancellationToken cancellationToken = default);
}
