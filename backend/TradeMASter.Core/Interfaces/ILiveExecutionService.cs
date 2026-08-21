using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Interfaces;

public record BrokerQuoteSnapshot(
    string Symbol,
    decimal Price,
    decimal? Bid,
    decimal? Ask,
    DateTime AsOfUtc,
    string Source);

public record BrokerInstrumentEligibility(
    string Symbol,
    bool IsTradable,
    bool SupportsFractionalShares,
    AssetType AssetType,
    string Exchange,
    string Source);

public record BrokerOpenOrderSnapshot(
    string BrokerOrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal? LimitPrice,
    string State);

public record BrokerExecutionSnapshot(
    string AccountNumber,
    string AccountType,
    decimal TotalEquity,
    decimal CashAvailable,
    decimal BuyingPower,
    DateTime AsOfUtc,
    IReadOnlyList<RobinhoodHoldingItem> Holdings,
    IReadOnlyList<BrokerOpenOrderSnapshot> OpenOrders,
    IReadOnlyList<BrokerQuoteSnapshot> Quotes,
    IReadOnlyList<BrokerInstrumentEligibility> Eligibility,
    decimal CurrentDailyTurnoverPercent);

public record BrokerOrderCommand(
    string AccountNumber,
    Guid ClientOrderId,
    string IdempotencyKey,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal LimitPrice,
    string TimeInForce = "gfd");

public record BrokerOrderReview(
    bool IsApproved,
    IReadOnlyList<string> Warnings,
    string SanitizedResponseJson);

public record BrokerOrderSubmission(
    BrokerSubmissionOutcome Outcome,
    string? BrokerOrderId,
    string BrokerState,
    string Message,
    string SanitizedResponseJson);

public record BrokerOrderLifecycleSnapshot(
    string BrokerOrderId,
    Guid? ClientOrderId,
    string Symbol,
    OrderSide Side,
    decimal OrderedQuantity,
    decimal FilledQuantity,
    decimal? AverageFillPrice,
    decimal? LimitPrice,
    string State,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string SanitizedPayloadJson);

public record BrokerOrderHistorySnapshot(
    string AccountNumber,
    DateTime AsOfUtc,
    IReadOnlyList<BrokerOrderLifecycleSnapshot> Orders);

public record BrokerCancelResult(
    BrokerSubmissionOutcome Outcome,
    string BrokerState,
    string Message,
    string SanitizedResponseJson);

public interface IRobinhoodLiveExecutionAdapter
{
    Task<Result<BrokerExecutionSnapshot>> GetFreshPreflightSnapshotAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default);
    Task<Result<BrokerOrderReview>> ReviewOrderAsync(
        BrokerOrderCommand command,
        CancellationToken cancellationToken = default);
    Task<BrokerOrderSubmission> PlaceOrderAsync(
        BrokerOrderCommand command,
        CancellationToken cancellationToken = default);
    Task<Result<BrokerOrderHistorySnapshot>> GetOrderHistoryAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default);
    Task<BrokerCancelResult> CancelOrderAsync(
        string brokerOrderId,
        CancellationToken cancellationToken = default);
}

public interface ILiveExecutionAuthority
{
    Result Verify(LivePortfolioPolicySnapshot policy);
}

public interface IUsMarketCalendar
{
    bool IsRegularSession(DateTime utcNow);
    string DescribeClosure(DateTime utcNow);
}

public record ExecuteApprovedTradePlanRequest(string? PlanHash, string? Confirmation);

public record LiveExecutionAttemptView(
    Guid Id,
    int Sequence,
    Guid ClientOrderId,
    string IdempotencyKey,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal LimitPrice,
    decimal EstimatedNotional,
    LiveExecutionAttemptStatus Status,
    string? BrokerOrderId,
    int AttemptCount,
    DateTime? LastAttemptAtUtc,
    string? FailureReason,
    string SanitizedRequestJson,
    string? SanitizedReviewJson,
    string? SanitizedResponseJson,
    string? BrokerState,
    decimal FilledQuantity,
    decimal? AverageFillPrice,
    DateTime? LastReconciledAtUtc);

public record LiveExecutionBatchView(
    Guid Id,
    Guid TradePlanId,
    string PlanHash,
    LiveExecutionBatchStatus Status,
    string AccountLastFour,
    DateTime PreflightAtUtc,
    decimal ReservedBuyingPower,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    string? StatusReason,
    DateTime? SubmittedAtUtc,
    IReadOnlyList<LiveExecutionAttemptView> Attempts,
    DateTime? LastReconciledAtUtc,
    string? LatestRiskSnapshotJson,
    string? FinalSnapshotJson,
    bool FinalPortfolioVerified,
    string? InterventionReason);

public record ReconcileLiveExecutionRequest(string? Confirmation = null);

public interface ILiveExecutionService
{
    Task<Result<LiveExecutionBatchView?>> GetByTradePlanAsync(Guid tradePlanId, CancellationToken cancellationToken = default);
    Task<Result<LiveExecutionBatchView>> ExecuteApprovedPlanAsync(
        Guid tradePlanId,
        ExecuteApprovedTradePlanRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<LiveExecutionBatchView>> ReconcileAsync(
        Guid tradePlanId,
        CancellationToken cancellationToken = default);
}

public interface ILiveExecutionReconciliationService
{
    Task<Result<LiveExecutionBatchView>> ReconcileBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<int> ReconcileActiveBatchesAsync(CancellationToken cancellationToken = default);
}
