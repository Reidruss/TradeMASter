using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Interfaces;

public enum RebalanceAction
{
    Hold = 0,
    Buy = 1,
    Sell = 2,
    Trim = 3,
    Add = 4
}

public record AllocationDeltaItem(
    string Symbol,
    decimal CurrentQuantity,
    decimal CurrentPrice,
    decimal CurrentValue,
    decimal CurrentWeightPercent,
    decimal TargetWeightPercent,
    decimal WeightDeltaPercent,
    RebalanceAction Action,
    decimal RecommendedQuantity,
    decimal EstimatedTradeValue,
    string PersonaRationale,
    SignalDirection CommitteeSignal);

public record OptimizationPlan(
    Guid Id,
    Guid PortfolioId,
    DateTime GeneratedAtUtc,
    DateTime NextScheduledRebalanceUtc,
    decimal CurrentTotalEquity,
    decimal CurrentCash,
    decimal ProjectedCash,
    decimal EstimatedTotalTurnoverPercent,
    IReadOnlyList<AllocationDeltaItem> Allocations,
    string ExecutiveConsensusRationale,
    bool IsRiskApproved,
    string RiskAuditorNotes,
    IReadOnlyList<OrderRequest> ExecutableOrders,
    int LivePolicyVersion = 1);

public record OptimizationExecutionResult(
    Guid PlanId,
    int OrdersExecuted,
    decimal TotalCapitalRotated,
    IReadOnlyList<Order> ExecutedOrders,
    string Summary);

public interface IPortfolioOptimizerService
{
    Task<Result<OptimizationPlan>> GenerateBiWeeklyOptimizationPlanAsync(
        Guid? portfolioId = null,
        CancellationToken cancellationToken = default);

    Task<Result<OptimizationExecutionResult>> ExecuteOptimizationPlanAsync(
        OptimizationPlan plan,
        CancellationToken cancellationToken = default);

    Task<Result<DateTime>> GetNextScheduledRebalanceTimeAsync(CancellationToken cancellationToken = default);
}
