using Microsoft.EntityFrameworkCore;
using TradeMASter.Agents.Orchestration;
using TradeMASter.Agents.LLM;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Agents.Optimization;

public class PortfolioOptimizerService : IPortfolioOptimizerService
{
    private readonly IDeliberationEngine _deliberationEngine;
    private readonly IBrokerClient _brokerClient;
    private readonly IRobinhoodService _robinhoodService;
    private readonly TradeMASterDbContext _dbContext;
    private readonly ILlmClient _llmClient;
    private readonly ILivePortfolioPolicyService _livePolicyService;

    private static OptimizationPlan? _latestPlan = null;
    private static DateTime _nextRebalanceDateUtc = DateTime.UtcNow.AddDays(14);

    public PortfolioOptimizerService(
        IDeliberationEngine deliberationEngine,
        IBrokerClient brokerClient,
        IRobinhoodService robinhoodService,
        TradeMASterDbContext dbContext,
        ILlmClient llmClient,
        ILivePortfolioPolicyService livePolicyService)
    {
        _deliberationEngine = deliberationEngine;
        _brokerClient = brokerClient;
        _robinhoodService = robinhoodService;
        _dbContext = dbContext;
        _llmClient = llmClient;
        _livePolicyService = livePolicyService;
    }

    public Task<Result<DateTime>> GetNextScheduledRebalanceTimeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(_nextRebalanceDateUtc));
    }

    public async Task<Result<OptimizationPlan>> GenerateBiWeeklyOptimizationPlanAsync(
        Guid? portfolioId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Require an authenticated source and sync live Robinhood holdings.
        var accountStatus = await _robinhoodService.GetAccountStatusAsync(cancellationToken);
        if (accountStatus.IsFailure || !accountStatus.Value.IsConnected)
        {
            return Result.Failure<OptimizationPlan>(accountStatus.Error ?? "Connect Robinhood MCP before running optimization.");
        }
        if (!accountStatus.Value.IsDemoMode && _llmClient.ProviderName != "OpenAI")
        {
            return Result.Failure<OptimizationPlan>(
                "Live portfolio optimization requires OPENAI_API_KEY so the research agents can verify current fundamentals and news. Offline and Anthropic-only analysis is restricted to demo mode.");
        }

        var syncResult = await _robinhoodService.SyncHoldingsToPortfolioAsync(portfolioId, cancellationToken);
        if (syncResult.IsFailure)
        {
            return Result.Failure<OptimizationPlan>(syncResult.Error!);
        }

        var portfolio = portfolioId.HasValue
            ? await _dbContext.Portfolios.Include(p => p.Positions).FirstOrDefaultAsync(p => p.Id == portfolioId.Value, cancellationToken)
            : await _dbContext.Portfolios.Include(p => p.Positions).OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken);

        if (portfolio is null)
        {
            return Result.Failure<OptimizationPlan>("No active portfolio found to optimize.");
        }

        var totalEquity = portfolio.TotalEquity;
        var currentCash = portfolio.CashBalance;
        var livePolicy = await _livePolicyService.GetAsync(cancellationToken);
        var minimumCashReserve = totalEquity * livePolicy.MinimumCashReservePercent / 100m;
        var maxOrderNotional = Math.Min(
            livePolicy.MaxOrderNotionalAmount,
            totalEquity * livePolicy.MaxOrderNotionalPercent / 100m);
        var positions = portfolio.Positions.Where(p => p.Quantity > 0).ToList();

        if (totalEquity <= 0)
        {
            return Result.Failure<OptimizationPlan>("Portfolio equity must be greater than zero.");
        }

        var allocationDeltas = new List<AllocationDeltaItem>();
        var executableOrders = new List<OrderRequest>();
        decimal projectedCash = currentCash;
        decimal totalTurnoverDollars = 0m;

        // 2. Run multi-agent committee deliberation on each holding
        foreach (var pos in positions)
        {
            var symbol = pos.Symbol;
            var currentVal = pos.CurrentMarketValue;
            var currentPrice = pos.CurrentPrice > 0 ? pos.CurrentPrice : pos.AverageEntryPrice;
            var currentWeightPct = (currentVal / totalEquity) * 100m;

            var delibRes = await _deliberationEngine.DeliberateAsync(symbol, portfolio.Id, autoExecute: false, cancellationToken);
            
            SignalDirection overallSignal = SignalDirection.Neutral;
            string rationale = "Balanced holding status.";

            if (delibRes.IsSuccess)
            {
                var session = delibRes.Value.Session;
                rationale = session.FinalConsensusSummary;
                overallSignal = session.FinalVerdict switch
                {
                    DecisionVerdict.Buy => SignalDirection.Bullish,
                    DecisionVerdict.Sell => SignalDirection.Bearish,
                    DecisionVerdict.Vetoed => SignalDirection.Bearish,
                    _ => SignalDirection.Neutral
                };
            }

            // Target Allocation Heuristics:
            // - Strong Bullish: Target 20% - 25%
            // - Moderate Bullish: Target 15% - 18%
            // - Neutral: Target 8% - 12%
            // - Bearish: Target 0% - 5% (Trim)
            var maxPositionWeight = Math.Min(
                Math.Clamp(portfolio.RiskConfig.MaxPositionSizePercent, 1m, 100m),
                livePolicy.MaxPositionPercent);
            decimal targetWeightPct = overallSignal switch
            {
                SignalDirection.StrongBuy => maxPositionWeight,
                SignalDirection.Bullish => maxPositionWeight * 0.80m,
                SignalDirection.Neutral => maxPositionWeight * 0.50m,
                SignalDirection.Bearish => maxPositionWeight * 0.20m,
                SignalDirection.StrongSell => 0.0m,
                _ => maxPositionWeight * 0.50m
            };

            var weightDeltaPct = targetWeightPct - currentWeightPct;
            var targetValue = totalEquity * (targetWeightPct / 100m);
            var valueDelta = targetValue - currentVal;

            RebalanceAction action = RebalanceAction.Hold;
            decimal recQty = 0m;
            decimal tradeValue = 0m;

            if (Math.Abs(weightDeltaPct) >= 2.0m) // Only rebalance if delta is at least 2% (avoids micro-churn)
            {
                if (weightDeltaPct > 0)
                {
                    action = currentVal > 0 ? RebalanceAction.Add : RebalanceAction.Buy;
                    recQty = Math.Floor((valueDelta / currentPrice) * 10_000m) / 10_000m;
                    if (!livePolicy.FractionalSharesEnabled) recQty = decimal.Floor(recQty);
                    recQty = Math.Min(recQty, decimal.Floor(maxOrderNotional / currentPrice));
                    tradeValue = recQty * currentPrice;

                    if (!livePolicy.EmergencyHaltActive
                        && projectedCash - tradeValue >= minimumCashReserve
                        && recQty > 0)
                    {
                        executableOrders.Add(new OrderRequest(
                            portfolio.Id,
                            symbol,
                            OrderSide.Buy,
                            OrderType.Limit,
                            recQty,
                            LimitPrice: Math.Round(currentPrice * 1.001m, 2)
                        ));
                        projectedCash -= tradeValue;
                        totalTurnoverDollars += tradeValue;
                    }
                }
                else
                {
                    action = targetWeightPct == 0 ? RebalanceAction.Sell : RebalanceAction.Trim;
                    recQty = Math.Floor((Math.Abs(valueDelta) / currentPrice) * 10_000m) / 10_000m;
                    if (!livePolicy.FractionalSharesEnabled) recQty = decimal.Floor(recQty);
                    recQty = Math.Min(recQty, decimal.Floor(maxOrderNotional / currentPrice));
                    recQty = Math.Min(recQty, pos.Quantity);
                    tradeValue = recQty * currentPrice;

                    if (recQty > 0)
                    {
                        executableOrders.Add(new OrderRequest(
                            portfolio.Id,
                            symbol,
                            OrderSide.Sell,
                            OrderType.Limit,
                            recQty,
                            LimitPrice: Math.Round(currentPrice * 0.999m, 2)
                        ));
                        projectedCash += tradeValue;
                        totalTurnoverDollars += tradeValue;
                    }
                }
            }

            allocationDeltas.Add(new AllocationDeltaItem(
                Symbol: symbol,
                CurrentQuantity: pos.Quantity,
                CurrentPrice: currentPrice,
                CurrentValue: Math.Round(currentVal, 2),
                CurrentWeightPercent: Math.Round(currentWeightPct, 2),
                TargetWeightPercent: Math.Round(targetWeightPct, 2),
                WeightDeltaPercent: Math.Round(weightDeltaPct, 2),
                Action: action,
                RecommendedQuantity: recQty,
                EstimatedTradeValue: Math.Round(tradeValue, 2),
                PersonaRationale: rationale,
                CommitteeSignal: overallSignal
            ));
        }

        var turnoverPct = totalEquity > 0 ? (totalTurnoverDollars / totalEquity) * 100m : 0m;
        var nextRebalance = DateTime.UtcNow.AddDays(14);
        _nextRebalanceDateUtc = nextRebalance;

        var maxTurnoverPercent = livePolicy.MaxDailyTurnoverPercent;
        var riskApproved = projectedCash >= minimumCashReserve
            && turnoverPct <= maxTurnoverPercent
            && allocationDeltas.All(item => item.TargetWeightPercent <= livePolicy.MaxPositionPercent)
            && (!livePolicy.EmergencyHaltActive || executableOrders.All(item => item.Side == OrderSide.Sell));
        var riskNotes = riskApproved
            ? $"Persisted live policy v{livePolicy.PolicyVersion}: plan is within the {livePolicy.MaxPositionPercent:F1}% position cap, preserves {livePolicy.MinimumCashReservePercent:F1}% cash, uses limit orders, and remains below the {maxTurnoverPercent:F0}% turnover ceiling."
            : $"Plan blocked by persisted live policy v{livePolicy.PolicyVersion}: position cap, cash reserve, emergency halt, or {maxTurnoverPercent:F0}% turnover ceiling would be breached.";

        var plan = new OptimizationPlan(
            Id: Guid.NewGuid(),
            PortfolioId: portfolio.Id,
            GeneratedAtUtc: DateTime.UtcNow,
            NextScheduledRebalanceUtc: nextRebalance,
            CurrentTotalEquity: Math.Round(totalEquity, 2),
            CurrentCash: Math.Round(currentCash, 2),
            ProjectedCash: Math.Round(projectedCash, 2),
            EstimatedTotalTurnoverPercent: Math.Round(turnoverPct, 2),
            Allocations: allocationDeltas,
            ExecutiveConsensusRationale: $"Autonomous bi-weekly multi-agent review conducted. Generated {executableOrders.Count} rebalancing orders with an estimated portfolio turnover of {turnoverPct:F1}%. High conviction assets were scaled up while overconcentrated / decelerating assets were trimmed.",
            IsRiskApproved: riskApproved,
            RiskAuditorNotes: riskNotes,
            ExecutableOrders: riskApproved ? executableOrders : new List<OrderRequest>(),
            LivePolicyVersion: livePolicy.PolicyVersion
        );

        _latestPlan = plan;
        return Result.Success(plan);
    }

    public async Task<Result<OptimizationExecutionResult>> ExecuteOptimizationPlanAsync(
        OptimizationPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (_latestPlan is null || _latestPlan.Id != plan.Id)
        {
            return Result.Failure<OptimizationExecutionResult>("This plan is stale or was not generated by the current server session. Generate a new plan.");
        }
        if (!_latestPlan.IsRiskApproved)
        {
            return Result.Failure<OptimizationExecutionResult>("The Risk Guard did not approve this plan.");
        }
        var livePolicy = await _livePolicyService.GetAsync(cancellationToken);
        if (livePolicy.PolicyVersion != _latestPlan.LivePolicyVersion)
            return Result.Failure<OptimizationExecutionResult>("The persisted safety policy changed after this plan was generated. Generate a new plan.");
        if (livePolicy.EmergencyHaltActive && _latestPlan.ExecutableOrders.Any(item => item.Side == OrderSide.Buy))
            return Result.Failure<OptimizationExecutionResult>("Emergency halt blocks execution of plans that increase exposure.");

        var executedOrders = new List<Order>();
        decimal rotatedCapital = 0m;

        // First execute all SELL/TRIM orders to free up liquidity
        var sellOrders = _latestPlan.ExecutableOrders.Where(o => o.Side == OrderSide.Sell).ToList();
        var buyOrders = _latestPlan.ExecutableOrders.Where(o => o.Side == OrderSide.Buy).ToList();

        foreach (var orderReq in sellOrders.Concat(buyOrders))
        {
            var submitRes = await _brokerClient.SubmitOrderAsync(orderReq, cancellationToken);
            if (submitRes.IsSuccess)
            {
                var order = submitRes.Value;
                executedOrders.Add(order);
                rotatedCapital += (order.FilledPrice ?? 0m) * order.FilledQuantity;
            }
        }

        return Result.Success(new OptimizationExecutionResult(
            PlanId: plan.Id,
            OrdersExecuted: executedOrders.Count,
            TotalCapitalRotated: Math.Round(rotatedCapital, 2),
            ExecutedOrders: executedOrders,
            Summary: $"Executed {executedOrders.Count} paper-trading rebalance orders, rotating ${rotatedCapital:N2} in the local simulation. No live Robinhood order was placed."
        ));
    }
}
