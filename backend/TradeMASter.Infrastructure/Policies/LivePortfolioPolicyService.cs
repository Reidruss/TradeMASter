using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Infrastructure.Policies;

public sealed class LivePortfolioPolicyService(
    TradeMASterDbContext dbContext,
    IConfiguration configuration) : ILivePortfolioPolicyService
{
    public async Task<LivePortfolioPolicySnapshot> GetAsync(CancellationToken cancellationToken = default) =>
        (await GetEntityAsync(cancellationToken)).ToSnapshot();

    public async Task<Result<LivePortfolioPolicySnapshot>> UpdateAsync(
        UpdateLivePortfolioPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetEntityAsync(cancellationToken);
        var result = policy.Apply(request);
        if (result.IsFailure) return Result.Failure<LivePortfolioPolicySnapshot>(result.Error!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(policy.ToSnapshot());
    }

    public async Task<Result<LivePortfolioPolicySnapshot>> ActivateEmergencyHaltAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetEntityAsync(cancellationToken);
        var result = policy.ActivateEmergencyHalt(reason);
        if (result.IsFailure) return Result.Failure<LivePortfolioPolicySnapshot>(result.Error!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(policy.ToSnapshot());
    }

    public async Task<Result<LivePortfolioPolicySnapshot>> ClearEmergencyHaltAsync(
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetEntityAsync(cancellationToken);
        var result = policy.ClearEmergencyHalt(confirmation);
        if (result.IsFailure) return Result.Failure<LivePortfolioPolicySnapshot>(result.Error!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(policy.ToSnapshot());
    }

    public async Task<Result> ValidatePreflightOrderAsync(
        OrderRequest request,
        LiveOrderPolicyContext context,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetEntityAsync(cancellationToken);
        if (request.Quantity <= 0m) return Result.Failure("Live order quantity must be greater than zero.");
        if (context.ReferencePrice <= 0m) return Result.Failure("A positive, current reference price is required.");
        if (context.TotalEquity <= 0m) return Result.Failure("A positive account equity snapshot is required.");

        if (policy.EmergencyHaltActive && request.Side == OrderSide.Buy)
            return Result.Failure($"Emergency halt blocks new exposure: {policy.EmergencyHaltReason}");
        if (!policy.AllowedAssetTypes.Contains(context.AssetType))
            return Result.Failure($"Asset type {context.AssetType} is not permitted by live policy.");
        if (!policy.AllowedExchanges.Contains(context.Exchange.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase))
            return Result.Failure($"Exchange {context.Exchange} is not permitted by live policy.");
        if (!policy.AllowedOrderTypes.Contains(request.Type))
            return Result.Failure($"Order type {request.Type} is not permitted; initial live policy allows Limit orders only.");
        if (!policy.FractionalSharesEnabled && request.Quantity != decimal.Truncate(request.Quantity))
            return Result.Failure("Fractional live orders are disabled until broker eligibility preflight is implemented.");
        if (request.Type == OrderType.Limit && (!request.LimitPrice.HasValue || request.LimitPrice <= 0m))
            return Result.Failure("A positive limit price is required for live limit orders.");

        var now = context.EvaluationTimeUtc ?? DateTime.UtcNow;
        if (context.QuoteAsOfUtc > now.AddSeconds(5)
            || now - context.QuoteAsOfUtc > TimeSpan.FromSeconds(policy.MaxQuoteAgeSeconds))
            return Result.Failure($"Quote is stale; maximum age is {policy.MaxQuoteAgeSeconds} seconds.");
        if (context.AccountSnapshotAsOfUtc > now.AddSeconds(5)
            || now - context.AccountSnapshotAsOfUtc > TimeSpan.FromSeconds(policy.MaxAccountSnapshotAgeSeconds))
            return Result.Failure($"Account snapshot is stale; maximum age is {policy.MaxAccountSnapshotAgeSeconds} seconds.");

        var notional = request.Quantity * (request.LimitPrice ?? context.ReferencePrice);
        var maxNotional = Math.Min(
            policy.MaxOrderNotionalAmount,
            context.TotalEquity * policy.MaxOrderNotionalPercent / 100m);
        if (notional > maxNotional + 0.01m)
            return Result.Failure($"Order notional ${notional:N2} exceeds the live limit of ${maxNotional:N2}.");
        var orderTurnover = notional / context.TotalEquity * 100m;
        if (context.CurrentDailyTurnoverPercent + orderTurnover > policy.MaxDailyTurnoverPercent + 0.01m)
            return Result.Failure("Order would exceed the maximum daily turnover policy.");
        if (context.CurrentDailyLossPercent >= policy.MaxDailyLossPercent)
            return Result.Failure("Daily-loss circuit breaker blocks live order submission.");
        if (context.CurrentDrawdownPercent >= policy.MaxDrawdownPercent)
            return Result.Failure("Drawdown circuit breaker blocks live order submission.");

        if (request.Side == OrderSide.Buy)
        {
            var projectedCash = context.AvailableCash - notional;
            var minimumCash = context.TotalEquity * policy.MinimumCashReservePercent / 100m;
            if (projectedCash < minimumCash - 0.01m)
                return Result.Failure($"Order would reduce cash below the {policy.MinimumCashReservePercent:F1}% reserve.");
            var projectedPositionPercent = (context.CurrentPositionValue + notional) / context.TotalEquity * 100m;
            if (projectedPositionPercent > policy.MaxPositionPercent + 0.01m)
                return Result.Failure("Order would exceed the maximum single-position exposure.");
            if (context.ProjectedSectorPercent <= 0m
                || context.ProjectedPortfolioVolatilityPercent <= 0m
                || context.ProjectedDailyVaR95Percent <= 0m)
                return Result.Failure("Projected sector, volatility, and VaR metrics are required before increasing live exposure.");
            if (context.ProjectedSectorPercent > policy.MaxSectorPercent + 0.01m)
                return Result.Failure("Order would exceed the maximum sector exposure.");
            if (context.ProjectedPortfolioVolatilityPercent > policy.MaxAnnualizedVolatilityPercent + 0.01m)
                return Result.Failure("Order would exceed the portfolio volatility limit.");
            if (context.ProjectedDailyVaR95Percent > policy.MaxDailyVaR95Percent + 0.01m)
                return Result.Failure("Order would exceed the one-day 95% VaR limit.");
        }

        return Result.Success();
    }

    public async Task<Result> ValidateLiveOrderAsync(
        OrderRequest request,
        LiveOrderPolicyContext context,
        CancellationToken cancellationToken = default)
    {
        var preflight = await ValidatePreflightOrderAsync(request, context, cancellationToken);
        if (preflight.IsFailure) return preflight;
        var policy = await GetEntityAsync(cancellationToken);
        if (policy.RegularMarketHoursOnly && !IsRegularUsMarketSession(context.EvaluationTimeUtc ?? DateTime.UtcNow))
            return Result.Failure("Initial live policy permits submission only during regular U.S. market hours.");
        if (!policy.LiveTradingEnabled || !configuration.GetValue<bool>("Robinhood:LiveTradingEnabled"))
            return Result.Failure("Live order submission is disabled by both persisted policy and application configuration.");
        return Result.Success();
    }

    private async Task<LivePortfolioPolicy> GetEntityAsync(CancellationToken cancellationToken)
    {
        var policy = await dbContext.LivePortfolioPolicies
            .SingleOrDefaultAsync(item => item.Id == LivePortfolioPolicy.SingletonId, cancellationToken);
        if (policy is not null) return policy;
        policy = new LivePortfolioPolicy();
        dbContext.LivePortfolioPolicies.Add(policy);
        await dbContext.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private static bool IsRegularUsMarketSession(DateTime utcNow)
    {
        TimeZoneInfo eastern;
        try
        {
            eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), eastern);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        var time = local.TimeOfDay;
        return time >= TimeSpan.FromHours(9.5) && time < TimeSpan.FromHours(16);
    }
}
