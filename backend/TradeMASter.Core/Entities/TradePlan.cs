using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Entities;

public sealed class TradePlan : BaseEntity
{
    public const string PrimaryApprovalConfirmation = "APPROVE EXACT PLAN";
    public const string SecondaryApprovalConfirmation = "CONFIRM MATERIAL TRADE PLAN";
    public const string LiveSubmissionConfirmation = "SUBMIT APPROVED PLAN";

    public Guid SourceRunId { get; private set; }
    public Guid PortfolioId { get; private set; }
    public TradePlanStatus Status { get; private set; } = TradePlanStatus.Proposed;
    public string PlanHash { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public int PolicyVersion { get; private set; }
    public bool RequiresSecondaryConfirmation { get; private set; }
    public string SecondaryConfirmationReasons { get; private set; } = string.Empty;
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    public TradePlan() { }

    public TradePlan(
        Guid sourceRunId,
        Guid portfolioId,
        string planHash,
        string payloadJson,
        DateTime expiresAtUtc,
        int policyVersion,
        bool requiresSecondaryConfirmation,
        IReadOnlyList<string> secondaryConfirmationReasons)
    {
        if (sourceRunId == Guid.Empty) throw new ArgumentException("Source run ID is required.", nameof(sourceRunId));
        if (portfolioId == Guid.Empty) throw new ArgumentException("Portfolio ID is required.", nameof(portfolioId));
        if (planHash.Length != 64) throw new ArgumentException("A SHA-256 plan hash is required.", nameof(planHash));
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ArgumentException("Immutable plan payload is required.", nameof(payloadJson));
        if (expiresAtUtc <= DateTime.UtcNow) throw new ArgumentException("Plan expiry must be in the future.", nameof(expiresAtUtc));
        if (policyVersion <= 0) throw new ArgumentOutOfRangeException(nameof(policyVersion));

        SourceRunId = sourceRunId;
        PortfolioId = portfolioId;
        PlanHash = planHash.ToLowerInvariant();
        PayloadJson = payloadJson;
        ExpiresAtUtc = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);
        PolicyVersion = policyVersion;
        RequiresSecondaryConfirmation = requiresSecondaryConfirmation;
        SecondaryConfirmationReasons = string.Join(" | ", secondaryConfirmationReasons.Distinct());
    }

    public bool RefreshExpiry(DateTime utcNow)
    {
        if (Status != TradePlanStatus.Proposed || utcNow < ExpiresAtUtc) return false;
        Status = TradePlanStatus.Expired;
        DecisionReason = "Approval window expired.";
        UpdatedAt = utcNow;
        return true;
    }

    public Result Approve(string? planHash, string? confirmation, string? secondaryConfirmation, DateTime utcNow)
    {
        if (Status == TradePlanStatus.Approved)
            return FixedTimeEquals(planHash, PlanHash)
                ? Result.Success()
                : Result.Failure("Approved plan hash does not match this request.");
        if (Status != TradePlanStatus.Proposed)
            return Result.Failure($"Only a proposed plan can be approved; current status is {Status}.");
        if (RefreshExpiry(utcNow)) return Result.Failure("Trade plan expired before approval.");
        if (!FixedTimeEquals(planHash, PlanHash)) return Result.Failure("Plan hash mismatch; refresh and review the exact current plan.");
        if (!string.Equals(confirmation?.Trim(), PrimaryApprovalConfirmation, StringComparison.Ordinal))
            return Result.Failure($"Exact confirmation '{PrimaryApprovalConfirmation}' is required.");
        if (RequiresSecondaryConfirmation
            && !string.Equals(secondaryConfirmation?.Trim(), SecondaryApprovalConfirmation, StringComparison.Ordinal))
            return Result.Failure($"This plan is material. Exact second confirmation '{SecondaryApprovalConfirmation}' is required.");

        Status = TradePlanStatus.Approved;
        ApprovedAtUtc = utcNow;
        DecisionReason = "Exact immutable plan approved by the local operator.";
        UpdatedAt = utcNow;
        return Result.Success();
    }

    public Result Reject(string? planHash, string? reason, DateTime utcNow)
    {
        if (Status == TradePlanStatus.Rejected)
            return FixedTimeEquals(planHash, PlanHash) ? Result.Success() : Result.Failure("Rejected plan hash does not match this request.");
        if (Status != TradePlanStatus.Proposed)
            return Result.Failure($"Only a proposed plan can be rejected; current status is {Status}.");
        if (!FixedTimeEquals(planHash, PlanHash)) return Result.Failure("Plan hash mismatch; refresh and review the exact current plan.");
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length < 5 || normalized.Length > 500)
            return Result.Failure("Rejection reason must contain between 5 and 500 characters.");
        Status = TradePlanStatus.Rejected;
        RejectedAtUtc = utcNow;
        DecisionReason = normalized;
        UpdatedAt = utcNow;
        return Result.Success();
    }

    public Result Invalidate(string reason, DateTime utcNow)
    {
        if (Status == TradePlanStatus.Invalidated) return Result.Success();
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length < 5 || normalized.Length > 500)
            return Result.Failure("Invalidation reason must contain between 5 and 500 characters.");
        Status = TradePlanStatus.Invalidated;
        InvalidatedAtUtc = utcNow;
        DecisionReason = normalized;
        UpdatedAt = utcNow;
        return Result.Success();
    }

    private static bool FixedTimeEquals(string? supplied, string expected)
    {
        if (supplied is null) return false;
        if (supplied.Length != expected.Length) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(supplied.ToLowerInvariant()),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }
}
