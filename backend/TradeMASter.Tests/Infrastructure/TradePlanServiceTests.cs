using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;
using TradeMASter.Infrastructure.Policies;
using TradeMASter.Infrastructure.Trading;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class TradePlanServiceTests
{
    [Fact]
    public async Task Creation_PersistsOneDeterministicPlanPerMarketRun()
    {
        await using var fixture = Fixture();
        var run = Run(fixture.Portfolio.Id);

        var first = await fixture.Service.CreateFromMarketRunAsync(run, fixture.Portfolio);
        var repeated = await fixture.Service.CreateFromMarketRunAsync(run, fixture.Portfolio);

        first.IsSuccess.Should().BeTrue(first.Error);
        repeated.IsSuccess.Should().BeTrue(repeated.Error);
        first.Value.Should().NotBeNull();
        repeated.Value!.Id.Should().Be(first.Value!.Id);
        repeated.Value.PlanHash.Should().Be(first.Value.PlanHash);
        repeated.Value.PlanHash.Should().HaveLength(64);
        repeated.Value.Payload.Account.AccountLastFour.Should().Be("7250");
        repeated.Value.Payload.Account.TotalEquity.Should().Be(850m);
        repeated.Value.Payload.Orders.Should().ContainSingle(item => item.Symbol == "F" && item.EstimatedNotional == 40m);
        (await fixture.Db.TradePlans.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Approval_IsExactAndIdempotentWithoutBrokerSubmission()
    {
        await using var fixture = Fixture();
        var created = (await fixture.Service.CreateFromMarketRunAsync(Run(fixture.Portfolio.Id), fixture.Portfolio)).Value!;

        var wrong = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            new string('b', 64), TradePlan.PrimaryApprovalConfirmation));
        var approved = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash, TradePlan.PrimaryApprovalConfirmation));
        var repeated = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash, string.Empty));

        wrong.IsFailure.Should().BeTrue();
        approved.IsSuccess.Should().BeTrue(approved.Error);
        repeated.IsSuccess.Should().BeTrue(repeated.Error);
        repeated.Value.Status.Should().Be(TradePlanStatus.Approved);
        repeated.Value.ApprovedAtUtc.Should().Be(approved.Value.ApprovedAtUtc);
        fixture.Robinhood.Verify(service => service.GetAccountStatusAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Robinhood.Verify(service => service.GetLiveHoldingsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Approval_InvalidatesWhenAccountEquityDrifts()
    {
        var statusCalls = 0;
        await using var fixture = Fixture(() => ++statusCalls == 1 ? Account(850m) : Account(700m));
        var created = (await fixture.Service.CreateFromMarketRunAsync(Run(fixture.Portfolio.Id), fixture.Portfolio)).Value!;

        var result = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash, TradePlan.PrimaryApprovalConfirmation));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalidated").And.Contain("equity drift");
        (await fixture.Service.GetAsync(created.Id)).Value.Status.Should().Be(TradePlanStatus.Invalidated);
    }

    [Fact]
    public async Task Approval_InvalidatesWhenPolicyChanges()
    {
        await using var fixture = Fixture();
        var created = (await fixture.Service.CreateFromMarketRunAsync(Run(fixture.Portfolio.Id), fixture.Portfolio)).Value!;
        (await fixture.Policy.ActivateEmergencyHaltAsync("Operator paused plan approvals for reconciliation")).IsSuccess.Should().BeTrue();

        var result = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash, TradePlan.PrimaryApprovalConfirmation));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("policy changed");
        (await fixture.Service.GetAsync(created.Id)).Value.Status.Should().Be(TradePlanStatus.Invalidated);
    }

    [Theory]
    [InlineData("cash", "cash drift")]
    [InlineData("price", "price drift")]
    [InlineData("quantity", "quantity drift")]
    public async Task Approval_InvalidatesMaterialAccountSnapshotDrift(string driftMode, string expectedReason)
    {
        var statusCalls = 0;
        var holdingCalls = 0;
        RobinhoodAccountInfo Status() => driftMode == "cash" && ++statusCalls > 1
            ? Account(850m, 700m)
            : Account(850m, 810m);
        IReadOnlyList<RobinhoodHoldingItem> Holdings()
        {
            var changed = ++holdingCalls > 1;
            var quantity = driftMode == "quantity" && changed ? 3m : 2m;
            var price = driftMode == "price" && changed ? 25m : 20m;
            return [new RobinhoodHoldingItem("F", "Ford Motor Company", quantity, 18m, price,
                quantity * price, quantity * (price - 18m), 0m, 4.71m)];
        }
        await using var fixture = Fixture(Status, holdings: Holdings);
        var created = (await fixture.Service.CreateFromMarketRunAsync(Run(fixture.Portfolio.Id), fixture.Portfolio)).Value!;

        var result = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash, TradePlan.PrimaryApprovalConfirmation));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain(expectedReason);
        (await fixture.Service.GetAsync(created.Id)).Value.Status.Should().Be(TradePlanStatus.Invalidated);
    }

    [Fact]
    public async Task Read_InvalidatesAPlanWhosePersistedPayloadWasTamperedWith()
    {
        await using var fixture = Fixture();
        var created = (await fixture.Service.CreateFromMarketRunAsync(Run(fixture.Portfolio.Id), fixture.Portfolio)).Value!;
        var entity = await fixture.Db.TradePlans.SingleAsync(item => item.Id == created.Id);
        fixture.Db.Entry(entity).Property(item => item.PayloadJson).CurrentValue = "{\"tampered\":true}";
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.GetAsync(created.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("integrity check failed");
        entity.Status.Should().Be(TradePlanStatus.Invalidated);
    }

    [Fact]
    public async Task MaterialNotional_RequiresSecondConfirmation()
    {
        await using var fixture = Fixture(secondaryThreshold: 30m);
        var created = (await fixture.Service.CreateFromMarketRunAsync(Run(fixture.Portfolio.Id), fixture.Portfolio)).Value!;

        created.RequiresSecondaryConfirmation.Should().BeTrue();
        var missing = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash, TradePlan.PrimaryApprovalConfirmation));
        var approved = await fixture.Service.ApproveAsync(created.Id, new ApproveTradePlanRequest(
            created.PlanHash,
            TradePlan.PrimaryApprovalConfirmation,
            TradePlan.SecondaryApprovalConfirmation));

        missing.IsFailure.Should().BeTrue();
        approved.IsSuccess.Should().BeTrue(approved.Error);
    }

    private static TradePlanFixture Fixture(
        Func<RobinhoodAccountInfo>? account = null,
        decimal secondaryThreshold = 100m,
        Func<IReadOnlyList<RobinhoodHoldingItem>>? holdings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Robinhood:LiveTradingEnabled"] = "false",
            ["TradePlans:SecondaryConfirmationNotionalAmount"] = secondaryThreshold.ToString()
        }).Build();
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var policy = new LivePortfolioPolicyService(db, configuration);
        var robinhood = new Mock<IRobinhoodService>(MockBehavior.Strict);
        robinhood.Setup(service => service.GetAccountStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Success((account ?? (() => Account(850m)))()));
        robinhood.Setup(service => service.GetLiveHoldingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Success(holdings?.Invoke() ?? (IReadOnlyList<RobinhoodHoldingItem>)[]));
        var portfolio = new Portfolio("Agentic account", 850m);
        return new TradePlanFixture(
            db,
            policy,
            robinhood,
            new TradePlanService(db, policy, robinhood.Object, configuration),
            portfolio);
    }

    private static RobinhoodAccountInfo Account(decimal equity, decimal? cash = null) => new(
        "837197250", "cash", equity, cash ?? equity, cash ?? equity, true, DateTime.UtcNow, "Connected");

    private static MarketIntelligenceRun Run(Guid portfolioId)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        return new MarketIntelligenceRun(
            Guid.NewGuid(),
            false,
            started,
            DateTime.UtcNow,
            4_000,
            300,
            new MacroRegimeAssessment("Risk-On", 80m, 20m, 15m, 4m, "Stable growth.", []),
            [new MarketCandidateAssessment(
                "F", "Ford Motor Company", "Consumer Cyclical", 20m, 50_000_000_000m, 10_000_000m,
                80m, 75m, 70m, 65m, 72m, 30m, 18m, SignalDirection.Bullish, true,
                "Passed deterministic gates.", [], true, "SEC verified", ["https://www.sec.gov/edgar/browse/?CIK=37996"])],
            [new TargetAllocation("F", "Consumer Cyclical", 4.71m, 40m, 0m, 4.71m, 2m, 18m)],
            95.29m,
            4.71m,
            12m,
            1m,
            true,
            "All phase-one risk constraints passed.",
            [new OrderRequest(portfolioId, "F", OrderSide.Buy, OrderType.Limit, 2m, 20m)],
            "No historical outcomes yet.",
            new PortfolioPerformanceSnapshot(0, null, 0m, 0m, 0m),
            "NASDAQ universe; SEC company facts; market data snapshot.");
    }

    private sealed class TradePlanFixture(
        TradeMASterDbContext db,
        LivePortfolioPolicyService policy,
        Mock<IRobinhoodService> robinhood,
        TradePlanService service,
        Portfolio portfolio) : IAsyncDisposable
    {
        public TradeMASterDbContext Db { get; } = db;
        public LivePortfolioPolicyService Policy { get; } = policy;
        public Mock<IRobinhoodService> Robinhood { get; } = robinhood;
        public TradePlanService Service { get; } = service;
        public Portfolio Portfolio { get; } = portfolio;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
