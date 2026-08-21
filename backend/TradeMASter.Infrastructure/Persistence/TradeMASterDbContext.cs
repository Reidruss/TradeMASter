using Microsoft.EntityFrameworkCore;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Persistence;

public class TradeMASterDbContext : DbContext, IUnitOfWork
{
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<DeliberationSession> DeliberationSessions => Set<DeliberationSession>();
    public DbSet<AgentDecision> AgentDecisions => Set<AgentDecision>();
    public DbSet<RobinhoodSession> RobinhoodSessions => Set<RobinhoodSession>();
    public DbSet<LivePortfolioPolicy> LivePortfolioPolicies => Set<LivePortfolioPolicy>();
    public DbSet<TradePlan> TradePlans => Set<TradePlan>();

    public TradeMASterDbContext(DbContextOptions<TradeMASterDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradeMASterDbContext).Assembly);
    }
}
