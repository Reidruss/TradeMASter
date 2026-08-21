using FluentAssertions;
using TradeMASter.Agents.Personas;
using TradeMASter.Agents.Tools;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using Xunit;

namespace TradeMASter.Tests.Agents;

public sealed class MarketPipelineAgentTests
{
    [Fact]
    public void CandidateScreener_FiltersNonOperatingAndIlliquidListings()
    {
        var universe = new MarketUniverseSnapshot(DateTime.UtcNow, "test", 4,
        [
            Asset("AAA", "Alpha Common Stock", "Technology", 20m, 2_000_000_000m, 2_000_000m),
            Asset("AAAW", "Alpha Warrant", "Technology", 5m, 2_000_000_000m, 2_000_000m),
            Asset("LOW", "Low Liquidity Common Stock", "Health Care", 20m, 2_000_000_000m, 1_000m),
            Asset("BBB", "Beta Common Stock", "Industrials", 30m, 4_000_000_000m, 3_000_000m)
        ]);

        var result = new AssetSelectionCandidateScreener().Screen(universe, new MarketScanRequest(DeepAnalysisCount: 5));

        result.Select(item => item.Asset.Symbol).Should().BeEquivalentTo(["AAA", "BBB"]);
    }

    [Fact]
    public void CandidateScreener_PenalizesExtremeOneDayPriceSpikes()
    {
        var universe = new MarketUniverseSnapshot(DateTime.UtcNow, "test", 2,
        [
            new MarketUniverseAsset("STABLE", "Stable Corp", "Technology", "Software", "US", 20m, 0.5m, 2_000_000m, 2_000_000_000m),
            new MarketUniverseAsset("SPIKE", "Spike Corp", "Industrials", "Machinery", "US", 20m, 10m, 2_000_000m, 2_000_000_000m)
        ]);

        var result = new AssetSelectionCandidateScreener().Screen(
            universe, new MarketScanRequest(DeepAnalysisCount: 5));

        result.Single(item => item.Asset.Symbol == "STABLE").Score
            .Should().BeGreaterThan(result.Single(item => item.Asset.Symbol == "SPIKE").Score);
    }

