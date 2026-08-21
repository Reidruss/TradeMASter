using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Events;

namespace TradeMASter.Core.Entities;

public class Order : BaseEntity
{
    public Guid PortfolioId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal? LimitPrice { get; set; }
    public decimal? StopPrice { get; set; }
    public decimal? FilledPrice { get; set; }
    public decimal FilledQuantity { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FilledAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? DeliberationSessionId { get; set; }

    public Order() { }

    public Order(
        Guid portfolioId,
        string symbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? limitPrice = null,
        decimal? stopPrice = null,
        Guid? deliberationSessionId = null)
    {
        PortfolioId = portfolioId;
        Symbol = symbol.ToUpperInvariant();
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        StopPrice = stopPrice;
        Status = OrderStatus.Pending;
        SubmittedAt = DateTime.UtcNow;
        DeliberationSessionId = deliberationSessionId;

        AddDomainEvent(new OrderPlacedEvent(Id, PortfolioId, Symbol, Side, Type, Quantity, LimitPrice, SubmittedAt));
    }

    public void MarkFilled(decimal fillPrice, decimal quantity)
    {
        FilledPrice = fillPrice;
        FilledQuantity = quantity;
        Status = OrderStatus.Filled;
        FilledAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new OrderFilledEvent(Id, PortfolioId, Symbol, Side, quantity, fillPrice, FilledAt.Value));
    }

    public void MarkRejected(string reason)
    {
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Filled)
            throw new InvalidOperationException("Cannot cancel an already filled order.");

        Status = OrderStatus.Canceled;
        UpdatedAt = DateTime.UtcNow;
    }
}
