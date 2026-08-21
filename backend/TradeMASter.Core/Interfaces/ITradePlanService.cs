using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Interfaces;

public record TradePlanAccountSnapshot(
    string AccountLastFour,
    DateTime AsOfUtc,
    decimal TotalEquity,
    decimal CashAvailable,
    decimal BuyingPower,
    IReadOnlyList<TradePlanHoldingSnapshot> Holdings);

public record TradePlanHoldingSnapshot(
    string Symbol,
    decimal Quantity,
    decimal CurrentPrice,
    decimal CurrentMarketValue,
    decimal PortfolioWeightPercent);

public record TradePlanOrderSnapshot(
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    decimal EstimatedNotional,
    bool IsFullLiquidation);

public record TradePlanRiskSnapshot(
    bool IsRiskApproved,
    string Feedback,
    decimal EstimatedTurnoverPercent,
    decimal ProjectedAnnualizedVolatilityPercent,
    decimal ParametricDailyVaR95Percent,
    decimal TargetCashPercent,
    decimal HistoricalMaxDrawdownPercent = 0m);

public record ImmutableTradePlanPayload(
    Guid SourceRunId,
    Guid PortfolioId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    int PolicyVersion,
    TradePlanAccountSnapshot Account,
    MacroRegimeAssessment MacroRegime,
    IReadOnlyList<TargetAllocation> TargetAllocations,
    IReadOnlyList<TradePlanOrderSnapshot> Orders,
    TradePlanRiskSnapshot Risk,
    string ReflectionSummary,
    string DataSourceSummary,
    IReadOnlyDictionary<string, IReadOnlyList<string>> CandidateProvenance);

public record TradePlanView(
    Guid Id,
    Guid SourceRunId,
    Guid PortfolioId,
    TradePlanStatus Status,
    string PlanHash,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    int PolicyVersion,
    bool RequiresSecondaryConfirmation,
    IReadOnlyList<string> SecondaryConfirmationReasons,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    DateTime? InvalidatedAtUtc,
    string? DecisionReason,
    ImmutableTradePlanPayload Payload);

public record ApproveTradePlanRequest(
    string? PlanHash,
    string? Confirmation,
    string? SecondaryConfirmation = null);

public record RejectTradePlanRequest(string? PlanHash, string? Reason);

public interface ITradePlanService
{
    Task<Result<TradePlanView?>> CreateFromMarketRunAsync(
        MarketIntelligenceRun run,
        Portfolio portfolio,
        CancellationToken cancellationToken = default);
    Task<Result<TradePlanView?>> GetLatestAsync(CancellationToken cancellationToken = default);
    Task<Result<TradePlanView>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<TradePlanView>> ApproveAsync(Guid id, ApproveTradePlanRequest request, CancellationToken cancellationToken = default);
    Task<Result<TradePlanView>> RejectAsync(Guid id, RejectTradePlanRequest request, CancellationToken cancellationToken = default);
}
