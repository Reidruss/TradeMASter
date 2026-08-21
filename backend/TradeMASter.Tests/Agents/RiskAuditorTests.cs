using FluentAssertions;
using Moq;
using TradeMASter.Agents.LLM;
using TradeMASter.Agents.Personas;
using TradeMASter.Agents.Tools;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;
using Xunit;

namespace TradeMASter.Tests.Agents;

public class RiskAuditorTests
{
    private readonly Mock<ILlmClient> _mockLlm = new();

    [Fact]
    public void RiskAuditor_WhenOrderExceedsCash_IssuesVeto()
    {
        var riskAuditor = new RiskAuditor(_mockLlm.Object);
        var portfolio = new Portfolio("Test Fund", 1_000m); // only $1,000 cash

        var context = new MarketAnalysisContext(
            "NVDA",
            new PriceTick("NVDA", 200m, 1_000_000m, DateTime.UtcNow, 199.9m, 200.1m, 0m, 0m),
            new List<Candle>(),
            TechnicalIndicatorCalculator.Calculate(new List<Candle>()),
            FundamentalDataProvider.GetSnapshot("NVDA"),
            SentimentEvaluator.Evaluate("NVDA"),
            portfolio,
            portfolio.RiskConfig
        );

        // Propose buying 10 shares of NVDA ($2,000 cost > $1,000 cash)
        var audit = riskAuditor.EvaluateRisk(context, OrderSide.Buy, 10m);

        audit.IsApproved.Should().BeFalse();
        audit.VetoReason.Should().Contain("exceeds liquid cash reserves");
    }

    [Fact]
    public void RiskAuditor_WhenOrderExceedsMaxPositionSize_IssuesVeto()
    {
        var riskAuditor = new RiskAuditor(_mockLlm.Object);
        var portfolio = new Portfolio("Test Fund", 100_000m);
        // Max position size is 10% = $10,000

        var context = new MarketAnalysisContext(
            "NVDA",
            new PriceTick("NVDA", 200m, 1_000_000m, DateTime.UtcNow, 199.9m, 200.1m, 0m, 0m),
            new List<Candle>(),
            TechnicalIndicatorCalculator.Calculate(new List<Candle>()),
            FundamentalDataProvider.GetSnapshot("NVDA"),
            SentimentEvaluator.Evaluate("NVDA"),
            portfolio,
            portfolio.RiskConfig
        );

        // Propose buying 100 shares of NVDA ($20,000 > $10,000 max position size)
        var audit = riskAuditor.EvaluateRisk(context, OrderSide.Buy, 100m);

        audit.IsApproved.Should().BeFalse();
        audit.VetoReason.Should().Contain("exceeds risk limit");
    }

    [Fact]
    public void RiskAuditor_WhenOrderWithinLimits_ApprovesAndConfiguresStops()
    {
        var riskAuditor = new RiskAuditor(_mockLlm.Object);
        var portfolio = new Portfolio("Test Fund", 100_000m);

        var context = new MarketAnalysisContext(
            "NVDA",
            new PriceTick("NVDA", 200m, 1_000_000m, DateTime.UtcNow, 199.9m, 200.1m, 0m, 0m),
            new List<Candle>(),
            TechnicalIndicatorCalculator.Calculate(new List<Candle>()),
            FundamentalDataProvider.GetSnapshot("NVDA"),
            SentimentEvaluator.Evaluate("NVDA"),
            portfolio,
            portfolio.RiskConfig
        );

        // Propose buying 10 shares of NVDA ($2,000 < $10,000 limit)
        var audit = riskAuditor.EvaluateRisk(context, OrderSide.Buy, 10m);

        audit.IsApproved.Should().BeTrue();
        audit.VetoReason.Should().BeNull();
        audit.RecommendedStopLossPrice.Should().BeLessThan(200m);
        audit.RecommendedTakeProfitPrice.Should().BeGreaterThan(200m);
    }
}