    [Fact]
    public void CandidateGate_ExplainsCompositeConvictionRejection()
    {
        var result = new CandidateApprovalGate().Evaluate(
            conviction: 54m,
            fundamentalHealth: 65m,
            annualizedVolatility: 30m,
            priceObservationCount: 252,
            sentimentDirection: SignalDirection.Neutral,
            sentimentConfidence: 0.5,
            hasVerifiedFundamentals: false,
            request: new MarketScanRequest(IsMockRun: true));

        result.IsApproved.Should().BeFalse();
        result.RiskFlags.Should().ContainSingle(flag => flag.Contains("Composite conviction 54.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Allocator_UsesPhasedHrpWeightsWithinTurnoverLimit()
    {
        var portfolio = new Portfolio("Cash", 1_000m);
        var candidates = new[]
        {
            Candidate("AAA", "Technology", 80m, 20m),
            Candidate("BBB", "Industrials", 70m, 30m)
        };
        var request = new MarketScanRequest(MaxTurnoverPercent: 25m, MaxSingleAssetPercent: 20m);

        var allocations = new QuantitativeAllocator().Allocate(
            candidates,
            portfolio,
            new MacroRegimeAssessment("Risk-On", 75m, 25m, 15m, 4m, "test", []),
            request);
        var review = new RiskComplianceAuditor().Review(allocations, portfolio, request);

        allocations.Sum(item => item.TargetWeightPercent).Should().BeLessThanOrEqualTo(25m);
        allocations.Should().OnlyContain(item => item.TargetWeightPercent <= 20m);
        review.IsApproved.Should().BeTrue();
    }

    [Fact]
    public void RiskAuditor_RejectsSectorConcentration()
    {
        var allocations = new[]
        {
            new TargetAllocation("AAA", "Technology", 25m, 250m, 0m, 25m, 10m, 20m),
            new TargetAllocation("BBB", "Technology", 20m, 200m, 0m, 20m, 10m, 20m)
        };

        var review = new RiskComplianceAuditor().Review(
            allocations,
            new Portfolio("Cash", 1_000m),
            new MarketScanRequest(MaxSingleAssetPercent: 30m, MaxSectorPercent: 40m, MaxTurnoverPercent: 50m));

        review.IsApproved.Should().BeFalse();
        review.Feedback.Should().Contain("Technology").And.Contain("sector cap");
    }

    [Fact]
    public void RiskAuditor_RejectsPortfolioVolatilityAboveConfiguredLimit()
    {
        var allocations = new[]
        {
            new TargetAllocation("AAA", "Technology", 50m, 500m, 0m, 50m, 10m, 20m)
        };
        var returns = Enumerable.Range(0, 60)
            .Select(index => index % 2 == 0 ? 0.08m : -0.08m)
            .ToList();

        var review = new RiskComplianceAuditor().Review(
            allocations,
            new Portfolio("Cash", 1_000m),
            new MarketScanRequest(
                MaxSingleAssetPercent: 60m,
                MaxSectorPercent: 60m,
                MaxTurnoverPercent: 60m,
                MaxProjectedPortfolioVolatilityPercent: 20m,
                MaxDailyVaR95Percent: 10m),
            new Dictionary<string, IReadOnlyList<decimal>> { ["AAA"] = returns });

        review.IsApproved.Should().BeFalse();
        review.ProjectedAnnualizedVolatilityPercent.Should().BeGreaterThan(20m);
        review.Feedback.Should().Contain("Projected annualized volatility");
    }

    [Fact]
    public void RiskAuditor_AllowsPhasedReductionOfExistingConcentration()
    {
        var allocations = new[]
        {
            new TargetAllocation("AAA", "Technology", 55m, 550m, 70m, -15m, 5.5m, 0m)
        };

        var review = new RiskComplianceAuditor().Review(
            allocations,
            new Portfolio("Existing", 1_000m),
            new MarketScanRequest(
                MaxSingleAssetPercent: 20m,
                MaxSectorPercent: 40m,
                MaxTurnoverPercent: 25m,
                MaxProjectedPortfolioVolatilityPercent: 100m,
                MaxDailyVaR95Percent: 10m),
            fallbackVolatilityPercent: new Dictionary<string, decimal> { ["AAA"] = 20m });

        review.IsApproved.Should().BeTrue();
    }

    [Fact]
    public void Allocator_IncludesTurnoverBudgetedExitForUnselectedHolding()
    {
        var portfolio = new Portfolio("Existing", 1_000m) { CashBalance = 500m };
        var oldPosition = new Position(portfolio.Id, "OLD", 10m, 50m);
        oldPosition.UpdateCurrentPrice(50m);
        portfolio.Positions.Add(oldPosition);
        var candidates = new[] { Candidate("NEW", "Technology", 80m, 20m) };

        var allocations = new QuantitativeAllocator().Allocate(
            candidates,
            portfolio,
            new MacroRegimeAssessment("Defensive", 50m, 50m, 20m, 4m, "test", []),
            new MarketScanRequest(MaxTurnoverPercent: 25m),
            new Dictionary<string, IReadOnlyList<decimal>>
            {
                ["NEW"] = Enumerable.Range(1, 60).Select(index => index % 2 == 0 ? 0.01m : -0.005m).ToList()
            },
            new Dictionary<string, string> { ["OLD"] = "Industrials" });

        allocations.Should().Contain(item => item.Symbol == "OLD" && item.WeightDeltaPercent < 0m);
        allocations.Sum(item => Math.Abs(item.WeightDeltaPercent)).Should().BeLessThanOrEqualTo(25m);
    }

    [Fact]
    public void ExecutionManager_UsesLimitOrderForPositionExit()
    {
        var portfolio = new Portfolio("Existing", 1_000m) { CashBalance = 500m };
        var position = new Position(portfolio.Id, "OLD", 10m, 50m);
        position.UpdateCurrentPrice(50m);
        portfolio.Positions.Add(position);
        var allocation = new TargetAllocation("OLD", "Industrials", 0m, 0m, 50m, -50m, 0m, 0m);

        var orders = new ExecutionRebalancingManager().BuildPaperOrders([allocation], portfolio, true);

        orders.Should().ContainSingle();
        orders[0].Side.Should().Be(OrderSide.Sell);
        orders[0].Type.Should().Be(OrderType.Limit);
        orders[0].LimitPrice.Should().Be(49.95m);
    }

    [Fact]
    public void Macd_UsesNinePeriodSignalEmaInsteadOfFixedMultiplier()
    {
        var prices = Enumerable.Range(1, 80)
            .Select(index => 100m + index * 0.4m + (index % 7 - 3) * 0.2m)
            .ToList();

        var (line, signal, histogram) = TechnicalIndicatorCalculator.CalculateMacd(prices);

        signal.Should().NotBeApproximately(line * 0.85m, 0.000001m);
        histogram.Should().BeApproximately(line - signal, 0.000001m);
    }

    private static MarketUniverseAsset Asset(
        string symbol, string name, string sector, decimal price, decimal cap, decimal volume) =>
        new(symbol, name, sector, "Software", "United States", price, 1m, volume, cap);

    private static MarketCandidateAssessment Candidate(
        string symbol, string sector, decimal conviction, decimal volatility) =>
        new(symbol, symbol, sector, 10m, 5_000_000_000m, 2_000_000m, 90m, 80m, 75m, 65m,
            conviction, volatility, 9m, SignalDirection.Bullish, true, "test", []);
}
