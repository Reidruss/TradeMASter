using TradeMASter.Agents.Tools;
using TradeMASter.Core.Backtesting;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Agents.Backtesting.Strategies;

public class CommitteeConsensusStrategy : IStrategy
{
    public StrategyType Type => StrategyType.CommitteeConsensus;
    public string Name => "Multi-Agent Committee Consensus";
    public string Description => "Simulates committee synthesis of technical indicators, valuation metrics, and news sentiment tone.";

    public StrategySignal Evaluate(IReadOnlyList<Candle> historicalCandlesSlice, int currentIndex)
    {
        if (historicalCandlesSlice.Count < 26) return StrategySignal.Hold;

        var ind = TechnicalIndicatorCalculator.Calculate(historicalCandlesSlice);
        var isBullishTech = ind.LastClose > ind.Ema21 && ind.MacdHistogram > 0;
        var isReasonableRsi = ind.Rsi14 >= 42 && ind.Rsi14 <= 68;

        if (isBullishTech && isReasonableRsi)
        {
            return StrategySignal.EnterLong;
        }

        if (ind.LastClose < ind.Ema50 || ind.Rsi14 > 78 || ind.MacdHistogram < -1.5m)
        {
            return StrategySignal.ExitLong;
        }

        return StrategySignal.Hold;
    }
}

public class MacdRsiMomentumStrategy : IStrategy
{
    public StrategyType Type => StrategyType.MacdRsiMomentum;
    public string Name => "MACD & RSI Momentum Divergence";
    public string Description => "Enters on bullish MACD histogram inflection with confirming RSI momentum; exits on overbought exhaustion or signal line breakdown.";

    public StrategySignal Evaluate(IReadOnlyList<Candle> historicalCandlesSlice, int currentIndex)
    {
        if (historicalCandlesSlice.Count < 30) return StrategySignal.Hold;

        var ind = TechnicalIndicatorCalculator.Calculate(historicalCandlesSlice);

        // Entry: MACD Histogram positive and expanding, RSI between 45 and 65
        if (ind.MacdHistogram > 0.1m && ind.Rsi14 > 45 && ind.Rsi14 < 68)
        {
            return StrategySignal.EnterLong;
        }

        // Exit: MACD Histogram flips negative or RSI > 72
        if (ind.MacdHistogram < -0.2m || ind.Rsi14 > 72)
        {
            return StrategySignal.ExitLong;
        }

        return StrategySignal.Hold;
    }
}

public class EmaTrendBreakoutStrategy : IStrategy
{
    public StrategyType Type => StrategyType.EmaTrendBreakout;
    public string Name => "Triple EMA Trend Breakout";
    public string Description => "Follows strong directional trends when EMA 9 > EMA 21 > EMA 50; rides momentum until closing below EMA 21.";

    public StrategySignal Evaluate(IReadOnlyList<Candle> historicalCandlesSlice, int currentIndex)
    {
        if (historicalCandlesSlice.Count < 50) return StrategySignal.Hold;

        var ind = TechnicalIndicatorCalculator.Calculate(historicalCandlesSlice);

        // Long when fast EMA > medium EMA > slow EMA and price is above EMA 9
        if (ind.Ema9 > ind.Ema21 && ind.Ema21 > ind.Ema50 && ind.LastClose > ind.Ema9)
        {
            return StrategySignal.EnterLong;
        }

        // Exit when price drops below medium EMA 21
        if (ind.LastClose < ind.Ema21)
        {
            return StrategySignal.ExitLong;
        }

        return StrategySignal.Hold;
    }
}

public class MeanReversionBollingerStrategy : IStrategy
{
    public StrategyType Type => StrategyType.MeanReversionBollinger;
    public string Name => "Bollinger Bands Mean Reversion";
    public string Description => "Buys when price stretches below Lower Bollinger Band with oversold RSI; takes profit when price reverts to Middle SMA Band.";

    public StrategySignal Evaluate(IReadOnlyList<Candle> historicalCandlesSlice, int currentIndex)
    {
        if (historicalCandlesSlice.Count < 25) return StrategySignal.Hold;

        var ind = TechnicalIndicatorCalculator.Calculate(historicalCandlesSlice);

        // Entry: Price touches or dips below lower band, RSI < 38
        if (ind.LastClose <= ind.BollingerLower * 1.005m && ind.Rsi14 < 38)
        {
            return StrategySignal.EnterLong;
        }

        // Exit: Price reverts to Middle Band or Upper Band or RSI > 58
        if (ind.LastClose >= ind.BollingerMiddle || ind.Rsi14 > 58)
        {
            return StrategySignal.ExitLong;
        }

        return StrategySignal.Hold;
    }
}
