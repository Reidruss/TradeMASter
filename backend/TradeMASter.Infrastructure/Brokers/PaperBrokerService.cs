using Microsoft.EntityFrameworkCore;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Infrastructure.Brokers;

public class PaperBrokerService : IBrokerClient
{
    public string BrokerName => "PaperBroker";

    private readonly TradeMASterDbContext _dbContext;
    private readonly IMarketDataService _marketData;

    public PaperBrokerService(
        TradeMASterDbContext dbContext,
        IMarketDataService marketData)
    {
        _dbContext = dbContext;
        _marketData = marketData;
    }

    public async Task<Result<Order>> SubmitOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<Order>("Order quantity must be greater than zero.");
        }

        var portfolio = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .Include(p => p.Orders)
            .FirstOrDefaultAsync(p => p.Id == request.PortfolioId, cancellationToken);

        if (portfolio is null)
        {
            return Result.Failure<Order>($"Portfolio with ID '{request.PortfolioId}' not found.");
        }

        // Fetch current market price
        var quoteResult = await _marketData.GetQuoteAsync(request.Symbol, cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<Order>($"Failed to obtain market quote for {request.Symbol}: {quoteResult.Error}");
        }

        var currentPrice = quoteResult.Value.Price;
        if (currentPrice <= 0)
        {
            return Result.Failure<Order>($"Invalid market price ({currentPrice}) for {request.Symbol}.");
        }

        // Calculate fill price with 0.02% simulated execution slippage
        var slippageMultiplier = request.Side == OrderSide.Buy ? 1.0002m : 0.9998m;
        var fillPrice = Math.Round(currentPrice * slippageMultiplier, 2);
        var totalOrderValue = request.Quantity * fillPrice;

        var order = new Order(
            portfolio.Id,
            request.Symbol,
            request.Side,
            request.Type,
            request.Quantity,
            request.LimitPrice,
            request.StopPrice,
            request.DeliberationSessionId
        );

        // Pre-trade Risk Management validation
        if (request.Side == OrderSide.Buy)
        {
            if (totalOrderValue > portfolio.CashBalance)
            {
                order.MarkRejected($"Insufficient funds. Required: ${totalOrderValue:N2}, Available: ${portfolio.CashBalance:N2}");
                await _dbContext.Orders.AddAsync(order, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Failure<Order>(order.RejectionReason!);
            }

            var maxAllowedPositionValue = portfolio.TotalEquity * (portfolio.RiskConfig.MaxPositionSizePercent / 100m);
            var existingPosition = portfolio.Positions.FirstOrDefault(p => p.Symbol.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));
            var projectedPositionValue = (existingPosition?.CurrentMarketValue ?? 0m) + totalOrderValue;

            if (projectedPositionValue > maxAllowedPositionValue && portfolio.TotalEquity > 1000m)
            {
                order.MarkRejected($"Order exceeds Max Position Size risk limit ({portfolio.RiskConfig.MaxPositionSizePercent}% of equity = ${maxAllowedPositionValue:N2}).");
                await _dbContext.Orders.AddAsync(order, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Failure<Order>(order.RejectionReason!);
            }

            // Execute Buy Fill
            portfolio.CashBalance -= totalOrderValue;

            if (existingPosition is not null)
            {
                existingPosition.AddQuantity(request.Quantity, fillPrice);
            }
            else
            {
                var newPosition = new Position(portfolio.Id, request.Symbol, request.Quantity, fillPrice);
                await _dbContext.Positions.AddAsync(newPosition, cancellationToken);
            }

            order.MarkFilled(fillPrice, request.Quantity);
        }
        else // OrderSide.Sell
        {
            var existingPosition = portfolio.Positions.FirstOrDefault(p => p.Symbol.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));
            if (existingPosition is null || existingPosition.Quantity < request.Quantity)
            {
                var holding = existingPosition?.Quantity ?? 0m;
                order.MarkRejected($"Insufficient position quantity to sell. Holding: {holding}, Requested: {request.Quantity}");
                await _dbContext.Orders.AddAsync(order, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Failure<Order>(order.RejectionReason!);
            }

            // Execute Sell Fill
            existingPosition.ReduceQuantity(request.Quantity, fillPrice);
            portfolio.CashBalance += totalOrderValue;

            if (existingPosition.Quantity == 0)
            {
                _dbContext.Positions.Remove(existingPosition);
            }

            order.MarkFilled(fillPrice, request.Quantity);
        }

        await _dbContext.Orders.AddAsync(order, cancellationToken);
        portfolio.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(order);
    }

    public async Task<Result<OrderCancelResult>> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return Result.Failure<OrderCancelResult>("Order not found.");
        }

        if (order.Status == OrderStatus.Filled)
        {
            return Result.Failure<OrderCancelResult>("Cannot cancel an already filled order.");
        }

        order.Cancel();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new OrderCancelResult(order.Id, true, "Order successfully canceled."));
    }

    public async Task<Result<Portfolio>> GetPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var portfolio = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .Include(p => p.Orders)
            .FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);

        if (portfolio is null)
        {
            return Result.Failure<Portfolio>("Portfolio not found.");
        }

        // Update positions with latest market prices
        foreach (var pos in portfolio.Positions)
        {
            var quote = await _marketData.GetQuoteAsync(pos.Symbol, cancellationToken);
            if (quote.IsSuccess)
            {
                pos.UpdateCurrentPrice(quote.Value.Price);
            }
        }

        return Result.Success(portfolio);
    }

    public async Task<Result<IReadOnlyList<Position>>> GetPositionsAsync(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var portfolio = await _dbContext.Portfolios
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);

        if (portfolio is null)
        {
            return Result.Failure<IReadOnlyList<Position>>("Portfolio not found.");
        }

        foreach (var pos in portfolio.Positions)
        {
            var quote = await _marketData.GetQuoteAsync(pos.Symbol, cancellationToken);
            if (quote.IsSuccess)
            {
                pos.UpdateCurrentPrice(quote.Value.Price);
            }
        }

        return Result.Success<IReadOnlyList<Position>>(portfolio.Positions);
    }

    public async Task<Result<IReadOnlyList<Order>>> GetOrdersAsync(Guid portfolioId, OrderStatus? statusFilter = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Orders.Where(o => o.PortfolioId == portfolioId);
        if (statusFilter.HasValue)
        {
            query = query.Where(o => o.Status == statusFilter.Value);
        }

        var orders = await query.OrderByDescending(o => o.SubmittedAt).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<Order>>(orders);
    }
}
