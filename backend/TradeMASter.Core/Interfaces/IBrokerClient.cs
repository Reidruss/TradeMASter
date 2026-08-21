using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Interfaces;

public record OrderRequest(
    Guid PortfolioId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice = null,
    decimal? StopPrice = null,
    Guid? DeliberationSessionId = null);

public record OrderCancelResult(Guid OrderId, bool Success, string? Message);

public interface IBrokerClient
{
    string BrokerName { get; }
    Task<Result<Order>> SubmitOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);
    Task<Result<OrderCancelResult>> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<Result<Portfolio>> GetPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Position>>> GetPositionsAsync(Guid portfolioId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Order>>> GetOrdersAsync(Guid portfolioId, OrderStatus? statusFilter = null, CancellationToken cancellationToken = default);
}
