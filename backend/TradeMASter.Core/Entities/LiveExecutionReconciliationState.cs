using TradeMASter.Core.Common;

namespace TradeMASter.Core.Entities;

public sealed class LiveExecutionReconciliationState : BaseEntity
{
    public Guid BatchId { get; private set; }
    public DateTime? LastReconciledAtUtc { get; private set; }
    public string? LatestBrokerSnapshotJson { get; private set; }
    public string? LatestRiskSnapshotJson { get; private set; }
    public string? FinalSnapshotJson { get; private set; }
    public bool FinalPortfolioVerified { get; private set; }
    public string? InterventionReason { get; private set; }

    public LiveExecutionReconciliationState() { }
    public LiveExecutionReconciliationState(Guid batchId)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("Batch ID is required.", nameof(batchId));
        BatchId = batchId;
    }

    public void Record(DateTime utcNow, string brokerSnapshotJson, string riskSnapshotJson)
    {
        LastReconciledAtUtc = utcNow;
        LatestBrokerSnapshotJson = brokerSnapshotJson;
        LatestRiskSnapshotJson = riskSnapshotJson;
        InterventionReason = null;
        UpdatedAt = utcNow;
    }

    public void RequireIntervention(string reason, DateTime utcNow)
    {
        InterventionReason = reason.Trim().Length <= 1000 ? reason.Trim() : reason.Trim()[..1000];
        LastReconciledAtUtc = utcNow;
        FinalPortfolioVerified = false;
        UpdatedAt = utcNow;
    }

    public void VerifyFinal(string finalSnapshotJson, DateTime utcNow)
    {
        FinalSnapshotJson = finalSnapshotJson;
        FinalPortfolioVerified = true;
        InterventionReason = null;
        LastReconciledAtUtc = utcNow;
        UpdatedAt = utcNow;
    }
}
