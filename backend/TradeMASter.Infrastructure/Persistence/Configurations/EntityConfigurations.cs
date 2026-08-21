using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeMASter.Core.Entities;

namespace TradeMASter.Infrastructure.Persistence.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.Symbol).IsUnique();
        builder.Property(a => a.Symbol).IsRequired().HasMaxLength(20);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Exchange).HasMaxLength(50);
        builder.Property(a => a.Currency).HasMaxLength(10);
        builder.Property(a => a.LastPrice).HasPrecision(18, 4);
        builder.Property(a => a.PreviousClose).HasPrecision(18, 4);
        builder.Property(a => a.Change24h).HasPrecision(18, 4);
        builder.Property(a => a.ChangePercent24h).HasPrecision(18, 4);
        builder.Property(a => a.Volume24h).HasPrecision(24, 4);
    }
}

public class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    public void Configure(EntityTypeBuilder<Portfolio> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.CashBalance).HasPrecision(18, 4);
        builder.Property(p => p.InitialBalance).HasPrecision(18, 4);

        builder.OwnsOne(p => p.RiskConfig, rc =>
        {
            rc.Property(r => r.MaxPositionSizePercent).HasColumnName("Risk_MaxPositionSizePercent").HasPrecision(18, 4);
            rc.Property(r => r.MaxPortfolioDrawdownPercent).HasColumnName("Risk_MaxPortfolioDrawdownPercent").HasPrecision(18, 4);
            rc.Property(r => r.DefaultStopLossPercent).HasColumnName("Risk_DefaultStopLossPercent").HasPrecision(18, 4);
            rc.Property(r => r.DefaultTakeProfitPercent).HasColumnName("Risk_DefaultTakeProfitPercent").HasPrecision(18, 4);
            rc.Property(r => r.RequireHumanApprovalForLive).HasColumnName("Risk_RequireHumanApprovalForLive");
            rc.Property(r => r.MaxDailyLossAmount).HasColumnName("Risk_MaxDailyLossAmount").HasPrecision(18, 4);
        });

        builder.HasMany(p => p.Positions)
            .WithOne()
            .HasForeignKey(pos => pos.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Orders)
            .WithOne()
            .HasForeignKey(o => o.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.PortfolioId, p.Symbol }).IsUnique();
        builder.Property(p => p.Symbol).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Quantity).HasPrecision(18, 6);
        builder.Property(p => p.AverageEntryPrice).HasPrecision(18, 4);
        builder.Property(p => p.CurrentPrice).HasPrecision(18, 4);
        builder.Property(p => p.UnrealizedPnL).HasPrecision(18, 4);
        builder.Property(p => p.UnrealizedPnLPercent).HasPrecision(18, 4);
        builder.Property(p => p.RealizedPnL).HasPrecision(18, 4);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => o.PortfolioId);
        builder.HasIndex(o => o.Symbol);
        builder.Property(o => o.Symbol).IsRequired().HasMaxLength(20);
        builder.Property(o => o.Quantity).HasPrecision(18, 6);
        builder.Property(o => o.LimitPrice).HasPrecision(18, 4);
        builder.Property(o => o.StopPrice).HasPrecision(18, 4);
        builder.Property(o => o.FilledPrice).HasPrecision(18, 4);
        builder.Property(o => o.FilledQuantity).HasPrecision(18, 6);
        builder.Property(o => o.RejectionReason).HasMaxLength(500);
    }
}

public class DeliberationSessionConfiguration : IEntityTypeConfiguration<DeliberationSession>
{
    public void Configure(EntityTypeBuilder<DeliberationSession> builder)
    {
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.Symbol);
        builder.Property(d => d.Symbol).IsRequired().HasMaxLength(20);
        builder.Property(d => d.FinalConsensusSummary).HasMaxLength(2000);
        builder.Property(d => d.RiskNotes).HasMaxLength(1000);

        builder.HasMany(d => d.Decisions)
            .WithOne()
            .HasForeignKey(dec => dec.DeliberationSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AgentDecisionConfiguration : IEntityTypeConfiguration<AgentDecision>
{
    public void Configure(EntityTypeBuilder<AgentDecision> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.DeliberationSessionId);
        builder.Property(a => a.Symbol).IsRequired().HasMaxLength(20);
        builder.Property(a => a.Reasoning).HasMaxLength(2000);
        builder.Property(a => a.KeyFactorsJson).HasMaxLength(4000);
    }
}

public class LivePortfolioPolicyConfiguration : IEntityTypeConfiguration<LivePortfolioPolicy>
{
    public void Configure(EntityTypeBuilder<LivePortfolioPolicy> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AllowedAssetTypesCsv).IsRequired().HasMaxLength(100);
        builder.Property(item => item.AllowedExchangesCsv).IsRequired().HasMaxLength(500);
        builder.Property(item => item.AllowedOrderTypesCsv).IsRequired().HasMaxLength(100);
        builder.Property(item => item.EmergencyHaltReason).HasMaxLength(500);
        builder.Property(item => item.MinimumCashReservePercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxOrderNotionalPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxOrderNotionalAmount).HasPrecision(18, 4);
        builder.Property(item => item.MaxDailyTurnoverPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxDailyLossPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxPositionPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxSectorPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxAnnualizedVolatilityPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxDailyVaR95Percent).HasPrecision(18, 4);
        builder.Property(item => item.MaxDrawdownPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxPriceDriftPercent).HasPrecision(18, 4);
        builder.Property(item => item.MaxPositionDriftPercent).HasPrecision(18, 4);
        builder.Ignore(item => item.AllowedAssetTypes);
        builder.Ignore(item => item.AllowedExchanges);
        builder.Ignore(item => item.AllowedOrderTypes);
    }
}

