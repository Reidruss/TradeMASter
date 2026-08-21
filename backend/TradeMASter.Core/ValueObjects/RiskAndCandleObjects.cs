using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.ValueObjects;

public class Candle : ValueObject
{
    public string Symbol { get; }
    public TimeFrame TimeFrame { get; }
    public decimal Open { get; }
    public decimal High { get; }
    public decimal Low { get; }
    public decimal Close { get; }
    public decimal Volume { get; }
    public DateTime Timestamp { get; }

    public Candle(
        string symbol,
        TimeFrame timeFrame,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        DateTime timestamp)
    {
        Symbol = symbol.ToUpperInvariant();
        TimeFrame = timeFrame;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Timestamp = timestamp;
    }

    public bool IsBullish => Close >= Open;
    public decimal BodySize => Math.Abs(Close - Open);
    public decimal Range => High - Low;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Symbol;
        yield return TimeFrame;
        yield return Timestamp;
        yield return Open;
        yield return High;
        yield return Low;
        yield return Close;
        yield return Volume;
    }
}

public class RiskParameters : ValueObject
{
    public decimal MaxPositionSizePercent { get; set; } = 10.0m;
    public decimal MaxPortfolioDrawdownPercent { get; set; } = 5.0m;
    public decimal DefaultStopLossPercent { get; set; } = 2.5m;
    public decimal DefaultTakeProfitPercent { get; set; } = 5.0m;
    public bool RequireHumanApprovalForLive { get; set; } = true;
    public decimal MaxDailyLossAmount { get; set; } = 1000m;

    public RiskParameters() { }

    public RiskParameters(
        decimal maxPositionSizePercent,
        decimal maxPortfolioDrawdownPercent,
        decimal defaultStopLossPercent,
        decimal defaultTakeProfitPercent,
        bool requireHumanApprovalForLive,
        decimal maxDailyLossAmount)
    {
        MaxPositionSizePercent = maxPositionSizePercent;
        MaxPortfolioDrawdownPercent = maxPortfolioDrawdownPercent;
        DefaultStopLossPercent = defaultStopLossPercent;
        DefaultTakeProfitPercent = defaultTakeProfitPercent;
        RequireHumanApprovalForLive = requireHumanApprovalForLive;
        MaxDailyLossAmount = maxDailyLossAmount;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaxPositionSizePercent;
        yield return MaxPortfolioDrawdownPercent;
        yield return DefaultStopLossPercent;
        yield return DefaultTakeProfitPercent;
        yield return RequireHumanApprovalForLive;
        yield return MaxDailyLossAmount;
    }
}
