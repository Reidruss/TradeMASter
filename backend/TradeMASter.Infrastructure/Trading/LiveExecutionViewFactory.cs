using TradeMASter.Core.Entities;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Trading;

internal static class LiveExecutionViewFactory
{
    public static LiveExecutionBatchView Create(LiveExecutionBatch batch) => new(
        batch.Id,
        batch.TradePlanId,
        batch.PlanHash,
        batch.Status,
        batch.AccountLastFour,
        batch.PreflightAtUtc,
        batch.ReservedBuyingPower,
        batch.TotalBuyNotional,
        batch.TotalSellNotional,
        batch.StatusReason,
        batch.SubmittedAtUtc,
        batch.Attempts.OrderBy(item => item.Sequence).Select(item =>
        {
            var latest = item.Events.OrderByDescending(value => value.BrokerUpdatedAtUtc)
                .ThenByDescending(value => value.ObservedAtUtc).FirstOrDefault();
            return new LiveExecutionAttemptView(
                item.Id, item.Sequence, item.ClientOrderId, item.IdempotencyKey, item.Symbol, item.Side, item.Type,
                item.Quantity, item.LimitPrice, item.EstimatedNotional, item.Status, item.BrokerOrderId,
                item.AttemptCount, item.LastAttemptAtUtc, item.FailureReason, item.SanitizedRequestJson,
                item.SanitizedReviewJson, item.SanitizedResponseJson, latest?.BrokerState,
                latest?.FilledQuantity ?? 0m, latest?.AverageFillPrice, latest?.ObservedAtUtc);
        }).ToList(),
        batch.ReconciliationState?.LastReconciledAtUtc,
        batch.ReconciliationState?.LatestRiskSnapshotJson,
        batch.ReconciliationState?.FinalSnapshotJson,
        batch.ReconciliationState?.FinalPortfolioVerified ?? false,
        batch.ReconciliationState?.InterventionReason);
}
