using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Infrastructure.Trading;

public sealed class LiveExecutionService(
    TradeMASterDbContext dbContext,
    IRobinhoodLiveExecutionAdapter brokerAdapter,
    ILivePortfolioPolicyService policyService,
    ILiveExecutionAuthority executionAuthority,
    IUsMarketCalendar marketCalendar) : ILiveExecutionService, ILiveExecutionReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ReconciliationGates = new();

    public async Task<Result<LiveExecutionBatchView?>> GetByTradePlanAsync(
        Guid tradePlanId,
        CancellationToken cancellationToken = default)
    {
        var batch = await BatchQuery(asNoTracking: true)
            .SingleOrDefaultAsync(item => item.TradePlanId == tradePlanId, cancellationToken);
        return Result.Success<LiveExecutionBatchView?>(batch is null ? null : ToView(batch));
    }

    public async Task<Result<LiveExecutionBatchView>> ExecuteApprovedPlanAsync(
        Guid tradePlanId,
        ExecuteApprovedTradePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await LoadBatchAsync(tradePlanId, cancellationToken);
        if (existing is not null)
        {
            await ResumeIfSafeAsync(existing, cancellationToken);
            return Result.Success(ToView(await LoadBatchAsync(tradePlanId, cancellationToken) ?? existing));
        }

        var plan = await dbContext.TradePlans.SingleOrDefaultAsync(item => item.Id == tradePlanId, cancellationToken);
        if (plan is null) return Result.Failure<LiveExecutionBatchView>("Trade plan not found.");
        if (!VerifyPayload(plan)) return await InvalidatePlanAsync(plan, "Immutable plan payload failed its SHA-256 integrity check.", cancellationToken);
        if (!FixedHashEquals(request.PlanHash, plan.PlanHash))
            return Result.Failure<LiveExecutionBatchView>("Plan hash mismatch; submission cannot target a different plan.");
        if (!string.Equals(request.Confirmation?.Trim(), TradePlan.LiveSubmissionConfirmation, StringComparison.Ordinal))
            return Result.Failure<LiveExecutionBatchView>($"Exact confirmation '{TradePlan.LiveSubmissionConfirmation}' is required.");
        if (plan.Status != TradePlanStatus.Approved)
            return Result.Failure<LiveExecutionBatchView>($"Only an approved plan can enter broker preflight; current status is {plan.Status}.");
        if (DateTime.UtcNow >= plan.ExpiresAtUtc)
            return await InvalidatePlanAsync(plan, "Approved plan expired before broker preflight.", cancellationToken);

        var now = DateTime.UtcNow;
        var unresolved = await dbContext.LiveExecutionBatches.AsNoTracking().FirstOrDefaultAsync(item =>
            item.TradePlanId != tradePlanId
            && item.Status != LiveExecutionBatchStatus.SubmissionBlocked
            && item.Status != LiveExecutionBatchStatus.Completed
            && item.Status != LiveExecutionBatchStatus.Cancelled
            && item.Status != LiveExecutionBatchStatus.Expired
            && item.Status != LiveExecutionBatchStatus.Failed,
            cancellationToken);
        if (unresolved is not null)
            return Result.Failure<LiveExecutionBatchView>(
                $"Live activity is blocked until execution batch {unresolved.Id} reaches a proven terminal state.");
        var policy = await policyService.GetAsync(cancellationToken);
        if (policy.PolicyVersion != plan.PolicyVersion)
            return await InvalidatePlanAsync(plan, "Persisted live policy changed after plan approval.", cancellationToken);
        if (policy.RegularMarketHoursOnly && !marketCalendar.IsRegularSession(now))
            return Result.Failure<LiveExecutionBatchView>(marketCalendar.DescribeClosure(now));

        var payload = Deserialize(plan);
        var symbols = payload.Orders.Select(item => item.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var brokerSnapshotResult = await brokerAdapter.GetFreshPreflightSnapshotAsync(symbols, cancellationToken);
        if (brokerSnapshotResult.IsFailure)
            return Result.Failure<LiveExecutionBatchView>(brokerSnapshotResult.Error!);
        var snapshot = brokerSnapshotResult.Value;
        var drift = DetectSnapshotDrift(payload, snapshot, policy);
        if (drift is not null) return await InvalidatePlanAsync(plan, drift, cancellationToken);
        if (snapshot.OpenOrders.Count > 0)
            return await InvalidatePlanAsync(plan, "Open Robinhood equity orders exist; reconcile them before submitting a new batch.", cancellationToken);

        var preparation = await PrepareCommandsAsync(plan, payload, snapshot, policy, now, cancellationToken);
        if (preparation.IsFailure) return await InvalidatePlanAsync(plan, preparation.Error!, cancellationToken);
        var commands = preparation.Value;
        var totalBuy = commands.Where(item => item.Command.Side == OrderSide.Buy).Sum(item => item.Command.Quantity * item.Command.LimitPrice);
        var totalSell = commands.Where(item => item.Command.Side == OrderSide.Sell).Sum(item => item.Command.Quantity * item.Command.LimitPrice);
        var minimumCash = snapshot.TotalEquity * policy.MinimumCashReservePercent / 100m;
        var reservableBuyingPower = Math.Max(0m, Math.Min(snapshot.CashAvailable, snapshot.BuyingPower) - minimumCash);
        if (totalBuy > reservableBuyingPower + 0.01m)
            return await InvalidatePlanAsync(plan,
                $"Batch buys require ${totalBuy:N2}, but only ${reservableBuyingPower:N2} remains after the mandatory cash reserve. Sell proceeds are not counted before settlement.",
                cancellationToken);

        var authority = executionAuthority.Verify(policy);
        var sanitizedSnapshot = JsonSerializer.Serialize(new
        {
            accountLastFour = LastFour(snapshot.AccountNumber),
            snapshot.AsOfUtc,
            snapshot.TotalEquity,
            snapshot.CashAvailable,
            snapshot.BuyingPower,
            snapshot.CurrentDailyTurnoverPercent,
            holdings = snapshot.Holdings.Select(item => new { item.Symbol, item.Quantity, item.CurrentPrice, item.CurrentMarketValue }),
            quotes = snapshot.Quotes.Select(item => new { item.Symbol, item.Price, item.Bid, item.Ask, item.AsOfUtc, item.Source }),
            eligibility = snapshot.Eligibility.Select(item => new { item.Symbol, item.IsTradable, item.SupportsFractionalShares, item.AssetType, item.Exchange, item.Source }),
            openOrderCount = snapshot.OpenOrders.Count
        }, JsonOptions);
        var batch = new LiveExecutionBatch(
            plan.Id,
            plan.PlanHash,
            LastFour(snapshot.AccountNumber),
            now,
            sanitizedSnapshot,
            totalBuy,
            totalBuy,
            totalSell,
            authority.IsSuccess,
            authority.Error);
        foreach (var item in commands)
        {
            batch.Attempts.Add(new LiveExecutionOrderAttempt(
                batch.Id,
                item.Sequence,
                item.Command.ClientOrderId,
                item.Command.IdempotencyKey,
                item.Command.Symbol,
                item.Command.Side,
                item.Command.Type,
                item.Command.Quantity,
                item.Command.LimitPrice,
                item.Command.Quantity * item.Command.LimitPrice,
                item.SanitizedRequestJson,
                item.SanitizedReviewJson));
        }

        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            dbContext.LiveExecutionBatches.Add(batch);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var raced = await LoadBatchAsync(tradePlanId, cancellationToken);
            if (raced is null) throw;
            await ResumeIfSafeAsync(raced, cancellationToken);
            return Result.Success(ToView(await LoadBatchAsync(tradePlanId, cancellationToken) ?? raced));
        }

        if (authority.IsSuccess) await ProcessOutboxAsync(batch.Id, cancellationToken);
        return Result.Success(ToView(await LoadBatchAsync(tradePlanId, cancellationToken) ?? batch));
    }

    private async Task<Result<IReadOnlyList<PreparedCommand>>> PrepareCommandsAsync(
        TradePlan plan,
        ImmutableTradePlanPayload payload,
        BrokerExecutionSnapshot snapshot,
        LivePortfolioPolicySnapshot policy,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var holdings = snapshot.Holdings.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var quotes = snapshot.Quotes.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var eligibility = snapshot.Eligibility.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var ordered = payload.Orders.OrderByDescending(item => item.Side == OrderSide.Sell)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal).ToList();
        var results = new List<PreparedCommand>();
        var runningCash = Math.Min(snapshot.CashAvailable, snapshot.BuyingPower);
        var runningTurnover = snapshot.CurrentDailyTurnoverPercent;

        for (var index = 0; index < ordered.Count; index++)
        {
            var order = ordered[index];
            if (!quotes.TryGetValue(order.Symbol, out var quote) || quote.Price <= 0m)
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"Robinhood returned no positive quote for {order.Symbol}.");
            if (quote.AsOfUtc > now.AddSeconds(5) || now - quote.AsOfUtc > TimeSpan.FromSeconds(policy.MaxQuoteAgeSeconds))
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"{order.Symbol} quote is stale or future-dated.");
            if (!eligibility.TryGetValue(order.Symbol, out var instrument) || !instrument.IsTradable)
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"Robinhood does not confirm {order.Symbol} as currently tradable.");
            if (string.IsNullOrWhiteSpace(instrument.Exchange))
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"Robinhood/local metadata did not identify {order.Symbol}'s exchange; preflight fails closed.");
            if (order.Quantity != decimal.Truncate(order.Quantity)
                && (!policy.FractionalSharesEnabled || !instrument.SupportsFractionalShares))
                return Result.Failure<IReadOnlyList<PreparedCommand>>(
                    $"Fractional quantity for {order.Symbol} is not jointly authorized by policy and Robinhood eligibility.");
            if (order.LimitPrice is null or <= 0m)
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"{order.Symbol} has no positive approved limit price.");
            if (PercentDrift(quote.Price, order.LimitPrice.Value) > policy.MaxPriceDriftPercent)
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"{order.Symbol} price drift exceeded the approved tolerance.");
            if (order.Side == OrderSide.Sell
                && (!holdings.TryGetValue(order.Symbol, out var holding) || holding.Quantity + 0.000001m < order.Quantity))
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"Current {order.Symbol} quantity cannot satisfy the approved sell order.");

            holdings.TryGetValue(order.Symbol, out var currentHolding);
            var allocation = payload.TargetAllocations.FirstOrDefault(item => item.Symbol.Equals(order.Symbol, StringComparison.OrdinalIgnoreCase));
            var projectedSector = allocation is null ? 0m : payload.TargetAllocations
                .Where(item => item.Sector.Equals(allocation.Sector, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.TargetWeightPercent);
            var dailyLoss = snapshot.TotalEquity < payload.Account.TotalEquity && payload.Account.TotalEquity > 0m
                ? (payload.Account.TotalEquity - snapshot.TotalEquity) / payload.Account.TotalEquity * 100m : 0m;
            var request = new OrderRequest(
                payload.PortfolioId, order.Symbol, order.Side, order.Type, order.Quantity, order.LimitPrice, order.StopPrice);
            var context = new LiveOrderPolicyContext(
                instrument.AssetType,
                instrument.Exchange,
                quote.Price,
                snapshot.TotalEquity,
                runningCash,
                currentHolding?.CurrentMarketValue ?? 0m,
                quote.AsOfUtc,
                snapshot.AsOfUtc,
                runningTurnover,
                dailyLoss,
                payload.Risk.HistoricalMaxDrawdownPercent,
                projectedSector,
                payload.Risk.ProjectedAnnualizedVolatilityPercent,
                payload.Risk.ParametricDailyVaR95Percent,
                now);
            var policyResult = await policyService.ValidatePreflightOrderAsync(request, context, cancellationToken);
            if (policyResult.IsFailure) return Result.Failure<IReadOnlyList<PreparedCommand>>($"{order.Symbol}: {policyResult.Error}");

            var idempotencyKey = Hash($"{plan.Id:N}|{plan.PlanHash}|{index}|{order.Symbol}|{order.Side}|{order.Quantity}|{order.LimitPrice.Value}");
            var clientOrderId = StableGuid(idempotencyKey);
            var command = new BrokerOrderCommand(
                snapshot.AccountNumber,
                clientOrderId,
                idempotencyKey,
                order.Symbol,
                order.Side,
                order.Type,
                order.Quantity,
                order.LimitPrice.Value);
            var review = await brokerAdapter.ReviewOrderAsync(command, cancellationToken);
            if (review.IsFailure) return Result.Failure<IReadOnlyList<PreparedCommand>>($"{order.Symbol}: {review.Error}");
            if (!review.Value.IsApproved)
                return Result.Failure<IReadOnlyList<PreparedCommand>>($"Robinhood pre-trade review raised new warnings for {order.Symbol}: {string.Join("; ", review.Value.Warnings)}");
            var sanitizedRequest = JsonSerializer.Serialize(new
            {
                clientOrderId,
                idempotencyKey,
                command.Symbol,
                command.Side,
                command.Type,
                command.Quantity,
                command.LimitPrice,
                command.TimeInForce
            }, JsonOptions);
            results.Add(new PreparedCommand(index, command, sanitizedRequest, review.Value.SanitizedResponseJson));
            var notional = command.Quantity * command.LimitPrice;
            runningTurnover += notional / snapshot.TotalEquity * 100m;
            if (command.Side == OrderSide.Buy) runningCash -= notional;
            // Sell proceeds are deliberately excluded from same-batch buying power.
        }
        return Result.Success<IReadOnlyList<PreparedCommand>>(results);
    }

    private async Task ResumeIfSafeAsync(LiveExecutionBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Status is LiveExecutionBatchStatus.SubmissionBlocked
            or LiveExecutionBatchStatus.Submitted
            or LiveExecutionBatchStatus.PartiallyFilled
            or LiveExecutionBatchStatus.CancelPending
            or LiveExecutionBatchStatus.Completed
            or LiveExecutionBatchStatus.Cancelled
            or LiveExecutionBatchStatus.Expired
            or LiveExecutionBatchStatus.Failed
            or LiveExecutionBatchStatus.ReconciliationRequired) return;
        await ProcessOutboxAsync(batch.Id, cancellationToken);
    }

    private async Task ProcessOutboxAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await dbContext.LiveExecutionBatches.Include(item => item.Attempts)
            .SingleAsync(item => item.Id == batchId, cancellationToken);
        if (batch.Status == LiveExecutionBatchStatus.PreflightPassed)
        {
            batch.MarkSubmitting(DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (batch.Attempts.Any(item => item.Status is LiveExecutionAttemptStatus.BrokerAccepted
            or LiveExecutionAttemptStatus.PartiallyFilled or LiveExecutionAttemptStatus.CancelPending)) return;
        foreach (var attemptId in batch.Attempts.OrderBy(item => item.Sequence).Select(item => item.Id))
        {
            dbContext.ChangeTracker.Clear();
            var attempt = await dbContext.LiveExecutionOrderAttempts.SingleAsync(item => item.Id == attemptId, cancellationToken);
            if (attempt.Status is LiveExecutionAttemptStatus.Filled or LiveExecutionAttemptStatus.Cancelled
                or LiveExecutionAttemptStatus.Expired or LiveExecutionAttemptStatus.Skipped) continue;
            if (attempt.Status is LiveExecutionAttemptStatus.BrokerRejected
                or LiveExecutionAttemptStatus.ReconciliationRequired) return;
            if (attempt.Status == LiveExecutionAttemptStatus.Submitting)
            {
                if (attempt.LastAttemptAtUtc < DateTime.UtcNow.AddMinutes(-2))
                {
                    attempt.MarkReconciliationRequired("A prior submission started without a provable broker result.", null, DateTime.UtcNow);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await MarkBatchReconciliationAsync(batchId, attempt.FailureReason!, cancellationToken);
                }
                return;
            }
            if (attempt.Status != LiveExecutionAttemptStatus.Pending) return;
            var claimedAt = DateTime.UtcNow;
            var claimed = await dbContext.LiveExecutionOrderAttempts
                .Where(item => item.Id == attemptId && item.Status == LiveExecutionAttemptStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, LiveExecutionAttemptStatus.Submitting)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.LastAttemptAtUtc, claimedAt)
                    .SetProperty(item => item.UpdatedAt, claimedAt), cancellationToken);
            if (claimed == 0) return;
            dbContext.ChangeTracker.Clear();
            attempt = await dbContext.LiveExecutionOrderAttempts.SingleAsync(item => item.Id == attemptId, cancellationToken);

            var command = new BrokerOrderCommand(
                string.Empty,
                attempt.ClientOrderId,
                attempt.IdempotencyKey,
                attempt.Symbol,
                attempt.Side,
                attempt.Type,
                attempt.Quantity,
                attempt.LimitPrice);
            // The adapter resolves the current saved Agentic account; the account number is never persisted in the outbox.
            var account = await brokerAdapter.GetFreshPreflightSnapshotAsync([attempt.Symbol], cancellationToken);
            var immediateFailure = account.IsFailure
                ? account.Error
                : await ValidateImmediateSnapshotAsync(batchId, attempt, account.Value, cancellationToken);
            if (!string.IsNullOrWhiteSpace(immediateFailure))
            {
                attempt.MarkReconciliationRequired(
                    $"Immediate broker revalidation failed before submission: {immediateFailure}", null, DateTime.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                await MarkBatchReconciliationAsync(batchId, attempt.FailureReason!, cancellationToken);
                return;
            }
            command = command with { AccountNumber = account.Value.AccountNumber };
            var receipt = await brokerAdapter.PlaceOrderAsync(command, cancellationToken);
            if (receipt.Outcome == BrokerSubmissionOutcome.Accepted && !string.IsNullOrWhiteSpace(receipt.BrokerOrderId))
            {
                var receivedAt = DateTime.UtcNow;
                attempt.MarkAccepted(receipt.BrokerOrderId, receipt.SanitizedResponseJson, receivedAt);
                dbContext.LiveExecutionBrokerInbox.Add(new LiveExecutionBrokerInbox(
                    batchId,
                    attempt.Id,
                    attempt.ClientOrderId,
                    receipt.BrokerOrderId,
                    "accepted",
                    receipt.SanitizedResponseJson,
                    receivedAt));
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    dbContext.ChangeTracker.Clear();
                    await MarkBatchReconciliationAsync(
                        batchId,
                        "A broker acceptance receipt conflicted with the durable inbox; manual reconciliation is required.",
                        cancellationToken);
                    return;
                }
                dbContext.ChangeTracker.Clear();
                var submitted = await dbContext.LiveExecutionBatches.SingleAsync(item => item.Id == batchId, cancellationToken);
                submitted.MarkSubmitted(receivedAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                // Milestone 3 deliberately permits only one active order. The reconciler advances the outbox.
                return;
            }
            if (receipt.Outcome == BrokerSubmissionOutcome.Rejected)
            {
                attempt.MarkRejected(receipt.Message, receipt.SanitizedResponseJson, DateTime.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                await MarkBatchFailedAsync(batchId, receipt.Message, cancellationToken);
                await SkipPendingAsync(batchId, "A prior batch order was rejected; remaining orders were not submitted.", cancellationToken);
                return;
            }
            attempt.MarkReconciliationRequired(receipt.Message, receipt.SanitizedResponseJson, DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            await MarkBatchReconciliationAsync(batchId, receipt.Message, cancellationToken);
            return;
        }
        dbContext.ChangeTracker.Clear();
        var complete = await dbContext.LiveExecutionBatches.Include(item => item.Attempts)
            .SingleAsync(item => item.Id == batchId, cancellationToken);
        if (complete.Attempts.Any(item => item.Status == LiveExecutionAttemptStatus.BrokerAccepted))
            complete.MarkSubmitted(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ValidateImmediateSnapshotAsync(
        Guid batchId,
        LiveExecutionOrderAttempt attempt,
        BrokerExecutionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var batch = await dbContext.LiveExecutionBatches.AsNoTracking().SingleAsync(item => item.Id == batchId, cancellationToken);
        if (!string.Equals(LastFour(snapshot.AccountNumber), batch.AccountLastFour, StringComparison.Ordinal))
            return "Agentic account identity changed.";
        var plan = await dbContext.TradePlans.AsNoTracking().SingleAsync(item => item.Id == batch.TradePlanId, cancellationToken);
        if (plan.Status != TradePlanStatus.Approved || DateTime.UtcNow >= plan.ExpiresAtUtc)
            return "The approved plan is no longer valid or has expired.";
        if (!VerifyPayload(plan) || !FixedHashEquals(batch.PlanHash, plan.PlanHash))
            return "The persisted plan or batch hash failed integrity validation.";
        var policy = await policyService.GetAsync(cancellationToken);
        if (policy.PolicyVersion != plan.PolicyVersion) return "The live policy version changed.";
        var authority = executionAuthority.Verify(policy);
        if (authority.IsFailure) return authority.Error;
        if (policy.RegularMarketHoursOnly && !marketCalendar.IsRegularSession(DateTime.UtcNow))
            return marketCalendar.DescribeClosure(DateTime.UtcNow);
        if (snapshot.AsOfUtc > DateTime.UtcNow.AddSeconds(5)
            || DateTime.UtcNow - snapshot.AsOfUtc > TimeSpan.FromSeconds(policy.MaxAccountSnapshotAgeSeconds))
            return "The account snapshot is stale or future-dated.";
        var quote = snapshot.Quotes.SingleOrDefault(item => item.Symbol.Equals(attempt.Symbol, StringComparison.OrdinalIgnoreCase));
        if (quote is null || quote.Price <= 0m) return $"No current positive {attempt.Symbol} quote was returned.";
        if (quote.AsOfUtc > DateTime.UtcNow.AddSeconds(5)
            || DateTime.UtcNow - quote.AsOfUtc > TimeSpan.FromSeconds(policy.MaxQuoteAgeSeconds))
            return $"{attempt.Symbol} quote is stale or future-dated.";
        if (PercentDrift(quote.Price, attempt.LimitPrice) > policy.MaxPriceDriftPercent)
            return $"{attempt.Symbol} price drift exceeded the approved tolerance.";
        var instrument = snapshot.Eligibility.SingleOrDefault(item => item.Symbol.Equals(attempt.Symbol, StringComparison.OrdinalIgnoreCase));
        if (instrument is null || !instrument.IsTradable || string.IsNullOrWhiteSpace(instrument.Exchange))
            return $"{attempt.Symbol} is no longer confirmed eligible and tradable.";
        if (attempt.Quantity != decimal.Truncate(attempt.Quantity)
            && (!policy.FractionalSharesEnabled || !instrument.SupportsFractionalShares))
            return $"{attempt.Symbol} is no longer eligible for the approved fractional quantity.";
        var acceptedBrokerIds = await dbContext.LiveExecutionOrderAttempts.AsNoTracking()
            .Where(item => item.BatchId == batchId && item.BrokerOrderId != null)
            .Select(item => item.BrokerOrderId!).ToListAsync(cancellationToken);
        var unknownOpenOrder = snapshot.OpenOrders.FirstOrDefault(item =>
            !acceptedBrokerIds.Contains(item.BrokerOrderId, StringComparer.OrdinalIgnoreCase));
        if (unknownOpenOrder is not null)
            return $"Unknown open Robinhood order {unknownOpenOrder.BrokerOrderId} appeared during the batch.";
        if (attempt.Side == OrderSide.Sell)
        {
            var holding = snapshot.Holdings.SingleOrDefault(item => item.Symbol.Equals(attempt.Symbol, StringComparison.OrdinalIgnoreCase));
            if (holding is null || holding.Quantity + 0.000001m < attempt.Quantity)
                return $"Current {attempt.Symbol} quantity cannot satisfy the approved sell.";
        }
        else
        {
            var remainingBuyNotional = await dbContext.LiveExecutionOrderAttempts.AsNoTracking()
                .Where(item => item.BatchId == batchId
                    && item.Side == OrderSide.Buy
                    && (item.Status == LiveExecutionAttemptStatus.Pending || item.Status == LiveExecutionAttemptStatus.Submitting))
                .SumAsync(item => item.EstimatedNotional, cancellationToken);
            var minimumCash = snapshot.TotalEquity * policy.MinimumCashReservePercent / 100m;
            var available = Math.Max(0m, Math.Min(snapshot.CashAvailable, snapshot.BuyingPower) - minimumCash);
            if (remainingBuyNotional > available + 0.01m)
                return "Fresh buying power cannot reserve every remaining batch buy while preserving the cash floor.";
        }
        return null;
    }

    private async Task MarkBatchFailedAsync(Guid batchId, string reason, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var batch = await dbContext.LiveExecutionBatches.SingleAsync(item => item.Id == batchId, cancellationToken);
        batch.MarkFailed(reason, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkBatchReconciliationAsync(Guid batchId, string reason, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var batch = await dbContext.LiveExecutionBatches.SingleAsync(item => item.Id == batchId, cancellationToken);
        batch.MarkReconciliationRequired(reason, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SkipPendingAsync(Guid batchId, string reason, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var pending = await dbContext.LiveExecutionOrderAttempts.Where(item =>
            item.BatchId == batchId && item.Status == LiveExecutionAttemptStatus.Pending).ToListAsync(cancellationToken);
        foreach (var attempt in pending) attempt.MarkSkipped(reason, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<LiveExecutionBatchView>> ReconcileAsync(
        Guid tradePlanId,
        CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(tradePlanId, cancellationToken);
        if (batch is null) return Result.Failure<LiveExecutionBatchView>("No live execution batch exists for this trade plan.");
        return await ReconcileBatchAsync(batch.Id, cancellationToken);
    }

    public async Task<int> ReconcileActiveBatchesAsync(CancellationToken cancellationToken = default)
    {
        var activeIds = await dbContext.LiveExecutionBatches.AsNoTracking()
            .Where(item => item.Status == LiveExecutionBatchStatus.PreflightPassed
                || item.Status == LiveExecutionBatchStatus.Submitting
                || item.Status == LiveExecutionBatchStatus.Submitted
                || item.Status == LiveExecutionBatchStatus.PartiallyFilled
                || item.Status == LiveExecutionBatchStatus.CancelPending
                || item.Status == LiveExecutionBatchStatus.ReconciliationRequired)
            .OrderBy(item => item.CreatedAt)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var reconciled = 0;
        foreach (var batchId in activeIds)
        {
            var result = await ReconcileBatchAsync(batchId, cancellationToken);
            if (result.IsSuccess) reconciled++;
        }
        return reconciled;
    }

    public async Task<Result<LiveExecutionBatchView>> ReconcileBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var gate = ReconciliationGates.GetOrAdd(batchId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReconcileBatchCoreAsync(batchId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<LiveExecutionBatchView>> ReconcileBatchCoreAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var batch = await BatchQuery().SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<LiveExecutionBatchView>("Live execution batch not found.");
        if (batch.Status is LiveExecutionBatchStatus.SubmissionBlocked or LiveExecutionBatchStatus.Completed
            or LiveExecutionBatchStatus.Cancelled or LiveExecutionBatchStatus.Expired)
            return Result.Success(ToView(batch));

        var symbols = batch.Attempts.Select(item => item.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var historyResult = await brokerAdapter.GetOrderHistoryAsync(batch.CreatedAt.AddMinutes(-5), cancellationToken);
        var accountResult = await brokerAdapter.GetFreshPreflightSnapshotAsync(symbols, cancellationToken);
        if (historyResult.IsFailure || accountResult.IsFailure)
            return await RequireInterventionAsync(batch,
                historyResult.IsFailure ? historyResult.Error! : accountResult.Error!, cancellationToken);
        var history = historyResult.Value;
        var account = accountResult.Value;
        if (!string.Equals(LastFour(history.AccountNumber), batch.AccountLastFour, StringComparison.Ordinal)
            || !string.Equals(LastFour(account.AccountNumber), batch.AccountLastFour, StringComparison.Ordinal))
            return await RequireInterventionAsync(batch, "The Agentic account identity changed during reconciliation.", cancellationToken);

        var matchedOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observedAt = DateTime.UtcNow;
        foreach (var attempt in batch.Attempts.Where(item => item.Status != LiveExecutionAttemptStatus.Pending
            && item.Status != LiveExecutionAttemptStatus.Skipped))
        {
            var matches = history.Orders.Where(order =>
                    (!string.IsNullOrWhiteSpace(attempt.BrokerOrderId)
                        && order.BrokerOrderId.Equals(attempt.BrokerOrderId, StringComparison.OrdinalIgnoreCase))
                    || (order.ClientOrderId.HasValue && order.ClientOrderId.Value == attempt.ClientOrderId))
                .DistinctBy(item => item.BrokerOrderId, StringComparer.OrdinalIgnoreCase).ToList();
            if (matches.Count != 1)
                return await RequireInterventionAsync(batch,
                    matches.Count == 0
                        ? $"Robinhood history cannot prove the tracked {attempt.Symbol} order {attempt.ClientOrderId}."
                        : $"Robinhood history returned conflicting orders for client order ID {attempt.ClientOrderId}.",
                    cancellationToken);
            var brokerOrder = matches[0];
            matchedOrderIds.Add(brokerOrder.BrokerOrderId);
            var divergence = ValidateBrokerOrder(attempt, brokerOrder);
            if (divergence is not null) return await RequireInterventionAsync(batch, divergence, cancellationToken);
            if (attempt.BrokerOrderId is null)
                attempt.RecoverBrokerAcceptance(brokerOrder.BrokerOrderId, brokerOrder.SanitizedPayloadJson, observedAt);

            var eventKey = Hash($"{attempt.Id:N}|{brokerOrder.BrokerOrderId}|{NormalizeBrokerState(brokerOrder.State)}|{brokerOrder.FilledQuantity}|{brokerOrder.AverageFillPrice}|{brokerOrder.UpdatedAtUtc:O}");
            if (!attempt.Events.Any(item => item.EventKey == eventKey))
                dbContext.LiveExecutionOrderEvents.Add(new LiveExecutionOrderEvent(
                    batch.Id, attempt.Id, eventKey, brokerOrder.BrokerOrderId, brokerOrder.State,
                    brokerOrder.OrderedQuantity, brokerOrder.FilledQuantity, brokerOrder.AverageFillPrice,
                    brokerOrder.UpdatedAtUtc, observedAt, brokerOrder.SanitizedPayloadJson));
            try
            {
                attempt.ApplyBrokerState(brokerOrder.State, brokerOrder.FilledQuantity, observedAt);
            }
            catch (InvalidOperationException ex)
            {
                return await RequireInterventionAsync(batch, $"{attempt.Symbol}: {ex.Message}", cancellationToken);
            }
        }

        var unknown = history.Orders.FirstOrDefault(order => order.CreatedAtUtc >= batch.CreatedAt.AddSeconds(-5)
            && !matchedOrderIds.Contains(order.BrokerOrderId));
        if (unknown is not null)
            return await RequireInterventionAsync(batch,
                $"Unknown Robinhood order {unknown.BrokerOrderId} ({unknown.Symbol}) appeared during this batch.",
                cancellationToken);

        var policy = await policyService.GetAsync(cancellationToken);
        var plan = await dbContext.TradePlans.AsNoTracking().SingleAsync(item => item.Id == batch.TradePlanId, cancellationToken);
        var payload = Deserialize(plan);
        var (riskJson, buyBlockReason) = EvaluatePostFillRisk(payload, account, policy);
        var brokerJson = JsonSerializer.Serialize(new
        {
            accountLastFour = LastFour(account.AccountNumber),
            account.AsOfUtc,
            account.TotalEquity,
            account.CashAvailable,
            account.BuyingPower,
            holdings = account.Holdings.Select(item => new { item.Symbol, item.Quantity, item.CurrentPrice, item.CurrentMarketValue }),
            openOrders = account.OpenOrders.Select(item => new { item.BrokerOrderId, item.Symbol, item.Side, item.Quantity, item.State }),
            observedOrders = history.Orders.Count
        }, JsonOptions);
        var state = batch.ReconciliationState ?? new LiveExecutionReconciliationState(batch.Id);
        if (batch.ReconciliationState is null) dbContext.LiveExecutionReconciliationStates.Add(state);
        state.Record(observedAt, brokerJson, riskJson);

        if (!string.IsNullOrWhiteSpace(buyBlockReason))
        {
            foreach (var pendingBuy in batch.Attempts.Where(item => item.Side == OrderSide.Buy
                && item.Status == LiveExecutionAttemptStatus.Pending))
                pendingBuy.MarkSkipped($"Post-fill risk gate blocked remaining buys: {buyBlockReason}", observedAt);
            var activeBuy = batch.Attempts.FirstOrDefault(item => item.Side == OrderSide.Buy
                && item.Status is LiveExecutionAttemptStatus.BrokerAccepted or LiveExecutionAttemptStatus.PartiallyFilled);
            if (activeBuy is not null)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return await RequestCancellationAsync(batch.Id, activeBuy.Id,
                    $"Post-fill risk gate blocked the unfilled remainder: {buyBlockReason}", cancellationToken);
            }
        }

        var incompleteSell = batch.Attempts.FirstOrDefault(item => item.Side == OrderSide.Sell
            && (item.Status is LiveExecutionAttemptStatus.Cancelled or LiveExecutionAttemptStatus.Expired
                or LiveExecutionAttemptStatus.BrokerRejected)
            && (item.Events.OrderByDescending(value => value.BrokerUpdatedAtUtc).FirstOrDefault()?.FilledQuantity ?? 0m)
                + 0.000001m < item.Quantity);
        if (incompleteSell is not null)
        {
            foreach (var pendingBuy in batch.Attempts.Where(item => item.Side == OrderSide.Buy
                && item.Status == LiveExecutionAttemptStatus.Pending))
                pendingBuy.MarkSkipped(
                    $"The preceding {incompleteSell.Symbol} sell did not fill completely; dependent buys require a new approved plan.",
                    observedAt);
        }

        var rejected = batch.Attempts.FirstOrDefault(item => item.Status == LiveExecutionAttemptStatus.BrokerRejected);
        if (rejected is not null)
        {
            foreach (var pending in batch.Attempts.Where(item => item.Status == LiveExecutionAttemptStatus.Pending))
                pending.MarkSkipped("A reconciled broker rejection stopped the remaining batch.", observedAt);
            batch.MarkFailed(rejected.FailureReason ?? "Robinhood rejected a tracked order.", observedAt);
        }

        var timedOut = batch.Attempts.FirstOrDefault(item => (item.Status is LiveExecutionAttemptStatus.BrokerAccepted
                or LiveExecutionAttemptStatus.PartiallyFilled)
            && item.LastAttemptAtUtc.HasValue
            && observedAt - item.LastAttemptAtUtc.Value >= TimeSpan.FromSeconds(policy.OrderTimeoutSeconds));
        if (timedOut is not null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return await RequestCancellationAsync(batch.Id, timedOut.Id,
                $"The {policy.OrderTimeoutSeconds}-second deterministic order timeout elapsed.", cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entries = string.Join(", ", ex.Entries.Select(item => $"{item.Metadata.ClrType.Name}:{item.State}"));
            throw new InvalidOperationException($"Reconciliation persistence concurrency conflict: {entries}", ex);
        }
        if (batch.Attempts.All(item => IsTerminal(item.Status)))
            return await VerifyFinalPortfolioAsync(batch.Id, account, payload, cancellationToken);

        var hasPartial = batch.Attempts.Any(item => item.Status == LiveExecutionAttemptStatus.PartiallyFilled);
        var hasCancel = batch.Attempts.Any(item => item.Status == LiveExecutionAttemptStatus.CancelPending);
        if (hasCancel) batch.MarkCancelPending(observedAt);
        else if (hasPartial) batch.MarkPartiallyFilled(observedAt);
        else if (batch.Status != LiveExecutionBatchStatus.Failed) batch.MarkSubmitted(observedAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!hasPartial && !hasCancel
            && batch.Attempts.All(item => IsTerminal(item.Status) || item.Status == LiveExecutionAttemptStatus.Pending))
        {
            await ProcessOutboxAsync(batch.Id, cancellationToken);
        }
        dbContext.ChangeTracker.Clear();
        var refreshed = await BatchQuery(asNoTracking: true).SingleAsync(item => item.Id == batch.Id, cancellationToken);
        return Result.Success(ToView(refreshed));
    }

    private async Task<Result<LiveExecutionBatchView>> RequestCancellationAsync(
        Guid batchId,
        Guid attemptId,
        string reason,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var batch = await BatchQuery().SingleAsync(item => item.Id == batchId, cancellationToken);
        var attempt = batch.Attempts.Single(item => item.Id == attemptId);
        if (string.IsNullOrWhiteSpace(attempt.BrokerOrderId))
            return await RequireInterventionAsync(batch, $"{reason} Cancellation cannot be bound to a broker order ID.", cancellationToken);
        var cancellation = await brokerAdapter.CancelOrderAsync(attempt.BrokerOrderId, cancellationToken);
        if (cancellation.Outcome != BrokerSubmissionOutcome.Accepted)
            return await RequireInterventionAsync(batch,
                $"{reason} Robinhood cancellation was not proven: {cancellation.Message}", cancellationToken);
        attempt.MarkCancelPending(DateTime.UtcNow);
        batch.MarkCancelPending(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToView(batch));
    }

    private async Task<Result<LiveExecutionBatchView>> VerifyFinalPortfolioAsync(
        Guid batchId,
        BrokerExecutionSnapshot account,
        ImmutableTradePlanPayload payload,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var batch = await BatchQuery().SingleAsync(item => item.Id == batchId, cancellationToken);
        var expected = ReadPreflightHoldings(batch.PreflightSnapshotJson);
        if (expected is null)
            return await RequireInterventionAsync(batch,
                "The preflight snapshot predates durable holdings capture, so the final portfolio cannot be proven automatically.",
                cancellationToken);
        foreach (var attempt in batch.Attempts)
        {
            var latest = attempt.Events.OrderByDescending(item => item.BrokerUpdatedAtUtc)
                .ThenByDescending(item => item.ObservedAtUtc).FirstOrDefault();
            if (latest is null || latest.FilledQuantity == 0m) continue;
            expected.TryGetValue(attempt.Symbol, out var current);
            expected[attempt.Symbol] = current + (attempt.Side == OrderSide.Buy ? latest.FilledQuantity : -latest.FilledQuantity);
        }
        var actual = account.Holdings.ToDictionary(item => item.Symbol, item => item.Quantity, StringComparer.OrdinalIgnoreCase);
        var symbols = expected.Keys.Union(actual.Keys, StringComparer.OrdinalIgnoreCase);
        var mismatch = symbols.FirstOrDefault(symbol =>
            Math.Abs(expected.GetValueOrDefault(symbol) - actual.GetValueOrDefault(symbol)) > 0.000001m);
        if (mismatch is not null)
            return await RequireInterventionAsync(batch,
                $"Final {mismatch} quantity differs from reconciled fills by more than 0.000001 share.", cancellationToken);
        if (account.OpenOrders.Count > 0)
            return await RequireInterventionAsync(batch, "Robinhood still reports open equity orders after every local order became terminal.", cancellationToken);

        var startingCash = ReadPreflightCash(batch.PreflightSnapshotJson);
        if (!startingCash.HasValue)
            return await RequireInterventionAsync(batch, "The exact preflight cash balance is unavailable for final verification.", cancellationToken);
        var expectedCash = startingCash.Value;
        foreach (var attempt in batch.Attempts)
        {
            var latest = attempt.Events.OrderByDescending(item => item.BrokerUpdatedAtUtc)
                .ThenByDescending(item => item.ObservedAtUtc).FirstOrDefault();
            if (latest is null || latest.FilledQuantity == 0m) continue;
            var price = latest.AverageFillPrice ?? attempt.LimitPrice;
            expectedCash += (attempt.Side == OrderSide.Sell ? 1m : -1m) * latest.FilledQuantity * price;
        }
        if (Math.Abs(expectedCash - account.CashAvailable) > 0.05m)
            return await RequireInterventionAsync(batch,
                $"Final cash differs from reconciled fills by more than the explicit $0.05 rounding tolerance.", cancellationToken);

        var finalJson = JsonSerializer.Serialize(new
        {
            verifiedAtUtc = DateTime.UtcNow,
            quantityTolerance = 0.000001m,
            cashTolerance = 0.05m,
            expectedCash,
            actualCash = account.CashAvailable,
            holdings = actual.OrderBy(item => item.Key).Select(item => new { symbol = item.Key, quantity = item.Value }),
            openOrderCount = account.OpenOrders.Count
        }, JsonOptions);
        var state = batch.ReconciliationState ?? new LiveExecutionReconciliationState(batch.Id);
        if (batch.ReconciliationState is null) dbContext.LiveExecutionReconciliationStates.Add(state);
        state.VerifyFinal(finalJson, DateTime.UtcNow);
        if (batch.Attempts.Any(item => item.Status == LiveExecutionAttemptStatus.BrokerRejected))
            batch.MarkFailed("A broker rejection was reconciled and the final portfolio was verified.", DateTime.UtcNow);
        else if (batch.Attempts.Any(item => item.Status == LiveExecutionAttemptStatus.Expired)) batch.MarkExpired(DateTime.UtcNow);
        else if (batch.Attempts.All(item => item.Status is LiveExecutionAttemptStatus.Cancelled or LiveExecutionAttemptStatus.Skipped))
            batch.MarkCancelled(DateTime.UtcNow);
        else batch.MarkCompleted(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToView(batch));
    }

    private async Task<Result<LiveExecutionBatchView>> RequireInterventionAsync(
        LiveExecutionBatch batch,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var state = batch.ReconciliationState ?? new LiveExecutionReconciliationState(batch.Id);
        if (batch.ReconciliationState is null) dbContext.LiveExecutionReconciliationStates.Add(state);
        state.RequireIntervention(reason, now);
        batch.MarkReconciliationRequired(reason, now);
        foreach (var pendingBuy in batch.Attempts.Where(item => item.Side == OrderSide.Buy
            && item.Status == LiveExecutionAttemptStatus.Pending))
            pendingBuy.MarkSkipped("Manual intervention is required before new live exposure can continue.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToView(batch));
    }

    private static string? ValidateBrokerOrder(LiveExecutionOrderAttempt attempt, BrokerOrderLifecycleSnapshot order)
    {
        if (!order.Symbol.Equals(attempt.Symbol, StringComparison.OrdinalIgnoreCase) || order.Side != attempt.Side)
            return $"Broker order {order.BrokerOrderId} symbol or side diverges from the approved {attempt.Symbol} intent.";
        if (order.OrderedQuantity <= 0m || Math.Abs(order.OrderedQuantity - attempt.Quantity) > 0.000001m)
            return $"Broker order {order.BrokerOrderId} quantity diverges from the approved quantity.";
        if (order.LimitPrice.HasValue && Math.Abs(order.LimitPrice.Value - attempt.LimitPrice) > 0.0001m)
            return $"Broker order {order.BrokerOrderId} limit price diverges from the approved limit.";
        if (order.FilledQuantity < 0m || order.FilledQuantity > attempt.Quantity + 0.000001m)
            return $"Broker order {order.BrokerOrderId} reports an impossible filled quantity.";
        var prior = attempt.Events.OrderByDescending(item => item.BrokerUpdatedAtUtc)
            .ThenByDescending(item => item.ObservedAtUtc).FirstOrDefault();
        if (prior is not null && order.FilledQuantity + 0.000001m < prior.FilledQuantity)
            return $"Broker order {order.BrokerOrderId} cumulative fill quantity regressed from the prior observation.";
        if (IsTerminal(attempt.Status) && !IsEquivalentTerminalState(attempt.Status, order.State))
            return $"Broker order {order.BrokerOrderId} changed after the local lifecycle recorded a terminal state.";
        return null;
    }

    private static bool IsEquivalentTerminalState(LiveExecutionAttemptStatus status, string brokerState)
    {
        var state = NormalizeBrokerState(brokerState);
        return status switch
        {
            LiveExecutionAttemptStatus.Filled => state == "filled",
            LiveExecutionAttemptStatus.Cancelled => state is "cancelled" or "canceled",
            LiveExecutionAttemptStatus.Expired => state == "expired",
            LiveExecutionAttemptStatus.BrokerRejected => state is "rejected" or "failed",
            LiveExecutionAttemptStatus.Skipped => true,
            _ => false
        };
    }

    private static (string Json, string? BuyBlockReason) EvaluatePostFillRisk(
        ImmutableTradePlanPayload payload,
        BrokerExecutionSnapshot account,
        LivePortfolioPolicySnapshot policy)
    {
        var breaches = new List<string>();
        var minimumCash = account.TotalEquity * policy.MinimumCashReservePercent / 100m;
        if (account.CashAvailable + 0.01m < minimumCash) breaches.Add("cash reserve fell below policy");
        var positions = account.Holdings.Select(item => new
        {
            item.Symbol,
            Weight = account.TotalEquity > 0m ? item.CurrentMarketValue / account.TotalEquity * 100m : 100m
        }).ToList();
        var oversized = positions.FirstOrDefault(item => item.Weight > policy.MaxPositionPercent + 0.0001m);
        if (oversized is not null) breaches.Add($"{oversized.Symbol} exceeded the position limit");
        var sectors = payload.TargetAllocations.ToDictionary(item => item.Symbol, item => item.Sector, StringComparer.OrdinalIgnoreCase);
        var unknownSector = account.Holdings.FirstOrDefault(item => !sectors.ContainsKey(item.Symbol));
        if (unknownSector is not null) breaches.Add($"{unknownSector.Symbol} has no proven sector mapping");
        var sectorWeights = account.Holdings.Where(item => sectors.ContainsKey(item.Symbol))
            .GroupBy(item => sectors[item.Symbol], StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Sector = group.Key,
                Weight = account.TotalEquity > 0m ? group.Sum(item => item.CurrentMarketValue) / account.TotalEquity * 100m : 100m
            }).ToList();
        var oversizedSector = sectorWeights.FirstOrDefault(item => item.Weight > policy.MaxSectorPercent + 0.0001m);
        if (oversizedSector is not null) breaches.Add($"{oversizedSector.Sector} exceeded the sector limit");
        var dailyLoss = payload.Account.TotalEquity > 0m && account.TotalEquity < payload.Account.TotalEquity
            ? (payload.Account.TotalEquity - account.TotalEquity) / payload.Account.TotalEquity * 100m : 0m;
        if (dailyLoss > policy.MaxDailyLossPercent) breaches.Add("daily loss exceeded policy");
        if (payload.Risk.ProjectedAnnualizedVolatilityPercent > policy.MaxAnnualizedVolatilityPercent)
            breaches.Add("projected volatility exceeds policy");
        if (payload.Risk.ParametricDailyVaR95Percent > policy.MaxDailyVaR95Percent)
            breaches.Add("projected VaR exceeds policy");
        if (payload.Risk.HistoricalMaxDrawdownPercent > policy.MaxDrawdownPercent)
            breaches.Add("historical drawdown exceeds policy");
        if (policy.EmergencyHaltActive) breaches.Add("emergency halt is active");
        if (policy.PolicyVersion != payload.PolicyVersion) breaches.Add("live policy version changed");
        var json = JsonSerializer.Serialize(new
        {
            evaluatedAtUtc = DateTime.UtcNow,
            account.TotalEquity,
            account.CashAvailable,
            minimumCash,
            dailyLossPercent = dailyLoss,
            positions,
            sectors = sectorWeights,
            inheritedForecast = new
            {
                payload.Risk.ProjectedAnnualizedVolatilityPercent,
                payload.Risk.ParametricDailyVaR95Percent,
                payload.Risk.HistoricalMaxDrawdownPercent
            },
            remainingBuysAllowed = breaches.Count == 0,
            breaches
        }, JsonOptions);
        return (json, breaches.Count == 0 ? null : string.Join("; ", breaches));
    }

    private static Dictionary<string, decimal>? ReadPreflightHoldings(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("holdings", out var holdings) || holdings.ValueKind != JsonValueKind.Array) return null;
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in holdings.EnumerateArray())
        {
            if (!item.TryGetProperty("symbol", out var symbol) || !item.TryGetProperty("quantity", out var quantity)) continue;
            var key = symbol.GetString();
            if (!string.IsNullOrWhiteSpace(key) && quantity.TryGetDecimal(out var value)) result[key] = value;
        }
        return result;
    }

    private static decimal? ReadPreflightCash(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("cashAvailable", out var cash) && cash.TryGetDecimal(out var value) ? value : null;
    }

    private static bool IsTerminal(LiveExecutionAttemptStatus status) => status is
        LiveExecutionAttemptStatus.Filled or LiveExecutionAttemptStatus.Cancelled
        or LiveExecutionAttemptStatus.Expired or LiveExecutionAttemptStatus.BrokerRejected
        or LiveExecutionAttemptStatus.Skipped;

    private static string NormalizeBrokerState(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task<Result<LiveExecutionBatchView>> InvalidatePlanAsync(
        TradePlan plan,
        string reason,
        CancellationToken cancellationToken)
    {
        plan.Invalidate(reason.Length <= 500 ? reason : reason[..500], DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Failure<LiveExecutionBatchView>($"Trade plan was invalidated: {reason}");
    }

    private Task<LiveExecutionBatch?> LoadBatchAsync(Guid tradePlanId, CancellationToken cancellationToken) =>
        BatchQuery()
            .SingleOrDefaultAsync(item => item.TradePlanId == tradePlanId, cancellationToken);

    private IQueryable<LiveExecutionBatch> BatchQuery(bool asNoTracking = false)
    {
        IQueryable<LiveExecutionBatch> query = dbContext.LiveExecutionBatches;
        if (asNoTracking) query = query.AsNoTracking();
        return query.Include(item => item.Attempts).ThenInclude(item => item.Events)
            .Include(item => item.ReconciliationState);
    }

    private static string? DetectSnapshotDrift(
        ImmutableTradePlanPayload payload,
        BrokerExecutionSnapshot snapshot,
        LivePortfolioPolicySnapshot policy)
    {
        if (!string.Equals(LastFour(snapshot.AccountNumber), payload.Account.AccountLastFour, StringComparison.Ordinal))
            return "Connected Agentic account changed after approval.";
        if (snapshot.AsOfUtc > DateTime.UtcNow.AddSeconds(5)
            || DateTime.UtcNow - snapshot.AsOfUtc > TimeSpan.FromSeconds(policy.MaxAccountSnapshotAgeSeconds))
            return "Fresh Agentic account snapshot is stale or future-dated.";
        if (PercentDrift(snapshot.TotalEquity, payload.Account.TotalEquity) > policy.MaxPositionDriftPercent)
            return "Account equity drift exceeded the approved tolerance.";
        if (PercentDrift(snapshot.CashAvailable, payload.Account.CashAvailable) > policy.MaxPositionDriftPercent)
            return "Account cash drift exceeded the approved tolerance.";
        var oldHoldings = payload.Account.Holdings.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var newHoldings = snapshot.Holdings.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        if (!oldHoldings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(newHoldings.Keys))
            return "Holding symbols changed after approval.";
        foreach (var holding in oldHoldings.Values)
        {
            var current = newHoldings[holding.Symbol];
            if (PercentDrift(current.Quantity, holding.Quantity) > policy.MaxPositionDriftPercent)
                return $"{holding.Symbol} quantity drift exceeded the approved tolerance.";
        }
        return null;
    }

    private static bool VerifyPayload(TradePlan plan) => FixedHashEquals(Hash(plan.PayloadJson), plan.PlanHash);
    private static bool FixedHashEquals(string? supplied, string expected) => supplied is not null
        && supplied.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Guid StableGuid(string hash)
    {
        var bytes = Convert.FromHexString(hash[..32]);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
    private static decimal PercentDrift(decimal current, decimal original) => current == original ? 0m
        : original == 0m ? 100m : Math.Abs(current - original) / Math.Abs(original) * 100m;
    private static string LastFour(string accountNumber) => accountNumber.Length <= 4 ? accountNumber : accountNumber[^4..];
    private static ImmutableTradePlanPayload Deserialize(TradePlan plan) =>
        JsonSerializer.Deserialize<ImmutableTradePlanPayload>(plan.PayloadJson, JsonOptions)
        ?? throw new InvalidOperationException("Stored immutable plan payload is invalid.");

    private static LiveExecutionBatchView ToView(LiveExecutionBatch batch) => LiveExecutionViewFactory.Create(batch);

    private sealed record PreparedCommand(
        int Sequence,
        BrokerOrderCommand Command,
        string SanitizedRequestJson,
        string SanitizedReviewJson);
}
