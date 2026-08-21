using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;
using TradeMASter.Infrastructure.Policies;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class LivePortfolioPolicyTests
{
    [Fact]
    public void Defaults_AreNarrowAndCannotEnableLiveTrading()
    {
        var policy = new LivePortfolioPolicy();
        var snapshot = policy.ToSnapshot();

        snapshot.LiveTradingEnabled.Should().BeFalse();
        snapshot.AllowedAssetTypes.Should().BeEquivalentTo([AssetType.Stock, AssetType.Etf]);
        snapshot.AllowedOrderTypes.Should().Equal(OrderType.Limit);
        snapshot.RegularMarketHoursOnly.Should().BeTrue();
        snapshot.FractionalSharesEnabled.Should().BeFalse();
        snapshot.MinimumCashReservePercent.Should().Be(20m);
        snapshot.MaxOrderNotionalAmount.Should().Be(100m);
    }

    [Fact]
    public void Apply_RejectsPolicyExpansionBeyondInitialScope()
    {
        var policy = new LivePortfolioPolicy();
        var unsafeRequest = ValidRequest() with
        {
            AllowedAssetTypes = [AssetType.Stock, AssetType.Crypto],
            AllowedOrderTypes = [OrderType.Market],
            RegularMarketHoursOnly = false
        };

        var result = policy.Apply(unsafeRequest);

        result.IsFailure.Should().BeTrue();
        policy.ToSnapshot().PolicyVersion.Should().Be(1);
        policy.ToSnapshot().LiveTradingEnabled.Should().BeFalse();
    }

    [Fact]
    public void Apply_RejectsAttemptToLoosenPhaseOneRiskEnvelope()
    {
        var policy = new LivePortfolioPolicy();

        var result = policy.Apply(ValidRequest() with
        {
            MinimumCashReservePercent = 1m,
            MaxPositionPercent = 80m,
            MaxSectorPercent = 100m,
            MaxOrderNotionalAmount = 50_000m
        });

        result.IsFailure.Should().BeTrue();
        policy.ToSnapshot().MinimumCashReservePercent.Should().Be(20m);
        policy.ToSnapshot().MaxPositionPercent.Should().Be(20m);
        policy.ToSnapshot().MaxOrderNotionalAmount.Should().Be(100m);
    }

    [Fact]
    public void EmergencyHalt_RequiresReasonAndExactResumeConfirmation()
    {
        var policy = new LivePortfolioPolicy();

        policy.ActivateEmergencyHalt("stop new exposure during reconciliation").IsSuccess.Should().BeTrue();
        policy.ToSnapshot().EmergencyHaltActive.Should().BeTrue();
        policy.ClearEmergencyHalt("resume").IsFailure.Should().BeTrue();
        policy.ToSnapshot().EmergencyHaltActive.Should().BeTrue();
        policy.ClearEmergencyHalt("RESUME SUPERVISED OPERATIONS").IsSuccess.Should().BeTrue();
        policy.ToSnapshot().EmergencyHaltActive.Should().BeFalse();
        policy.ToSnapshot().LiveTradingEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Service_PersistsPolicyAndEmergencyHaltAcrossContexts()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var configuration = Configuration(liveTradingEnabled: false);

        await using (var firstDb = new TradeMASterDbContext(options))
        {
            var service = new LivePortfolioPolicyService(firstDb, configuration);
            var update = await service.UpdateAsync(ValidRequest() with { MinimumCashReservePercent = 25m });
            update.IsSuccess.Should().BeTrue(update.Error);
            (await service.ActivateEmergencyHaltAsync("operator requested emergency stop")).IsSuccess.Should().BeTrue();
        }

        await using var secondDb = new TradeMASterDbContext(options);
        var reloaded = await new LivePortfolioPolicyService(secondDb, configuration).GetAsync();
        reloaded.MinimumCashReservePercent.Should().Be(25m);
        reloaded.EmergencyHaltActive.Should().BeTrue();
        reloaded.EmergencyHaltReason.Should().Be("operator requested emergency stop");
        reloaded.PolicyVersion.Should().Be(3);
        reloaded.LiveTradingEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateLiveOrder_BlocksNewExposureDuringEmergencyHalt()
    {
        var (service, db) = Service();
        await using var ownedDb = db;
        await service.ActivateEmergencyHaltAsync("account state cannot be reconciled");
        var request = new OrderRequest(
            Guid.NewGuid(), "AAPL", OrderSide.Buy, OrderType.Limit, 1m, LimitPrice: 50m);

        var result = await service.ValidateLiveOrderAsync(request, ValidContext());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Emergency halt");
    }

    [Fact]
    public async Task ValidateLiveOrder_RejectsDisallowedAssetBeforeBrokerAuthorityCheck()
    {
        var (service, db) = Service();
        await using var ownedDb = db;
        var request = new OrderRequest(
            Guid.NewGuid(), "BTC-USD", OrderSide.Buy, OrderType.Limit, 1m, LimitPrice: 50m);

        var result = await service.ValidateLiveOrderAsync(
            request,
            ValidContext() with { AssetType = AssetType.Crypto, Exchange = "COINBASE" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Asset type Crypto");
    }

    [Fact]
    public async Task ValidateLiveOrder_KeepsSubmissionDisabledAfterAllPolicyChecksPass()
    {
        var (service, db) = Service();
        await using var ownedDb = db;
        var request = new OrderRequest(
            Guid.NewGuid(), "AAPL", OrderSide.Buy, OrderType.Limit, 1m, LimitPrice: 50m);

        var result = await service.ValidateLiveOrderAsync(request, ValidContext());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Live order submission is disabled");
    }

    private static (LivePortfolioPolicyService Service, TradeMASterDbContext Db) Service()
    {
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        return (new LivePortfolioPolicyService(db, Configuration(false)), db);
    }

    private static IConfiguration Configuration(bool liveTradingEnabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Robinhood:LiveTradingEnabled"] = liveTradingEnabled.ToString()
        }).Build();

    private static LiveOrderPolicyContext ValidContext()
    {
        var regularSessionUtc = new DateTime(2026, 8, 18, 15, 0, 0, DateTimeKind.Utc);
        return new LiveOrderPolicyContext(
            AssetType.Stock,
            "NASDAQ",
            ReferencePrice: 50m,
            TotalEquity: 850m,
            AvailableCash: 850m,
            CurrentPositionValue: 0m,
            QuoteAsOfUtc: regularSessionUtc,
            AccountSnapshotAsOfUtc: regularSessionUtc,
            ProjectedSectorPercent: 5.9m,
            ProjectedPortfolioVolatilityPercent: 15m,
            ProjectedDailyVaR95Percent: 1.5m,
            EvaluationTimeUtc: regularSessionUtc);
    }

    private static UpdateLivePortfolioPolicyRequest ValidRequest() => new(
        AllowedAssetTypes: [AssetType.Stock, AssetType.Etf],
        AllowedExchanges: ["NASDAQ", "NYSE", "NYSEARCA"],
        AllowedOrderTypes: [OrderType.Limit],
        RegularMarketHoursOnly: true,
        FractionalSharesEnabled: false,
        MinimumCashReservePercent: 20m,
        MaxOrderNotionalPercent: 10m,
        MaxOrderNotionalAmount: 100m,
        MaxDailyTurnoverPercent: 10m,
        MaxDailyLossPercent: 2m,
        MaxPositionPercent: 20m,
        MaxSectorPercent: 40m,
        MaxAnnualizedVolatilityPercent: 35m,
        MaxDailyVaR95Percent: 3m,
        MaxDrawdownPercent: 10m,
        MaxQuoteAgeSeconds: 60,
        MaxAccountSnapshotAgeSeconds: 60,
        ApprovalExpiryMinutes: 5,
        MaxPriceDriftPercent: 1m,
        MaxPositionDriftPercent: 1m,
        OrderTimeoutSeconds: 120,
        CancelReplaceEnabled: false,
        MaxCancelReplaceAttempts: 0);
}
