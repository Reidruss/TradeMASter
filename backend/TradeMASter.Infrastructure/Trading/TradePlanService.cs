using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Infrastructure.Trading;

public sealed class TradePlanService(
    TradeMASterDbContext dbContext,
    ILivePortfolioPolicyService livePolicyService,
    IRobinhoodService robinhoodService,
    IConfiguration configuration) : ITradePlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public async Task<Result<TradePlanView?>> CreateFromMarketRunAsync(
        MarketIntelligenceRun run,
        Portfolio portfolio,
        CancellationToken cancellationToken = default)
    {
        if (run.IsMockRun || !run.IsRiskApproved || run.ProposedPaperOrders.Count == 0)
            return Result.Success<TradePlanView?>(null);
        var existing = await dbContext.TradePlans
            .SingleOrDefaultAsync(item => item.SourceRunId == run.Id, cancellationToken);
        if (existing is not null) return Result.Success<TradePlanView?>(ToView(existing));

        var policy = await livePolicyService.GetAsync(cancellationToken);
        var accountResult = await robinhoodService.GetAccountStatusAsync(cancellationToken);
        if (accountResult.IsFailure || !accountResult.Value.IsConnected)
            return Result.Failure<TradePlanView?>(accountResult.Error ?? "Connected Agentic account is required to snapshot a trade plan.");
        var holdingsResult = await robinhoodService.GetLiveHoldingsAsync(cancellationToken);
        if (holdingsResult.IsFailure)
            return Result.Failure<TradePlanView?>(holdingsResult.Error!);

        var account = accountResult.Value;
        var holdings = holdingsResult.Value.OrderBy(item => item.Symbol, StringComparer.Ordinal).ToList();
        var holdingBySymbol = holdings.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var createdAt = DateTime.UtcNow;
        if (account.LastSyncedUtc > createdAt.AddSeconds(5)
            || createdAt - account.LastSyncedUtc > TimeSpan.FromSeconds(policy.MaxAccountSnapshotAgeSeconds))
            return Result.Failure<TradePlanView?>("The Agentic account snapshot is stale or has an invalid timestamp; refresh before creating a plan.");
        var expiresAt = createdAt.AddMinutes(policy.ApprovalExpiryMinutes);
        var orderSnapshots = run.ProposedPaperOrders
            .OrderBy(item => item.Side)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(item =>
            {
                holdingBySymbol.TryGetValue(item.Symbol, out var holding);
                var referencePrice = item.LimitPrice ?? holding?.CurrentPrice ?? 0m;
                var liquidation = item.Side == OrderSide.Sell
                    && holding is not null
                    && item.Quantity >= holding.Quantity - 0.0001m;
                return new TradePlanOrderSnapshot(
                    item.Symbol,
                    item.Side,
                    item.Type,
                    item.Quantity,
                    item.LimitPrice,
                    item.StopPrice,
                    Math.Round(item.Quantity * referencePrice, 2),
                    liquidation);
            }).ToList();
        var invalidOrder = orderSnapshots.FirstOrDefault(item =>
            string.IsNullOrWhiteSpace(item.Symbol)
            || item.Quantity <= 0m
            || !policy.AllowedOrderTypes.Contains(item.Type)
            || (item.Type == OrderType.Limit && (!item.LimitPrice.HasValue || item.LimitPrice <= 0m))
            || (!policy.FractionalSharesEnabled && item.Quantity != decimal.Truncate(item.Quantity))
            || item.EstimatedNotional > Math.Min(
                policy.MaxOrderNotionalAmount,
                account.TotalEquity * policy.MaxOrderNotionalPercent / 100m) + 0.01m);
        if (invalidOrder is not null)
            return Result.Failure<TradePlanView?>(
                $"Order for {invalidOrder.Symbol} does not satisfy the persisted phase-one order scope. Recalculate under the current policy.");

        var payload = new ImmutableTradePlanPayload(
            run.Id,
            portfolio.Id,
            createdAt,
            expiresAt,
            policy.PolicyVersion,
            new TradePlanAccountSnapshot(
                LastFour(account.AccountNumber),
                account.LastSyncedUtc,
                account.TotalEquity,
                account.CashAvailable,
                account.BuyingPower,
                holdings.Select(item => new TradePlanHoldingSnapshot(
                    item.Symbol,
                    item.Quantity,
                    item.CurrentPrice,
                    item.CurrentMarketValue,
                    item.PortfolioWeightPercent)).ToList()),
            run.MacroRegime,
            run.TargetAllocations,
            orderSnapshots,
            new TradePlanRiskSnapshot(
                run.IsRiskApproved,
                run.RiskAuditorFeedback,
                run.EstimatedTurnoverPercent,
                run.ProjectedAnnualizedVolatilityPercent,
                run.ParametricDailyVaR95Percent,
                run.TargetCashPercent),
            run.ReflectionSummary,
            run.DataSourceSummary,
            run.Candidates.ToDictionary(
                item => item.Symbol,
                item => (IReadOnlyList<string>)(item.FundamentalSources ?? []).ToList(),
                StringComparer.OrdinalIgnoreCase));
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var planHash = Hash(payloadJson);
        var secondaryThreshold = configuration.GetValue<decimal?>("TradePlans:SecondaryConfirmationNotionalAmount") ?? 100m;
        var totalNotional = orderSnapshots.Sum(item => item.EstimatedNotional);
        var secondaryReasons = new List<string>();
        if (orderSnapshots.Any(item => item.IsFullLiquidation)) secondaryReasons.Add("Includes full position liquidation");
        if (totalNotional >= secondaryThreshold)
            secondaryReasons.Add($"Total estimated notional ${totalNotional:N2} meets the ${secondaryThreshold:N2} material-plan threshold");

        var entity = new TradePlan(
            run.Id,
            portfolio.Id,
            planHash,
            payloadJson,
            expiresAt,
            policy.PolicyVersion,
            secondaryReasons.Count > 0,
            secondaryReasons);
        dbContext.TradePlans.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success<TradePlanView?>(ToView(entity));
    }

    public async Task<Result<TradePlanView?>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TradePlans.OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null) return Result.Success<TradePlanView?>(null);
        if (!VerifyPayload(entity))
        {
            entity.Invalidate("Stored immutable payload failed its SHA-256 integrity check.", DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure<TradePlanView?>("Trade plan integrity check failed and the plan was invalidated.");
        }
        await RefreshExpiryAsync(entity, cancellationToken);
        return Result.Success<TradePlanView?>(ToView(entity));
    }

    public async Task<Result<TradePlanView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TradePlans.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return Result.Failure<TradePlanView>("Trade plan not found.");
        if (!VerifyPayload(entity))
        {
            entity.Invalidate("Stored immutable payload failed its SHA-256 integrity check.", DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure<TradePlanView>("Trade plan integrity check failed and the plan was invalidated.");
        }
        await RefreshExpiryAsync(entity, cancellationToken);
        return Result.Success(ToView(entity));
    }

    public async Task<Result<TradePlanView>> ApproveAsync(
        Guid id,
        ApproveTradePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TradePlans.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return Result.Failure<TradePlanView>("Trade plan not found.");
        if (!VerifyPayload(entity))
        {
            entity.Invalidate("Stored immutable payload failed its SHA-256 integrity check.", DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure<TradePlanView>("Trade plan integrity check failed and the plan was invalidated.");
        }
        if (entity.Status == TradePlanStatus.Approved)
        {
            var repeated = entity.Approve(request.PlanHash, request.Confirmation, request.SecondaryConfirmation, DateTime.UtcNow);
            return repeated.IsSuccess ? Result.Success(ToView(entity)) : Result.Failure<TradePlanView>(repeated.Error!);
        }
        if (entity.RefreshExpiry(DateTime.UtcNow))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure<TradePlanView>("Trade plan expired before approval.");
        }
        if (entity.Status != TradePlanStatus.Proposed)
            return Result.Failure<TradePlanView>($"Only a proposed plan can be approved; current status is {entity.Status}.");
        if (!FixedHashEquals(request.PlanHash, entity.PlanHash))
            return Result.Failure<TradePlanView>("Plan hash mismatch; refresh and review the exact current plan.");
        if (!string.Equals(request.Confirmation?.Trim(), TradePlan.PrimaryApprovalConfirmation, StringComparison.Ordinal))
            return Result.Failure<TradePlanView>($"Exact confirmation '{TradePlan.PrimaryApprovalConfirmation}' is required.");
        if (entity.RequiresSecondaryConfirmation
            && !string.Equals(request.SecondaryConfirmation?.Trim(), TradePlan.SecondaryApprovalConfirmation, StringComparison.Ordinal))
            return Result.Failure<TradePlanView>($"This plan is material. Exact second confirmation '{TradePlan.SecondaryApprovalConfirmation}' is required.");

        var policy = await livePolicyService.GetAsync(cancellationToken);
        if (policy.PolicyVersion != entity.PolicyVersion)
            return await InvalidateFailureAsync(entity, "Persisted live policy changed after plan creation.", cancellationToken);
        if (policy.EmergencyHaltActive && Deserialize(entity).Orders.Any(item => item.Side == OrderSide.Buy))
            return await InvalidateFailureAsync(entity, "Emergency halt activated after plan creation.", cancellationToken);

        var drift = await DetectAccountDriftAsync(entity, policy, cancellationToken);
        if (drift.IsFailure) return Result.Failure<TradePlanView>(drift.Error!);
        if (!string.IsNullOrWhiteSpace(drift.Value))
            return await InvalidateFailureAsync(entity, drift.Value!, cancellationToken);

        var approval = entity.Approve(
            request.PlanHash,
            request.Confirmation,
            request.SecondaryConfirmation,
            DateTime.UtcNow);
        if (approval.IsFailure) return Result.Failure<TradePlanView>(approval.Error!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToView(entity));
    }

    public async Task<Result<TradePlanView>> RejectAsync(
        Guid id,
        RejectTradePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TradePlans.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return Result.Failure<TradePlanView>("Trade plan not found.");
        if (!VerifyPayload(entity))
            return await InvalidateFailureAsync(entity, "Stored immutable payload failed its SHA-256 integrity check.", cancellationToken);
        var result = entity.Reject(request.PlanHash, request.Reason, DateTime.UtcNow);
        if (result.IsFailure) return Result.Failure<TradePlanView>(result.Error!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToView(entity));
    }

    private async Task<Result<string?>> DetectAccountDriftAsync(
        TradePlan entity,
        LivePortfolioPolicySnapshot policy,
        CancellationToken cancellationToken)
    {
        var statusResult = await robinhoodService.GetAccountStatusAsync(cancellationToken);
        if (statusResult.IsFailure || !statusResult.Value.IsConnected)
            return Result.Failure<string?>(statusResult.Error ?? "Robinhood account is disconnected; approval fails closed.");
        var holdingsResult = await robinhoodService.GetLiveHoldingsAsync(cancellationToken);
        if (holdingsResult.IsFailure) return Result.Failure<string?>(holdingsResult.Error!);

        var payload = Deserialize(entity);
        var status = statusResult.Value;
        if (!string.Equals(LastFour(status.AccountNumber), payload.Account.AccountLastFour, StringComparison.Ordinal))
            return Result.Success<string?>("Connected Agentic account changed after plan creation.");
        if (PercentDrift(status.TotalEquity, payload.Account.TotalEquity) > policy.MaxPositionDriftPercent)
            return Result.Success<string?>("Account equity drift exceeded the persisted tolerance.");
        if (PercentDrift(status.CashAvailable, payload.Account.CashAvailable) > policy.MaxPositionDriftPercent)
            return Result.Success<string?>("Account cash drift exceeded the persisted tolerance.");

        var original = payload.Account.Holdings.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var current = holdingsResult.Value.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        if (!original.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(current.Keys))
            return Result.Success<string?>("Holding symbols changed after plan creation.");
        foreach (var holding in original.Values)
        {
            var latest = current[holding.Symbol];
            if (PercentDrift(latest.Quantity, holding.Quantity) > policy.MaxPositionDriftPercent)
                return Result.Success<string?>($"{holding.Symbol} quantity drift exceeded the persisted tolerance.");
            if (PercentDrift(latest.CurrentPrice, holding.CurrentPrice) > policy.MaxPriceDriftPercent)
                return Result.Success<string?>($"{holding.Symbol} price drift exceeded the persisted tolerance.");
        }
        return Result.Success<string?>(null);
    }

    private async Task<Result<TradePlanView>> InvalidateFailureAsync(
        TradePlan entity,
        string reason,
        CancellationToken cancellationToken)
    {
        entity.Invalidate(reason, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Failure<TradePlanView>($"Trade plan was invalidated: {reason}");
    }

    private async Task RefreshExpiryAsync(TradePlan entity, CancellationToken cancellationToken)
    {
        if (!entity.RefreshExpiry(DateTime.UtcNow)) return;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool VerifyPayload(TradePlan entity) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(entity.PayloadJson)),
            Encoding.UTF8.GetBytes(entity.PlanHash));

    private static bool FixedHashEquals(string? supplied, string expected) =>
        supplied is not null
        && supplied.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(expected));

    private static string Hash(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();

    private static string LastFour(string accountNumber) =>
        accountNumber.Length <= 4 ? accountNumber : accountNumber[^4..];

    private static decimal PercentDrift(decimal current, decimal original)
    {
        if (current == original) return 0m;
        if (original == 0m) return 100m;
        return Math.Abs(current - original) / Math.Abs(original) * 100m;
    }

    private static ImmutableTradePlanPayload Deserialize(TradePlan entity) =>
        JsonSerializer.Deserialize<ImmutableTradePlanPayload>(entity.PayloadJson, JsonOptions)
        ?? throw new InvalidOperationException("Stored trade plan payload is invalid.");

    private static TradePlanView ToView(TradePlan entity) => new(
        entity.Id,
        entity.SourceRunId,
        entity.PortfolioId,
        entity.Status,
        entity.PlanHash,
        entity.CreatedAt,
        entity.ExpiresAtUtc,
        entity.PolicyVersion,
        entity.RequiresSecondaryConfirmation,
        entity.SecondaryConfirmationReasons.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        entity.ApprovedAtUtc,
        entity.RejectedAtUtc,
        entity.InvalidatedAtUtc,
        entity.DecisionReason,
        Deserialize(entity));
}
