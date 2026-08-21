using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Entities;

public class Position : BaseEntity
{
    public Guid PortfolioId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AverageEntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal UnrealizedPnLPercent { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal TotalCostBasis => Quantity * AverageEntryPrice;
    public decimal CurrentMarketValue => Quantity * CurrentPrice;

    public Position() { }

    public Position(Guid portfolioId, string symbol, decimal quantity, decimal averageEntryPrice)
    {
        PortfolioId = portfolioId;
        Symbol = symbol.ToUpperInvariant();
        Quantity = quantity;
        AverageEntryPrice = averageEntryPrice;
        CurrentPrice = averageEntryPrice;
        UnrealizedPnL = 0m;
        UnrealizedPnLPercent = 0m;
        RealizedPnL = 0m;
    }

    public void UpdateCurrentPrice(decimal newPrice)
    {
        CurrentPrice = newPrice;
        if (Quantity > 0)
        {
            UnrealizedPnL = (CurrentPrice - AverageEntryPrice) * Quantity;
            UnrealizedPnLPercent = AverageEntryPrice > 0 ? (UnrealizedPnL / TotalCostBasis) * 100m : 0m;
        }
        else
        {
            UnrealizedPnL = 0m;
            UnrealizedPnLPercent = 0m;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddQuantity(decimal addedQty, decimal fillPrice)
    {
        var totalExistingCost = Quantity * AverageEntryPrice;
        var newCost = addedQty * fillPrice;
        var totalQty = Quantity + addedQty;

        if (totalQty > 0)
        {
            AverageEntryPrice = (totalExistingCost + newCost) / totalQty;
            Quantity = totalQty;
        }

        UpdateCurrentPrice(fillPrice);
    }

    public decimal ReduceQuantity(decimal reduceQty, decimal fillPrice)
    {
        if (reduceQty > Quantity)
            throw new InvalidOperationException($"Cannot reduce {reduceQty} shares when holding only {Quantity}.");

        var profitPerShare = fillPrice - AverageEntryPrice;
        var realized = profitPerShare * reduceQty;
        RealizedPnL += realized;
        Quantity -= reduceQty;

        UpdateCurrentPrice(fillPrice);
        return realized;
    }
}
