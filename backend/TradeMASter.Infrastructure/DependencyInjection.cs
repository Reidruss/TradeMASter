using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Brokers;
using TradeMASter.Infrastructure.Brokers.Robinhood;
using TradeMASter.Infrastructure.Cache;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence;
using TradeMASter.Infrastructure.Persistence.Repositories;
using TradeMASter.Infrastructure.Policies;
using TradeMASter.Infrastructure.Trading;

namespace TradeMASter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Database Configuration (PostgreSQL in deployment; persistent SQLite locally)
        var connectionString = configuration.GetConnectionString("TradeMASterDb");
        var usePostgreSql = configuration.GetValue<bool>("Database:UsePostgreSql") && !string.IsNullOrWhiteSpace(connectionString);

        if (usePostgreSql)
        {
            services.AddDbContext<TradeMASterDbContext>(options =>
                options.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(TradeMASterDbContext).Assembly.FullName)));
        }
        else
        {
            services.AddDbContext<TradeMASterDbContext>(options =>
                options.UseSqlite(configuration.GetConnectionString("TradeMASterSqlite")
                    ?? "Data Source=trademaster.db"));
        }

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TradeMASterDbContext>());

        // 2. Repositories
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        // 3. Cache & PubSub (Redis with InMemory fallback)
        var redisConnStr = configuration.GetConnectionString("Redis");
        var useRedis = configuration.GetValue<bool>("Cache:UseRedis") && !string.IsNullOrWhiteSpace(redisConnStr);

        if (useRedis)
        {
            try
            {
                var multiplexer = ConnectionMultiplexer.Connect(redisConnStr!);
                services.AddSingleton<IConnectionMultiplexer>(multiplexer);
                services.AddSingleton<ICacheService, RedisCacheService>();
            }
            catch
            {
                services.AddSingleton<ICacheService, InMemoryCacheService>();
            }
        }
        else
        {
            services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        // 4. Market Data Providers & HTTP Clients
        services.AddHttpClient<YahooFinanceMarketDataProvider>();
        services.AddHttpClient<IMarketUniverseProvider, NasdaqMarketUniverseProvider>(client =>
            client.Timeout = TimeSpan.FromSeconds(45));
        services.AddSingleton<SimulatedMarketDataProvider>();
        services.AddScoped<IMarketDataService, MarketDataService>();

        // 5. Broker Clients & Robinhood Integration
        services.AddScoped<IBrokerClient, PaperBrokerService>();
        services.AddHttpClient<RobinhoodMcpClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int?>("Robinhood:RequestTimeoutSeconds") ?? 30));
        services.AddHttpClient<RobinhoodBrokerService>(client =>
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int?>("Robinhood:RequestTimeoutSeconds") ?? 30));
        services.AddScoped<IRobinhoodService, RobinhoodBrokerService>();
        services.AddScoped<ILivePortfolioPolicyService, LivePortfolioPolicyService>();
        services.AddScoped<ITradePlanService, TradePlanService>();
        services.AddScoped<IRobinhoodLiveExecutionAdapter, RobinhoodLiveExecutionAdapter>();
        services.AddScoped<ILiveExecutionAuthority, LiveExecutionAuthority>();
        services.AddSingleton<IUsMarketCalendar, UsEquityMarketCalendar>();
        services.AddScoped<LiveExecutionService>();
        services.AddScoped<ILiveExecutionService>(sp => sp.GetRequiredService<LiveExecutionService>());
        services.AddScoped<ILiveExecutionReconciliationService>(sp => sp.GetRequiredService<LiveExecutionService>());
        services.AddHostedService<LiveExecutionReconciliationWorker>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradeMASterDbContext>();
        var logger = scope.ServiceProvider.GetService<ILogger<TradeMASterDbContext>>();

        if (db.Database.IsNpgsql())
        {
            try
            {
                var migrations = db.Database.GetMigrations();
                if (migrations.Any())
                    await db.Database.MigrateAsync();
                else
                    await db.Database.EnsureCreatedAsync();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to apply PostgreSQL migrations. Ensure database server is online.");
            }
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        await EnsureReadinessTablesAsync(db);

        if (!await db.LivePortfolioPolicies.AnyAsync())
        {
            await db.LivePortfolioPolicies.AddAsync(new LivePortfolioPolicy());
            await db.SaveChangesAsync();
        }

        // Seed initial tradable assets if empty
        if (!await db.Assets.AnyAsync())
        {
            var seedAssets = new List<Asset>
            {
                new("NVDA", "NVIDIA Corporation", AssetType.Stock, "NASDAQ", "USD", true, 132.50m),
                new("AAPL", "Apple Inc.", AssetType.Stock, "NASDAQ", "USD", true, 228.40m),
                new("MSFT", "Microsoft Corporation", AssetType.Stock, "NASDAQ", "USD", true, 445.80m),
                new("TSLA", "Tesla, Inc.", AssetType.Stock, "NASDAQ", "USD", true, 218.90m),
                new("AMZN", "Amazon.com, Inc.", AssetType.Stock, "NASDAQ", "USD", true, 186.20m),
                new("GOOGL", "Alphabet Inc.", AssetType.Stock, "NASDAQ", "USD", true, 178.60m),
                new("META", "Meta Platforms, Inc.", AssetType.Stock, "NASDAQ", "USD", true, 510.40m),
                new("BTC-USD", "Bitcoin USD", AssetType.Crypto, "COINBASE", "USD", true, 64250.00m),
                new("ETH-USD", "Ethereum USD", AssetType.Crypto, "COINBASE", "USD", true, 3480.00m),
                new("SOL-USD", "Solana USD", AssetType.Crypto, "COINBASE", "USD", true, 148.50m),
                new("SPY", "SPDR S&P 500 ETF Trust", AssetType.Etf, "NYSE", "USD", true, 552.30m),
                new("QQQ", "Invesco QQQ Trust", AssetType.Etf, "NASDAQ", "USD", true, 480.10m)
            };

            await db.Assets.AddRangeAsync(seedAssets);
            await db.SaveChangesAsync();
        }

        // Seed initial default portfolio if empty
        if (!await db.Portfolios.AnyAsync())
        {
            var defaultPortfolio = new Portfolio("Robinhood Agentic Portfolio", 24_500m);
            defaultPortfolio.Positions.Add(new Position(defaultPortfolio.Id, "NVDA", 45, 175.50m));
            defaultPortfolio.Positions.Add(new Position(defaultPortfolio.Id, "AAPL", 60, 215.20m));
            defaultPortfolio.Positions.Add(new Position(defaultPortfolio.Id, "MSFT", 30, 410.00m));
            defaultPortfolio.Positions.Add(new Position(defaultPortfolio.Id, "TSLA", 50, 210.00m));
            defaultPortfolio.Positions.Add(new Position(defaultPortfolio.Id, "BTC-USD", 0.12m, 62400.00m));

            await db.Portfolios.AddAsync(defaultPortfolio);
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureReadinessTablesAsync(TradeMASterDbContext db)
    {
        if (!db.Database.IsRelational()) return;
        var sql = db.Database.IsNpgsql()
            ? """
              CREATE TABLE IF NOT EXISTS "LivePortfolioPolicies" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "LiveTradingEnabled" boolean NOT NULL,
                  "AllowedAssetTypesCsv" character varying(100) NOT NULL,
                  "AllowedExchangesCsv" character varying(500) NOT NULL,
                  "AllowedOrderTypesCsv" character varying(100) NOT NULL,
                  "RegularMarketHoursOnly" boolean NOT NULL,
                  "FractionalSharesEnabled" boolean NOT NULL,
                  "MinimumCashReservePercent" numeric(18,4) NOT NULL,
                  "MaxOrderNotionalPercent" numeric(18,4) NOT NULL,
                  "MaxOrderNotionalAmount" numeric(18,4) NOT NULL,
                  "MaxDailyTurnoverPercent" numeric(18,4) NOT NULL,
                  "MaxDailyLossPercent" numeric(18,4) NOT NULL,
                  "MaxPositionPercent" numeric(18,4) NOT NULL,
                  "MaxSectorPercent" numeric(18,4) NOT NULL,
                  "MaxAnnualizedVolatilityPercent" numeric(18,4) NOT NULL,
                  "MaxDailyVaR95Percent" numeric(18,4) NOT NULL,
                  "MaxDrawdownPercent" numeric(18,4) NOT NULL,
                  "MaxQuoteAgeSeconds" integer NOT NULL,
                  "MaxAccountSnapshotAgeSeconds" integer NOT NULL,
                  "ApprovalExpiryMinutes" integer NOT NULL,
                  "MaxPriceDriftPercent" numeric(18,4) NOT NULL,
                  "MaxPositionDriftPercent" numeric(18,4) NOT NULL,
                  "OrderTimeoutSeconds" integer NOT NULL,
                  "CancelReplaceEnabled" boolean NOT NULL,
                  "MaxCancelReplaceAttempts" integer NOT NULL,
                  "EmergencyHaltActive" boolean NOT NULL,
                  "EmergencyHaltReason" character varying(500) NULL,
                  "EmergencyHaltedAtUtc" timestamp with time zone NULL,
                  "PolicyVersion" integer NOT NULL
              );
              """
            : """
              CREATE TABLE IF NOT EXISTS "LivePortfolioPolicies" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "LiveTradingEnabled" INTEGER NOT NULL,
                  "AllowedAssetTypesCsv" TEXT NOT NULL,
                  "AllowedExchangesCsv" TEXT NOT NULL,
                  "AllowedOrderTypesCsv" TEXT NOT NULL,
                  "RegularMarketHoursOnly" INTEGER NOT NULL,
                  "FractionalSharesEnabled" INTEGER NOT NULL,
                  "MinimumCashReservePercent" TEXT NOT NULL,
                  "MaxOrderNotionalPercent" TEXT NOT NULL,
                  "MaxOrderNotionalAmount" TEXT NOT NULL,
                  "MaxDailyTurnoverPercent" TEXT NOT NULL,
                  "MaxDailyLossPercent" TEXT NOT NULL,
                  "MaxPositionPercent" TEXT NOT NULL,
                  "MaxSectorPercent" TEXT NOT NULL,
                  "MaxAnnualizedVolatilityPercent" TEXT NOT NULL,
                  "MaxDailyVaR95Percent" TEXT NOT NULL,
                  "MaxDrawdownPercent" TEXT NOT NULL,
                  "MaxQuoteAgeSeconds" INTEGER NOT NULL,
                  "MaxAccountSnapshotAgeSeconds" INTEGER NOT NULL,
                  "ApprovalExpiryMinutes" INTEGER NOT NULL,
                  "MaxPriceDriftPercent" TEXT NOT NULL,
                  "MaxPositionDriftPercent" TEXT NOT NULL,
                  "OrderTimeoutSeconds" INTEGER NOT NULL,
                  "CancelReplaceEnabled" INTEGER NOT NULL,
                  "MaxCancelReplaceAttempts" INTEGER NOT NULL,
                  "EmergencyHaltActive" INTEGER NOT NULL,
                  "EmergencyHaltReason" TEXT NULL,
                  "EmergencyHaltedAtUtc" TEXT NULL,
                  "PolicyVersion" INTEGER NOT NULL
              );
              """;
        await db.Database.ExecuteSqlRawAsync(sql);

        var tradePlanSql = db.Database.IsNpgsql()
            ? """
              CREATE TABLE IF NOT EXISTS "TradePlans" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "SourceRunId" uuid NOT NULL,
                  "PortfolioId" uuid NOT NULL,
                  "Status" integer NOT NULL,
                  "PlanHash" character varying(64) NOT NULL,
                  "PayloadJson" text NOT NULL,
                  "ExpiresAtUtc" timestamp with time zone NOT NULL,
                  "PolicyVersion" integer NOT NULL,
                  "RequiresSecondaryConfirmation" boolean NOT NULL,
                  "SecondaryConfirmationReasons" character varying(1000) NOT NULL,
                  "ApprovedAtUtc" timestamp with time zone NULL,
                  "RejectedAtUtc" timestamp with time zone NULL,
                  "InvalidatedAtUtc" timestamp with time zone NULL,
                  "DecisionReason" character varying(500) NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_TradePlans_SourceRunId" ON "TradePlans" ("SourceRunId");
              CREATE INDEX IF NOT EXISTS "IX_TradePlans_Status_CreatedAt" ON "TradePlans" ("Status", "CreatedAt");
              """
            : """
              CREATE TABLE IF NOT EXISTS "TradePlans" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "SourceRunId" TEXT NOT NULL,
                  "PortfolioId" TEXT NOT NULL,
                  "Status" INTEGER NOT NULL,
                  "PlanHash" TEXT NOT NULL,
                  "PayloadJson" TEXT NOT NULL,
                  "ExpiresAtUtc" TEXT NOT NULL,
                  "PolicyVersion" INTEGER NOT NULL,
                  "RequiresSecondaryConfirmation" INTEGER NOT NULL,
                  "SecondaryConfirmationReasons" TEXT NOT NULL,
                  "ApprovedAtUtc" TEXT NULL,
                  "RejectedAtUtc" TEXT NULL,
                  "InvalidatedAtUtc" TEXT NULL,
                  "DecisionReason" TEXT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_TradePlans_SourceRunId" ON "TradePlans" ("SourceRunId");
              CREATE INDEX IF NOT EXISTS "IX_TradePlans_Status_CreatedAt" ON "TradePlans" ("Status", "CreatedAt");
              """;
        await db.Database.ExecuteSqlRawAsync(tradePlanSql);

        var executionSql = db.Database.IsNpgsql()
            ? """
              CREATE TABLE IF NOT EXISTS "LiveExecutionBatches" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "TradePlanId" uuid NOT NULL,
                  "PlanHash" character varying(64) NOT NULL,
                  "Status" integer NOT NULL,
                  "AccountLastFour" character varying(16) NOT NULL,
                  "PreflightAtUtc" timestamp with time zone NOT NULL,
                  "PreflightSnapshotJson" text NOT NULL,
                  "ReservedBuyingPower" numeric(18,4) NOT NULL,
                  "TotalBuyNotional" numeric(18,4) NOT NULL,
                  "TotalSellNotional" numeric(18,4) NOT NULL,
                  "StatusReason" character varying(1000) NULL,
                  "SubmittedAtUtc" timestamp with time zone NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBatches_TradePlanId" ON "LiveExecutionBatches" ("TradePlanId");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionBatches_Status_CreatedAt" ON "LiveExecutionBatches" ("Status", "CreatedAt");
              CREATE TABLE IF NOT EXISTS "LiveExecutionOrderAttempts" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "BatchId" uuid NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "Sequence" integer NOT NULL,
                  "ClientOrderId" uuid NOT NULL,
                  "IdempotencyKey" character varying(64) NOT NULL,
                  "Symbol" character varying(20) NOT NULL,
                  "Side" integer NOT NULL,
                  "Type" integer NOT NULL,
                  "Quantity" numeric(18,6) NOT NULL,
                  "LimitPrice" numeric(18,4) NOT NULL,
                  "EstimatedNotional" numeric(18,4) NOT NULL,
                  "Status" integer NOT NULL,
                  "SanitizedRequestJson" text NOT NULL,
                  "SanitizedReviewJson" text NULL,
                  "SanitizedResponseJson" text NULL,
                  "BrokerOrderId" character varying(200) NULL,
                  "AttemptCount" integer NOT NULL,
                  "LastAttemptAtUtc" timestamp with time zone NULL,
                  "FailureReason" character varying(1000) NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_ClientOrderId" ON "LiveExecutionOrderAttempts" ("ClientOrderId");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_IdempotencyKey" ON "LiveExecutionOrderAttempts" ("IdempotencyKey");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_BatchId_Sequence" ON "LiveExecutionOrderAttempts" ("BatchId", "Sequence");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_BrokerOrderId" ON "LiveExecutionOrderAttempts" ("BrokerOrderId");
              CREATE TABLE IF NOT EXISTS "LiveExecutionBrokerInbox" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "BatchId" uuid NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "AttemptId" uuid NOT NULL REFERENCES "LiveExecutionOrderAttempts" ("Id") ON DELETE CASCADE,
                  "ClientOrderId" uuid NOT NULL,
                  "BrokerOrderId" character varying(200) NOT NULL,
                  "BrokerState" character varying(100) NOT NULL,
                  "SanitizedPayloadJson" text NOT NULL,
                  "ReceivedAtUtc" timestamp with time zone NOT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_AttemptId" ON "LiveExecutionBrokerInbox" ("AttemptId");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_ClientOrderId" ON "LiveExecutionBrokerInbox" ("ClientOrderId");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_BrokerOrderId" ON "LiveExecutionBrokerInbox" ("BrokerOrderId");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_BatchId_ReceivedAtUtc" ON "LiveExecutionBrokerInbox" ("BatchId", "ReceivedAtUtc");
              """
            : """
              CREATE TABLE IF NOT EXISTS "LiveExecutionBatches" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "TradePlanId" TEXT NOT NULL,
                  "PlanHash" TEXT NOT NULL,
                  "Status" INTEGER NOT NULL,
                  "AccountLastFour" TEXT NOT NULL,
                  "PreflightAtUtc" TEXT NOT NULL,
                  "PreflightSnapshotJson" TEXT NOT NULL,
                  "ReservedBuyingPower" TEXT NOT NULL,
                  "TotalBuyNotional" TEXT NOT NULL,
                  "TotalSellNotional" TEXT NOT NULL,
                  "StatusReason" TEXT NULL,
                  "SubmittedAtUtc" TEXT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBatches_TradePlanId" ON "LiveExecutionBatches" ("TradePlanId");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionBatches_Status_CreatedAt" ON "LiveExecutionBatches" ("Status", "CreatedAt");
              CREATE TABLE IF NOT EXISTS "LiveExecutionOrderAttempts" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "BatchId" TEXT NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "Sequence" INTEGER NOT NULL,
                  "ClientOrderId" TEXT NOT NULL,
                  "IdempotencyKey" TEXT NOT NULL,
                  "Symbol" TEXT NOT NULL,
                  "Side" INTEGER NOT NULL,
                  "Type" INTEGER NOT NULL,
                  "Quantity" TEXT NOT NULL,
                  "LimitPrice" TEXT NOT NULL,
                  "EstimatedNotional" TEXT NOT NULL,
                  "Status" INTEGER NOT NULL,
                  "SanitizedRequestJson" TEXT NOT NULL,
                  "SanitizedReviewJson" TEXT NULL,
                  "SanitizedResponseJson" TEXT NULL,
                  "BrokerOrderId" TEXT NULL,
                  "AttemptCount" INTEGER NOT NULL,
                  "LastAttemptAtUtc" TEXT NULL,
                  "FailureReason" TEXT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_ClientOrderId" ON "LiveExecutionOrderAttempts" ("ClientOrderId");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_IdempotencyKey" ON "LiveExecutionOrderAttempts" ("IdempotencyKey");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_BatchId_Sequence" ON "LiveExecutionOrderAttempts" ("BatchId", "Sequence");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionOrderAttempts_BrokerOrderId" ON "LiveExecutionOrderAttempts" ("BrokerOrderId");
              CREATE TABLE IF NOT EXISTS "LiveExecutionBrokerInbox" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "BatchId" TEXT NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "AttemptId" TEXT NOT NULL REFERENCES "LiveExecutionOrderAttempts" ("Id") ON DELETE CASCADE,
                  "ClientOrderId" TEXT NOT NULL,
                  "BrokerOrderId" TEXT NOT NULL,
                  "BrokerState" TEXT NOT NULL,
                  "SanitizedPayloadJson" TEXT NOT NULL,
                  "ReceivedAtUtc" TEXT NOT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_AttemptId" ON "LiveExecutionBrokerInbox" ("AttemptId");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_ClientOrderId" ON "LiveExecutionBrokerInbox" ("ClientOrderId");
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_BrokerOrderId" ON "LiveExecutionBrokerInbox" ("BrokerOrderId");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionBrokerInbox_BatchId_ReceivedAtUtc" ON "LiveExecutionBrokerInbox" ("BatchId", "ReceivedAtUtc");
              """;
        await db.Database.ExecuteSqlRawAsync(executionSql);

        var reconciliationSql = db.Database.IsNpgsql()
            ? """
              CREATE TABLE IF NOT EXISTS "LiveExecutionReconciliationStates" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "BatchId" uuid NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "LastReconciledAtUtc" timestamp with time zone NULL,
                  "LatestBrokerSnapshotJson" text NULL,
                  "LatestRiskSnapshotJson" text NULL,
                  "FinalSnapshotJson" text NULL,
                  "FinalPortfolioVerified" boolean NOT NULL,
                  "InterventionReason" character varying(1000) NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionReconciliationStates_BatchId"
                  ON "LiveExecutionReconciliationStates" ("BatchId");
              CREATE TABLE IF NOT EXISTS "LiveExecutionOrderEvents" (
                  "Id" uuid NOT NULL PRIMARY KEY,
                  "CreatedAt" timestamp with time zone NOT NULL,
                  "UpdatedAt" timestamp with time zone NULL,
                  "BatchId" uuid NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "AttemptId" uuid NOT NULL REFERENCES "LiveExecutionOrderAttempts" ("Id") ON DELETE CASCADE,
                  "EventKey" character varying(64) NOT NULL,
                  "BrokerOrderId" character varying(200) NOT NULL,
                  "BrokerState" character varying(100) NOT NULL,
                  "OrderedQuantity" numeric(18,6) NOT NULL,
                  "FilledQuantity" numeric(18,6) NOT NULL,
                  "AverageFillPrice" numeric(18,4) NULL,
                  "BrokerUpdatedAtUtc" timestamp with time zone NOT NULL,
                  "ObservedAtUtc" timestamp with time zone NOT NULL,
                  "SanitizedPayloadJson" text NOT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderEvents_EventKey" ON "LiveExecutionOrderEvents" ("EventKey");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionOrderEvents_AttemptId_BrokerUpdatedAtUtc"
                  ON "LiveExecutionOrderEvents" ("AttemptId", "BrokerUpdatedAtUtc");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionOrderEvents_BatchId_ObservedAtUtc"
                  ON "LiveExecutionOrderEvents" ("BatchId", "ObservedAtUtc");
              """
            : """
              CREATE TABLE IF NOT EXISTS "LiveExecutionReconciliationStates" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "BatchId" TEXT NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "LastReconciledAtUtc" TEXT NULL,
                  "LatestBrokerSnapshotJson" TEXT NULL,
                  "LatestRiskSnapshotJson" TEXT NULL,
                  "FinalSnapshotJson" TEXT NULL,
                  "FinalPortfolioVerified" INTEGER NOT NULL,
                  "InterventionReason" TEXT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionReconciliationStates_BatchId"
                  ON "LiveExecutionReconciliationStates" ("BatchId");
              CREATE TABLE IF NOT EXISTS "LiveExecutionOrderEvents" (
                  "Id" TEXT NOT NULL PRIMARY KEY,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NULL,
                  "BatchId" TEXT NOT NULL REFERENCES "LiveExecutionBatches" ("Id") ON DELETE CASCADE,
                  "AttemptId" TEXT NOT NULL REFERENCES "LiveExecutionOrderAttempts" ("Id") ON DELETE CASCADE,
                  "EventKey" TEXT NOT NULL,
                  "BrokerOrderId" TEXT NOT NULL,
                  "BrokerState" TEXT NOT NULL,
                  "OrderedQuantity" TEXT NOT NULL,
                  "FilledQuantity" TEXT NOT NULL,
                  "AverageFillPrice" TEXT NULL,
                  "BrokerUpdatedAtUtc" TEXT NOT NULL,
                  "ObservedAtUtc" TEXT NOT NULL,
                  "SanitizedPayloadJson" TEXT NOT NULL
              );
              CREATE UNIQUE INDEX IF NOT EXISTS "IX_LiveExecutionOrderEvents_EventKey" ON "LiveExecutionOrderEvents" ("EventKey");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionOrderEvents_AttemptId_BrokerUpdatedAtUtc"
                  ON "LiveExecutionOrderEvents" ("AttemptId", "BrokerUpdatedAtUtc");
              CREATE INDEX IF NOT EXISTS "IX_LiveExecutionOrderEvents_BatchId_ObservedAtUtc"
                  ON "LiveExecutionOrderEvents" ("BatchId", "ObservedAtUtc");
              """;
        await db.Database.ExecuteSqlRawAsync(reconciliationSql);
    }
}