public class TradePlanConfiguration : IEntityTypeConfiguration<TradePlan>
{
    public void Configure(EntityTypeBuilder<TradePlan> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.SourceRunId).IsUnique();
        builder.HasIndex(item => new { item.Status, item.CreatedAt });
        builder.Property(item => item.PlanHash).IsRequired().HasMaxLength(64);
        builder.Property(item => item.PayloadJson).IsRequired();
        builder.Property(item => item.SecondaryConfirmationReasons).HasMaxLength(1000);
        builder.Property(item => item.DecisionReason).HasMaxLength(500);
    }
}

public class LiveExecutionBatchConfiguration : IEntityTypeConfiguration<LiveExecutionBatch>
{
    public void Configure(EntityTypeBuilder<LiveExecutionBatch> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.TradePlanId).IsUnique();
        builder.HasIndex(item => new { item.Status, item.CreatedAt });
        builder.Property(item => item.PlanHash).IsRequired().HasMaxLength(64);
        builder.Property(item => item.AccountLastFour).IsRequired().HasMaxLength(16);
        builder.Property(item => item.PreflightSnapshotJson).IsRequired();
        builder.Property(item => item.ReservedBuyingPower).HasPrecision(18, 4);
        builder.Property(item => item.TotalBuyNotional).HasPrecision(18, 4);
        builder.Property(item => item.TotalSellNotional).HasPrecision(18, 4);
        builder.Property(item => item.StatusReason).HasMaxLength(1000);
        builder.HasMany(item => item.Attempts).WithOne().HasForeignKey(item => item.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.ReconciliationState).WithOne().HasForeignKey<LiveExecutionReconciliationState>(item => item.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LiveExecutionOrderAttemptConfiguration : IEntityTypeConfiguration<LiveExecutionOrderAttempt>
{
    public void Configure(EntityTypeBuilder<LiveExecutionOrderAttempt> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.ClientOrderId).IsUnique();
        builder.HasIndex(item => item.IdempotencyKey).IsUnique();
        builder.HasIndex(item => new { item.BatchId, item.Sequence }).IsUnique();
        builder.HasIndex(item => item.BrokerOrderId);
        builder.Property(item => item.IdempotencyKey).IsRequired().HasMaxLength(64);
        builder.Property(item => item.Symbol).IsRequired().HasMaxLength(20);
        builder.Property(item => item.Quantity).HasPrecision(18, 6);
        builder.Property(item => item.LimitPrice).HasPrecision(18, 4);
        builder.Property(item => item.EstimatedNotional).HasPrecision(18, 4);
        builder.Property(item => item.SanitizedRequestJson).IsRequired();
        builder.Property(item => item.SanitizedReviewJson);
        builder.Property(item => item.SanitizedResponseJson);
        builder.Property(item => item.BrokerOrderId).HasMaxLength(200);
        builder.Property(item => item.FailureReason).HasMaxLength(1000);
        builder.HasMany(item => item.Events).WithOne().HasForeignKey(item => item.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LiveExecutionOrderEventConfiguration : IEntityTypeConfiguration<LiveExecutionOrderEvent>
{
    public void Configure(EntityTypeBuilder<LiveExecutionOrderEvent> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.EventKey).IsUnique();
        builder.HasIndex(item => new { item.AttemptId, item.BrokerUpdatedAtUtc });
        builder.HasIndex(item => new { item.BatchId, item.ObservedAtUtc });
        builder.Property(item => item.EventKey).IsRequired().HasMaxLength(64);
        builder.Property(item => item.BrokerOrderId).IsRequired().HasMaxLength(200);
        builder.Property(item => item.BrokerState).IsRequired().HasMaxLength(100);
        builder.Property(item => item.OrderedQuantity).HasPrecision(18, 6);
        builder.Property(item => item.FilledQuantity).HasPrecision(18, 6);
        builder.Property(item => item.AverageFillPrice).HasPrecision(18, 4);
        builder.Property(item => item.SanitizedPayloadJson).IsRequired();
        builder.HasOne<LiveExecutionBatch>().WithMany().HasForeignKey(item => item.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LiveExecutionReconciliationStateConfiguration : IEntityTypeConfiguration<LiveExecutionReconciliationState>
{
    public void Configure(EntityTypeBuilder<LiveExecutionReconciliationState> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.BatchId).IsUnique();
        builder.Property(item => item.InterventionReason).HasMaxLength(1000);
        builder.HasOne<LiveExecutionBatch>().WithOne(item => item.ReconciliationState)
            .HasForeignKey<LiveExecutionReconciliationState>(item => item.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LiveExecutionBrokerInboxConfiguration : IEntityTypeConfiguration<LiveExecutionBrokerInbox>
{
    public void Configure(EntityTypeBuilder<LiveExecutionBrokerInbox> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.AttemptId).IsUnique();
        builder.HasIndex(item => item.ClientOrderId).IsUnique();
        builder.HasIndex(item => item.BrokerOrderId).IsUnique();
        builder.HasIndex(item => new { item.BatchId, item.ReceivedAtUtc });
        builder.Property(item => item.BrokerOrderId).IsRequired().HasMaxLength(200);
        builder.Property(item => item.BrokerState).IsRequired().HasMaxLength(100);
        builder.Property(item => item.SanitizedPayloadJson).IsRequired();
        builder.HasOne<LiveExecutionBatch>().WithMany().HasForeignKey(item => item.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LiveExecutionOrderAttempt>().WithOne().HasForeignKey<LiveExecutionBrokerInbox>(item => item.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}
