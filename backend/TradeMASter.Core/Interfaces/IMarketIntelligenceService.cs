using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Interfaces;

public record MarketUniverseAsset(
    string Symbol,
    string Name,
    string Sector,
    string Industry,
    string Country,
    decimal LastPrice,
    decimal ChangePercent,
    decimal Volume,
    decimal MarketCap);

public record MarketUniverseSnapshot(
    DateTime AsOfUtc,
    string Source,
    int TotalSecuritiesScanned,
    IReadOnlyList<MarketUniverseAsset> Securities);

public interface IMarketUniverseProvider
{
    Task<Result<MarketUniverseSnapshot>> ScanAsync(CancellationToken cancellationToken = default);
}

public record MacroRegimeAssessment(
    string Regime,
    decimal TargetEquityPercent,
    decimal TargetCashPercent,
    decimal Vix,
    decimal TenYearYield,
    string Rationale,
    IReadOnlyList<string> KeyRisks);

public record MarketCandidateAssessment(
    string Symbol,
    string Name,
    string Sector,
    decimal LastPrice,
    decimal MarketCap,
    decimal AverageDailyVolume,
    decimal MarketScreenScore,
    decimal FundamentalHealthScore,
    decimal TechnicalMomentumScore,
    decimal SentimentScore,
    decimal CompositeConvictionScore,
    decimal AnnualizedVolatilityPercent,
    decimal AtrStopLossPrice,
    SignalDirection Direction,
    bool IsApproved,
    string Rationale,
    IReadOnlyList<string> RiskFlags,
    bool HasVerifiedFundamentals = false,
    string FundamentalDataQuality = "Unavailable",
    IReadOnlyList<string>? FundamentalSources = null);

public record TargetAllocation(
    string Symbol,
    string Sector,
    decimal TargetWeightPercent,
    decimal TargetValue,
    decimal CurrentWeightPercent,
    decimal WeightDeltaPercent,
    decimal EstimatedQuantity,
    decimal StopLossPrice);

public record MarketScanRequest(
    int DeepAnalysisCount = 8,
    decimal MinimumMarketCap = 500_000_000m,
    decimal MinimumSharePrice = 3m,
    decimal MinimumDailyVolume = 250_000m,
    decimal MaxSingleAssetPercent = 20m,
    decimal MaxSectorPercent = 40m,
    decimal MaxTurnoverPercent = 25m,
    bool IsMockRun = false,
    decimal MockPortfolioEquity = 10_000m,
    decimal MinimumFundamentalHealthScore = 55m,
    decimal MaxCandidateVolatilityPercent = 80m,
    decimal MaxProjectedPortfolioVolatilityPercent = 35m,
    decimal MaxDailyVaR95Percent = 3m);

public record PortfolioPerformanceSnapshot(
    int ObservationCount,
    decimal? AnnualizedSharpeRatio,
    decimal MaxDrawdownPercent,
    decimal WinRatePercent,
    decimal CumulativeReturnPercent);

public record MarketIntelligenceRun(
    Guid Id,
    bool IsMockRun,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int TotalSecuritiesScanned,
    int EligibleSecurities,
    MacroRegimeAssessment MacroRegime,
    IReadOnlyList<MarketCandidateAssessment> Candidates,
    IReadOnlyList<TargetAllocation> TargetAllocations,
    decimal TargetCashPercent,
    decimal EstimatedTurnoverPercent,
    decimal ProjectedAnnualizedVolatilityPercent,
    decimal ParametricDailyVaR95Percent,
    bool IsRiskApproved,
    string RiskAuditorFeedback,
    IReadOnlyList<OrderRequest> ProposedPaperOrders,
    string ReflectionSummary,
    PortfolioPerformanceSnapshot PerformanceMetrics,
    string DataSourceSummary,
    Guid? TradePlanId = null,
    string? TradePlanHash = null,
    TradePlanStatus? TradePlanStatus = null,
    DateTime? TradePlanExpiresAtUtc = null);

public interface IMarketIntelligenceService
{
    Task<Result<MarketIntelligenceRun>> RunMarketScanAsync(
        MarketScanRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<MarketIntelligenceRun?>> GetLatestRunAsync(CancellationToken cancellationToken = default);
}
