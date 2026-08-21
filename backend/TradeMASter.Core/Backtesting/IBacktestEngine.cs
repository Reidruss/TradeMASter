using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Core.Backtesting;

public enum StrategySignal
{
    Hold = 0,
    EnterLong = 1,
    ExitLong = 2,
    EnterShort = 3,
    ExitShort = 4
}

public interface IStrategy
{
    StrategyType Type { get; }
    string Name { get; }
    string Description { get; }
    StrategySignal Evaluate(IReadOnlyList<Candle> historicalCandlesSlice, int currentIndex);
}

public interface IBacktestEngine
{
    Task<Result<BacktestResult>> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default);
    IReadOnlyList<StrategyInfo> GetAvailableStrategies();
}

public record StrategyInfo(StrategyType Type, string Name, string Description, string DefaultTimeFrame);
