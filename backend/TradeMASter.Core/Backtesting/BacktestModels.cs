using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Core.Backtesting;

public enum StrategyType
{
    CommitteeConsensus = 0,
    MacdRsiMomentum = 1,
    EmaTrendBreakout = 2,
    MeanReversionBollinger = 3
}

public record BacktestRequest(
    string Symbol,
    TimeFrame TimeFrame,
    StrategyType Strategy,
    int CandleLimit = 180,
    decimal InitialBalance = 100_000m,
    decimal SlippagePercent = 0.05m,
    decimal CommissionPerTrade = 1.0m,
    decimal StopLossPercent = 3.0m,
    decimal TakeProfitPercent = 6.0m,
    decimal MaxPositionSizePercent = 20.0m);

public record BacktestTrade(
    Guid Id,
    string Symbol,
    OrderSide Side,
    DateTime EntryTime,
    decimal EntryPrice,
    DateTime ExitTime,
    decimal ExitPrice,
    decimal Quantity,
    decimal PnL,
    decimal ReturnPercent,
    string ExitReason);

public record EquityPoint(
    DateTime Timestamp,
    decimal Equity,
    decimal Cash,
    decimal DrawdownPercent);

public record BacktestPerformanceMetrics(
    decimal InitialBalance,
    decimal FinalEquity,
    decimal NetProfit,
    decimal TotalReturnPercent,
    decimal BuyAndHoldReturnPercent,
    double SharpeRatio,
    double SortinoRatio,
    decimal MaxDrawdownPercent,
    decimal MaxDrawdownDollars,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal WinRatePercent,
    decimal ProfitFactor,
    decimal AverageTradeReturnPercent,
    decimal AverageWinPercent,
    decimal AverageLossPercent,
    decimal LargestWinDollars,
    decimal LargestLossDollars);

public record BacktestResult(
    BacktestRequest Request,
    BacktestPerformanceMetrics Metrics,
    IReadOnlyList<BacktestTrade> Trades,
    IReadOnlyList<EquityPoint> EquityCurve);
