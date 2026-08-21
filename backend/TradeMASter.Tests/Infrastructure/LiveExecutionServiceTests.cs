using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;
using TradeMASter.Infrastructure.Policies;
using TradeMASter.Infrastructure.Trading;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class LiveExecutionServiceTests
{
    [Fact]
    public async Task DisabledAuthority_PersistsPreflightAndOutboxWithoutCallingBrokerPlace()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: false);

        var result = await fixture.Service.ExecuteApprovedPlanAsync(
            fixture.Plan.Id,
            Request(fixture.Plan));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Status.Should().Be(LiveExecutionBatchStatus.SubmissionBlocked);
        result.Value.Attempts.Should().ContainSingle();
        result.Value.Attempts[0].Status.Should().Be(LiveExecutionAttemptStatus.Pending);
        fixture.Adapter.Reviewed.Should().ContainSingle();
        fixture.Adapter.Placed.Should().BeEmpty();
        result.Value.Attempts[0].SanitizedRequestJson.Should().NotContain("837197250");
        (await fixture.Db.LiveExecutionBatches.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RepeatedExecution_IsIdempotentAndNeverPlacesTwice()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);

        var first = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));
        var repeated = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        first.IsSuccess.Should().BeTrue(first.Error);
        repeated.IsSuccess.Should().BeTrue(repeated.Error);
        first.Value.Id.Should().Be(repeated.Value.Id);
        repeated.Value.Status.Should().Be(LiveExecutionBatchStatus.Submitted);
        fixture.Adapter.Placed.Should().ContainSingle();
        repeated.Value.Attempts[0].AttemptCount.Should().Be(1);
        repeated.Value.Attempts[0].ClientOrderId.Should().NotBeEmpty();
        repeated.Value.Attempts[0].BrokerOrderId.Should().Be("broker-order-1");
        var inbox = await fixture.Db.LiveExecutionBrokerInbox.SingleAsync();
        inbox.AttemptId.Should().Be(repeated.Value.Attempts[0].Id);
        inbox.ClientOrderId.Should().Be(repeated.Value.Attempts[0].ClientOrderId);
        inbox.BrokerOrderId.Should().Be("broker-order-1");
        inbox.SanitizedPayloadJson.Should().NotContain("837197250");
    }

    [Fact]
    public async Task AmbiguousBrokerResult_RequiresReconciliationAndIsNeverRetried()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);
        fixture.Adapter.Submissions.Enqueue(new BrokerOrderSubmission(
            BrokerSubmissionOutcome.Unknown,
            null,
            "unknown",
            "Acceptance could not be proven.",
            "{\"outcome\":\"unknown\"}"));

        var first = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));
        var repeated = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        first.Value.Status.Should().Be(LiveExecutionBatchStatus.ReconciliationRequired);
        first.Value.Attempts[0].Status.Should().Be(LiveExecutionAttemptStatus.ReconciliationRequired);
        repeated.Value.Status.Should().Be(LiveExecutionBatchStatus.ReconciliationRequired);
        fixture.Adapter.Placed.Should().ContainSingle();
    }

    [Fact]
    public async Task Commands_AreDurablySequencedSellsBeforeBuys()
    {
        var holdings = new[]
        {
            Holding("F", 2m, 20m),
            Holding("GM", 1m, 20m)
        };
        var orders = new[]
        {
            new TradePlanOrderSnapshot("GM", OrderSide.Buy, OrderType.Limit, 2m, 20m, null, 40m, false),
            new TradePlanOrderSnapshot("F", OrderSide.Sell, OrderType.Limit, 2m, 20m, null, 40m, true)
        };
        await using var fixture = await FixtureAsync(true, holdings, orders);

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsSuccess.Should().BeTrue(result.Error);
        fixture.Adapter.Placed.Select(item => item.Side).Should().Equal(OrderSide.Sell, OrderSide.Buy);
        result.Value.Attempts.Select(item => item.Side).Should().Equal(OrderSide.Sell, OrderSide.Buy);
    }

    [Fact]
    public async Task PriceDrift_InvalidatesPlanBeforeReviewOrSubmission()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);
        fixture.Adapter.SnapshotFactory = () => Snapshot([], quotePrice: 25m);

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("price drift");
        fixture.Plan.Status.Should().Be(TradePlanStatus.Invalidated);
        fixture.Adapter.Reviewed.Should().BeEmpty();
        fixture.Adapter.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingOpenBrokerOrder_InvalidatesPlanBeforeReviewOrSubmission()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);
        fixture.Adapter.SnapshotFactory = () => Snapshot(
            [],
            openOrders:
            [new BrokerOpenOrderSnapshot("broker-existing", "F", OrderSide.Buy, 1m, 20m, "open")]);

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Open Robinhood equity orders exist");
        fixture.Plan.Status.Should().Be(TradePlanStatus.Invalidated);
        fixture.Adapter.Reviewed.Should().BeEmpty();
        fixture.Adapter.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task StaleQuote_InvalidatesPlanBeforeReviewOrSubmission()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);
        fixture.Adapter.SnapshotFactory = () => Snapshot([], quoteAsOfUtc: DateTime.UtcNow.AddMinutes(-2));

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("quote is stale");
        fixture.Plan.Status.Should().Be(TradePlanStatus.Invalidated);
        fixture.Adapter.Reviewed.Should().BeEmpty();
        fixture.Adapter.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task AggregateBuyingPowerReservation_FailsWithoutCountingSellProceeds()
    {
        var orders = new[]
        {
            new TradePlanOrderSnapshot("F", OrderSide.Buy, OrderType.Limit, 2m, 20m, null, 40m, false),
            new TradePlanOrderSnapshot("GM", OrderSide.Buy, OrderType.Limit, 2m, 20m, null, 40m, false)
        };
        await using var fixture = await FixtureAsync(true, [], orders, cash: 230m);
        fixture.Adapter.SnapshotFactory = () => Snapshot([], cash: 230m);

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("cash below the 20.0% reserve");
        fixture.Adapter.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task BuyingPowerDropImmediatelyBeforePlace_StopsWholeRemainingBuyBatch()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);
        var snapshotNumber = 0;
        fixture.Adapter.SnapshotFactory = () => ++snapshotNumber == 1
            ? Snapshot([])
            : Snapshot([], cash: 200m);

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Status.Should().Be(LiveExecutionBatchStatus.ReconciliationRequired);
        result.Value.Attempts.Should().ContainSingle(item =>
            item.Status == LiveExecutionAttemptStatus.ReconciliationRequired
            && item.FailureReason!.Contains("buying power"));
        fixture.Adapter.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerRejection_StopsRemainingOrders()
    {
        var holdings = new[] { Holding("F", 2m, 20m), Holding("GM", 1m, 20m) };
        var orders = new[]
        {
            new TradePlanOrderSnapshot("F", OrderSide.Sell, OrderType.Limit, 2m, 20m, null, 40m, true),
            new TradePlanOrderSnapshot("GM", OrderSide.Buy, OrderType.Limit, 2m, 20m, null, 40m, false)
        };
        await using var fixture = await FixtureAsync(true, holdings, orders);
        fixture.Adapter.Submissions.Enqueue(new BrokerOrderSubmission(
            BrokerSubmissionOutcome.Rejected, null, "rejected", "Broker rejected order.", "{\"outcome\":\"rejected\"}"));

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.Value.Status.Should().Be(LiveExecutionBatchStatus.Failed);
        fixture.Adapter.Placed.Should().ContainSingle();
        result.Value.Attempts[0].Status.Should().Be(LiveExecutionAttemptStatus.BrokerRejected);
        result.Value.Attempts[1].Status.Should().Be(LiveExecutionAttemptStatus.Skipped);
    }

    [Fact]
    public async Task ConcurrentRetries_AtomicallyClaimOneOutboxAttempt()
    {
        var databaseName = $"execution-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Default Timeout=30";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<TradeMASterDbContext>().UseSqlite(connectionString).Options;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Robinhood:LiveTradingEnabled"] = "false"
        }).Build();
        TradePlan plan;
        await using (var setupDb = new TradeMASterDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            var setupPolicy = new LivePortfolioPolicyService(setupDb, configuration);
            var policy = await setupPolicy.GetAsync();
            plan = ApprovedPlan(policy.PolicyVersion, [],
                [new TradePlanOrderSnapshot("F", OrderSide.Buy, OrderType.Limit, 2m, 20m, null, 40m, false)], 850m);
            setupDb.TradePlans.Add(plan);
            var batch = new LiveExecutionBatch(
                plan.Id, plan.PlanHash, "7250", DateTime.UtcNow, "{\"sanitized\":true}",
                40m, 40m, 0m, true, null);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("concurrent-attempt"))).ToLowerInvariant();
            batch.Attempts.Add(new LiveExecutionOrderAttempt(
                batch.Id, 0, Guid.NewGuid(), key, "F", OrderSide.Buy, OrderType.Limit,
                2m, 20m, 40m, "{\"symbol\":\"F\"}", "{\"approved\":true}"));
            setupDb.LiveExecutionBatches.Add(batch);
            await setupDb.SaveChangesAsync();
        }

        var adapter = new FakeExecutionAdapter { SnapshotFactory = () => Snapshot([]) };
        await using var db1 = new TradeMASterDbContext(options);
        await using var db2 = new TradeMASterDbContext(options);
        var service1 = new LiveExecutionService(db1, adapter, new LivePortfolioPolicyService(db1, configuration),
            new FakeAuthority(true), new AlwaysOpenCalendar());
        var service2 = new LiveExecutionService(db2, adapter, new LivePortfolioPolicyService(db2, configuration),
            new FakeAuthority(true), new AlwaysOpenCalendar());

        await Task.WhenAll(
            service1.ExecuteApprovedPlanAsync(plan.Id, Request(plan)),
            service2.ExecuteApprovedPlanAsync(plan.Id, Request(plan)));

        adapter.Placed.Should().ContainSingle();
        await using var verifyDb = new TradeMASterDbContext(options);
        var stored = await verifyDb.LiveExecutionBatches.Include(item => item.Attempts).SingleAsync();
        stored.Status.Should().Be(LiveExecutionBatchStatus.Submitted);
        stored.Attempts.Should().ContainSingle(item =>
            item.Status == LiveExecutionAttemptStatus.BrokerAccepted && item.AttemptCount == 1);
        (await verifyDb.LiveExecutionBrokerInbox.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RestartWithStaleSubmittingAttempt_RequiresReconciliationWithoutRetry()
    {
        await using var fixture = await FixtureAsync(authorityEnabled: true);
        var batch = new LiveExecutionBatch(
            fixture.Plan.Id,
            fixture.Plan.PlanHash,
            "7250",
            DateTime.UtcNow.AddMinutes(-4),
            "{\"sanitized\":true}",
            40m,
            40m,
            0m,
            true,
            null);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("stale-attempt"))).ToLowerInvariant();
        var attempt = new LiveExecutionOrderAttempt(
            batch.Id,
            0,
            Guid.NewGuid(),
            key,
            "F",
            OrderSide.Buy,
            OrderType.Limit,
            2m,
            20m,
            40m,
            "{\"symbol\":\"F\"}",
            "{\"approved\":true}");
        var staleAt = DateTime.UtcNow.AddMinutes(-3);
        batch.Attempts.Add(attempt);
        batch.MarkSubmitting(staleAt);
        attempt.MarkSubmitting(staleAt);
        fixture.Db.LiveExecutionBatches.Add(batch);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ExecuteApprovedPlanAsync(fixture.Plan.Id, Request(fixture.Plan));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Status.Should().Be(LiveExecutionBatchStatus.ReconciliationRequired);
        result.Value.Attempts.Should().ContainSingle(item =>
            item.Status == LiveExecutionAttemptStatus.ReconciliationRequired && item.AttemptCount == 1);
        fixture.Adapter.Placed.Should().BeEmpty();
        (await fixture.Db.LiveExecutionBrokerInbox.CountAsync()).Should().Be(0);
    }

    private static async Task<ExecutionFixture> FixtureAsync(
        bool authorityEnabled,
        IReadOnlyList<TradePlanHoldingSnapshot>? holdings = null,
        IReadOnlyList<TradePlanOrderSnapshot>? orders = null,
        decimal cash = 850m)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new TradeMASterDbContext(new DbContextOptionsBuilder<TradeMASterDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Robinhood:LiveTradingEnabled"] = "false"
        }).Build();
        var policy = new LivePortfolioPolicyService(db, configuration);
        var snapshot = await policy.GetAsync();
        var normalizedHoldings = holdings ?? [];
        var normalizedOrders = orders ??
        [new TradePlanOrderSnapshot("F", OrderSide.Buy, OrderType.Limit, 2m, 20m, null, 40m, false)];
        var plan = ApprovedPlan(snapshot.PolicyVersion, normalizedHoldings, normalizedOrders, cash);
        db.TradePlans.Add(plan);
        await db.SaveChangesAsync();
        var adapter = new FakeExecutionAdapter
        {
            SnapshotFactory = () => Snapshot(normalizedHoldings, cash)
        };
        var authority = new FakeAuthority(authorityEnabled);
        var service = new LiveExecutionService(db, adapter, policy, authority, new AlwaysOpenCalendar());
        return new ExecutionFixture(connection, db, service, adapter, plan);
    }

    private static TradePlan ApprovedPlan(
        int policyVersion,
        IReadOnlyList<TradePlanHoldingSnapshot> holdings,
        IReadOnlyList<TradePlanOrderSnapshot> orders,
        decimal cash)
    {
        var runId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var allocations = orders.Select(item => new TargetAllocation(
            item.Symbol, "Consumer Cyclical", 4.71m, item.EstimatedNotional, 0m, 4.71m,
            item.Quantity, 18m)).ToList();
        var payload = new ImmutableTradePlanPayload(
            runId,
            portfolioId,
            now,
            now.AddMinutes(5),
            policyVersion,
            new TradePlanAccountSnapshot("7250", now, 850m, cash, cash, holdings),
            new MacroRegimeAssessment("Risk-On", 80m, 20m, 15m, 4m, "Stable growth.", []),
            allocations,
            orders,
            new TradePlanRiskSnapshot(true, "Approved.", 5m, 12m, 1m, 20m, 2m),
            "No prior outcomes.",
            "Verified test sources.",
            new Dictionary<string, IReadOnlyList<string>>());
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var plan = new TradePlan(runId, portfolioId, hash, json, now.AddMinutes(5), policyVersion, false, []);
        plan.Approve(hash, TradePlan.PrimaryApprovalConfirmation, null, now).IsSuccess.Should().BeTrue();
        return plan;
    }

    private static ExecuteApprovedTradePlanRequest Request(TradePlan plan) =>
        new(plan.PlanHash, TradePlan.LiveSubmissionConfirmation);

    private static TradePlanHoldingSnapshot Holding(string symbol, decimal quantity, decimal price) =>
        new(symbol, quantity, price, quantity * price, 4.71m);

    private static BrokerExecutionSnapshot Snapshot(
        IReadOnlyList<TradePlanHoldingSnapshot> planHoldings,
        decimal cash = 850m,
        decimal quotePrice = 20m,
        IReadOnlyList<BrokerOpenOrderSnapshot>? openOrders = null,
        DateTime? quoteAsOfUtc = null)
    {
        var holdings = planHoldings.Select(item => new RobinhoodHoldingItem(
            item.Symbol, item.Symbol, item.Quantity, item.CurrentPrice, item.CurrentPrice,
            item.CurrentMarketValue, 0m, 0m, item.PortfolioWeightPercent)).ToList();
        var symbols = holdings.Select(item => item.Symbol).Append("F").Append("GM").Distinct().ToList();
        return new BrokerExecutionSnapshot(
            "837197250", "cash", 850m, cash, cash, DateTime.UtcNow,
            holdings,
            openOrders ?? [],
            symbols.Select(symbol => new BrokerQuoteSnapshot(symbol, quotePrice, quotePrice - 0.01m, quotePrice + 0.01m,
                quoteAsOfUtc ?? DateTime.UtcNow, "Robinhood MCP")).ToList(),
            symbols.Select(symbol => new BrokerInstrumentEligibility(symbol, true, false, AssetType.Stock, "NASDAQ", "Robinhood MCP")).ToList(),
            0m);
    }

    private sealed class FakeExecutionAdapter : IRobinhoodLiveExecutionAdapter
    {
        public Func<BrokerExecutionSnapshot> SnapshotFactory { get; set; } = () => Snapshot([]);
        public List<BrokerOrderCommand> Reviewed { get; } = [];
        public List<BrokerOrderCommand> Placed { get; } = [];
        public Queue<BrokerOrderSubmission> Submissions { get; } = new();

        public Task<Result<BrokerExecutionSnapshot>> GetFreshPreflightSnapshotAsync(
            IReadOnlyList<string> symbols,
            CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(SnapshotFactory()));

        public Task<Result<BrokerOrderReview>> ReviewOrderAsync(
            BrokerOrderCommand command,
            CancellationToken cancellationToken = default)
        {
            Reviewed.Add(command);
            return Task.FromResult(Result.Success(new BrokerOrderReview(true, [], "{\"approved\":true}")));
        }

        public Task<BrokerOrderSubmission> PlaceOrderAsync(
            BrokerOrderCommand command,
            CancellationToken cancellationToken = default)
        {
            Placed.Add(command);
            return Task.FromResult(Submissions.Count > 0
                ? Submissions.Dequeue()
                : new BrokerOrderSubmission(BrokerSubmissionOutcome.Accepted, $"broker-order-{Placed.Count}", "open", "Accepted.",
                    $"{{\"brokerOrderId\":\"broker-order-{Placed.Count}\",\"state\":\"open\"}}"));
        }
    }

    private sealed class FakeAuthority(bool enabled) : ILiveExecutionAuthority
    {
        public Result Verify(LivePortfolioPolicySnapshot policy) => enabled
            ? Result.Success()
            : Result.Failure("Live submission disabled for test.");
    }

    private sealed class AlwaysOpenCalendar : IUsMarketCalendar
    {
        public bool IsRegularSession(DateTime utcNow) => true;
        public string DescribeClosure(DateTime utcNow) => "Open";
    }

    private sealed class ExecutionFixture(
        SqliteConnection connection,
        TradeMASterDbContext db,
        LiveExecutionService service,
        FakeExecutionAdapter adapter,
        TradePlan plan) : IAsyncDisposable
    {
        public TradeMASterDbContext Db { get; } = db;
        public LiveExecutionService Service { get; } = service;
        public FakeExecutionAdapter Adapter { get; } = adapter;
        public TradePlan Plan { get; } = plan;
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
