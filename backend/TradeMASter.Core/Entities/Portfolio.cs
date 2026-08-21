using TradeMASter.Core.Common;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Core.Entities;

public class Portfolio : BaseEntity
{
    public string Name { get; set; } = "Default Portfolio";
    public decimal CashBalance { get; set; } = 100_000m;
    public decimal InitialBalance { get; set; } = 100_000m;
    public RiskParameters RiskConfig { get; set; } = new();
    public List<Position> Positions { get; set; } = new();
    public List<Order> Orders { get; set; } = new();

    public decimal TotalPositionValue => Positions.Sum(p => p.CurrentMarketValue);
    public decimal TotalEquity => CashBalance + TotalPositionValue;
    public decimal TotalUnrealizedPnL => Positions.Sum(p => p.UnrealizedPnL);
    public decimal TotalRealizedPnL => Positions.Sum(p => p.RealizedPnL);
    public decimal TotalPnL => TotalEquity - InitialBalance;
    public decimal TotalPnLPercent => InitialBalance > 0 ? (TotalPnL / InitialBalance) * 100m : 0m;

    public Portfolio() { }

    public Portfolio(string name, decimal initialBalance = 100_000m)
    {
        Name = name;
        CashBalance = initialBalance;
        InitialBalance = initialBalance;
        RiskConfig = new RiskParameters();
    }

    public void Deposit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Deposit amount must be positive.", nameof(amount));
        CashBalance += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Withdrawal amount must be positive.", nameof(amount));
        if (amount > CashBalance) throw new InvalidOperationException("Insufficient cash balance.");
        CashBalance -= amount;
        UpdatedAt = DateTime.UtcNow;
    }
}
