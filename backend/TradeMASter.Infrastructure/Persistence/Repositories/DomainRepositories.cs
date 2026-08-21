using Microsoft.EntityFrameworkCore;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Persistence.Repositories;

public interface IPortfolioRepository : IRepository<Portfolio>
{
    Task<Portfolio?> GetActivePortfolioWithDetailsAsync(CancellationToken cancellationToken = default);
    Task<Portfolio?> GetByIdWithDetailsAsync(Guid portfolioId, CancellationToken cancellationToken = default);
}

public class PortfolioRepository : EfRepository<Portfolio>, IPortfolioRepository
{
    public PortfolioRepository(TradeMASterDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<Portfolio?> GetActivePortfolioWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Positions)
            .Include(p => p.Orders)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Portfolio?> GetByIdWithDetailsAsync(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Positions)
            .Include(p => p.Orders)
            .FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
    }
}

public interface IAssetRepository : IRepository<Asset>
{
    Task<Asset?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Asset>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

public class AssetRepository : EfRepository<Asset>, IAssetRepository
{
    public AssetRepository(TradeMASterDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<Asset?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var upper = symbol.ToUpperInvariant();
        return await DbSet.FirstOrDefaultAsync(a => a.Symbol == upper, cancellationToken);
    }

    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var upper = query.ToUpperInvariant();
        return await DbSet
            .Where(a => a.Symbol.Contains(upper) || a.Name.ToUpper().Contains(upper))
            .Take(25)
            .ToListAsync(cancellationToken);
    }
}

public interface IOrderRepository : IRepository<Order>
{
    Task<IReadOnlyList<Order>> GetByPortfolioAsync(Guid portfolioId, OrderStatus? status = null, CancellationToken cancellationToken = default);
}

public class OrderRepository : EfRepository<Order>, IOrderRepository
{
    public OrderRepository(TradeMASterDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<Order>> GetByPortfolioAsync(Guid portfolioId, OrderStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(o => o.PortfolioId == portfolioId);
        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        return await query.OrderByDescending(o => o.SubmittedAt).ToListAsync(cancellationToken);
    }
}
