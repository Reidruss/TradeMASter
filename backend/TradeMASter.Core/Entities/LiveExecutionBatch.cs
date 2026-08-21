using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Entities;

public sealed class LiveExecutionBatch : BaseEntity
{
    public Guid TradePlanId { get; private set; }
    public string PlanHash { get; private set; } = string.Empty;
    public LiveExecutionBatchStatus Status { get; private set; }
    public string AccountLastFour { get; private set; } = string.Empty;
    public DateTime PreflightAtUtc { get; private set; }
    public string PreflightSnapshotJson { get; private set; } = string.Empty;
    public decimal ReservedBuyingPower { get; private set; }
    public decimal TotalBuyNotional { get; private set; }
    public decimal TotalSellNotional { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public List<LiveExecutionOrderAttempt> Attempts { get; private set; } = [];
    public LiveExecutionReconciliationState? ReconciliationState { get; private set; }

    public LiveExecutionBatch() { }

    public LiveExecutionBatch(
        Guid tradePlanId,
        string planHash,
        string accountLastFour,
        DateTime preflightAtUtc,
        string preflightSnapshotJson,
        decimal reservedBuyingPower,
        decimal totalBuyNotional,
        decimal totalSellNotional,
        bool submissionAuthorized,
        string? authorityReason)
    {
        if (tradePlanId == Guid.Empty) throw new ArgumentException("Trade plan ID is required.", nameof(tradePlanId));
        if (planHash.Length != 64) throw new ArgumentException("A SHA-256 plan hash is required.", nameof(planHash));
        if (string.IsNullOrWhiteSpace(accountLastFour)) throw new ArgumentException("Account identity is required.", nameof(accountLastFour));
        if (string.IsNullOrWhiteSpace(preflightSnapshotJson)) throw new ArgumentException("A sanitized preflight snapshot is required.", nameof(preflightSnapshotJson));
        TradePlanId = tradePlanId;
        PlanHash = planHash.ToLowerInvariant();
        AccountLastFour = accountLastFour;
        PreflightAtUtc = DateTime.SpecifyKind(preflightAtUtc, DateTimeKind.Utc);
        PreflightSnapshotJson = preflightSnapshotJson;
        ReservedBuyingPower = reservedBuyingPower;
        TotalBuyNotional = totalBuyNotional;
        TotalSellNotional = totalSellNotional;
        Status = submissionAuthorized ? LiveExecutionBatchStatus.PreflightPassed : LiveExecutionBatchStatus.SubmissionBlocked;
        StatusReason = submissionAuthorized ? "Fresh deterministic preflight passed." : authorityReason;
    }

    public void MarkSubmitting(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.Submitting;
        StatusReason = "Submitting durable outbox attempts in deterministic sequence.";
        UpdatedAt = utcNow;
    }

    public void MarkSubmitted(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.Submitted;
        StatusReason = "The current sell-first outbox order was accepted by the broker; lifecycle reconciliation is active.";
        SubmittedAtUtc ??= utcNow;
        UpdatedAt = utcNow;
    }

    public void MarkPartiallyFilled(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.PartiallyFilled;
        StatusReason = "At least one material fill was reconciled; remaining intent is gated by the fresh account state.";
        UpdatedAt = utcNow;
    }

    public void MarkCancelPending(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.CancelPending;
        StatusReason = "The deterministic order timeout elapsed and Robinhood cancellation was requested.";
        UpdatedAt = utcNow;
    }

    public void MarkCompleted(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.Completed;
        StatusReason = "Every order reached a terminal state and the final Robinhood portfolio was verified.";
        UpdatedAt = utcNow;
    }

    public void MarkCancelled(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.Cancelled;
        StatusReason = "Every order reached a terminal state and the cancelled batch portfolio was verified.";
        UpdatedAt = utcNow;
    }

    public void MarkExpired(DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.Expired;
        StatusReason = "Every order reached a terminal state and at least one broker order expired.";
        UpdatedAt = utcNow;
    }

    public void MarkFailed(string reason, DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.Failed;
        StatusReason = NormalizeReason(reason);
        UpdatedAt = utcNow;
    }

    public void MarkReconciliationRequired(string reason, DateTime utcNow)
    {
        Status = LiveExecutionBatchStatus.ReconciliationRequired;
        StatusReason = NormalizeReason(reason);
        UpdatedAt = utcNow;
    }

    private static string NormalizeReason(string reason)
    {
        var value = reason.Trim();
        if (value.Length is < 5 or > 1000) throw new ArgumentException("Execution status reason must contain 5–1000 characters.", nameof(reason));
        return value;
    }
}
