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
    IUsMarketCalendar marketCalendar) : ILiveExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<LiveExecutionBatchView?>> GetByTradePlanAsync(
        Guid tradePlanId,
        CancellationToken cancellationToken = default)
    {
        var batch = await dbContext.LiveExecutionBatches.AsNoTracking().Include(item => item.Attempts)
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
        foreach (var attemptId in batch.Attempts.OrderBy(item => item.Sequence).Select(item => item.Id))
        {
            dbContext.ChangeTracker.Clear();
            var attempt = await dbContext.LiveExecutionOrderAttempts.SingleAsync(item => item.Id == attemptId, cancellationToken);
            if (attempt.Status == LiveExecutionAttemptStatus.BrokerAccepted) continue;
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
                continue;
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
        if (complete.Attempts.All(item => item.Status == LiveExecutionAttemptStatus.BrokerAccepted))
        {
            complete.MarkSubmitted(DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
        dbContext.LiveExecutionBatches.Include(item => item.Attempts)
            .SingleOrDefaultAsync(item => item.TradePlanId == tradePlanId, cancellationToken);

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

    private static LiveExecutionBatchView ToView(LiveExecutionBatch batch) => new(
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
        batch.Attempts.OrderBy(item => item.Sequence).Select(item => new LiveExecutionAttemptView(
            item.Id, item.Sequence, item.ClientOrderId, item.IdempotencyKey, item.Symbol, item.Side, item.Type,
            item.Quantity, item.LimitPrice, item.EstimatedNotional, item.Status, item.BrokerOrderId,
            item.AttemptCount, item.LastAttemptAtUtc, item.FailureReason, item.SanitizedRequestJson,
            item.SanitizedReviewJson, item.SanitizedResponseJson)).ToList());

    private sealed record PreparedCommand(
        int Sequence,
        BrokerOrderCommand Command,
        string SanitizedRequestJson,
        string SanitizedReviewJson);
}
