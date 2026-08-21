using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Agents.Tools;

public record TechnicalIndicatorSnapshot(
    decimal LastClose,
    decimal Ema9,
    decimal Ema21,
    decimal Ema50,
    decimal Ema200,
    decimal Rsi14,
    decimal MacdLine,
    decimal MacdSignal,
    decimal MacdHistogram,
    decimal BollingerUpper,
    decimal BollingerMiddle,
    decimal BollingerLower,
    decimal Atr14,
    bool IsGoldenCross,
    bool IsDeathCross,
    string TrendDescription);

public static class TechnicalIndicatorCalculator
{
    public static TechnicalIndicatorSnapshot Calculate(IReadOnlyList<Candle> candles)
    {
        if (candles == null || candles.Count == 0)
        {
            return new TechnicalIndicatorSnapshot(
                LastClose: 0, Ema9: 0, Ema21: 0, Ema50: 0, Ema200: 0,
                Rsi14: 50, MacdLine: 0, MacdSignal: 0, MacdHistogram: 0,
                BollingerUpper: 0, BollingerMiddle: 0, BollingerLower: 0,
                Atr14: 0, IsGoldenCross: false, IsDeathCross: false,
                TrendDescription: "Insufficient candle history");
        }

        var closes = candles.Select(c => c.Close).ToList();
        var lastClose = closes.Last();

        var ema9 = CalculateEma(closes, 9);
        var ema21 = CalculateEma(closes, 21);
        var ema50 = CalculateEma(closes, 50);
        var ema200 = CalculateEma(closes, 200);

        var rsi14 = CalculateRsi(closes, 14);

        var (macdLine, macdSignal, macdHist) = CalculateMacd(closes);
        var (bbUpper, bbMid, bbLower) = CalculateBollingerBands(closes, 20, 2);
        var atr14 = CalculateAtr(candles, 14);

        var isGoldenCross = ema9 > ema21 && ema50 > ema200;
        var isDeathCross = ema9 < ema21 && ema50 < ema200;

        var trend = (lastClose > ema21 && ema21 > ema50)
            ? "Strong Bullish Uptrend (Above EMA 21 & 50)"
            : (lastClose < ema21 && ema21 < ema50)
            ? "Bearish Downtrend (Below EMA 21 & 50)"
            : "Consolidation / Rangebound";

        return new TechnicalIndicatorSnapshot(
            LastClose: Math.Round(lastClose, 2),
            Ema9: Math.Round(ema9, 2),
            Ema21: Math.Round(ema21, 2),
            Ema50: Math.Round(ema50, 2),
            Ema200: Math.Round(ema200, 2),
            Rsi14: Math.Round(rsi14, 2),
            MacdLine: Math.Round(macdLine, 3),
            MacdSignal: Math.Round(macdSignal, 3),
            MacdHistogram: Math.Round(macdHist, 3),
            BollingerUpper: Math.Round(bbUpper, 2),
            BollingerMiddle: Math.Round(bbMid, 2),
            BollingerLower: Math.Round(bbLower, 2),
            Atr14: Math.Round(atr14, 2),
            IsGoldenCross: isGoldenCross,
            IsDeathCross: isDeathCross,
            TrendDescription: trend
        );
    }

    public static decimal CalculateEma(List<decimal> values, int period)
    {
        if (values.Count == 0) return 0m;
        if (values.Count < period) return values.Average();

        var multiplier = 2m / (period + 1m);
        var ema = values.Take(period).Average();

        for (int i = period; i < values.Count; i++)
        {
            ema = ((values[i] - ema) * multiplier) + ema;
        }

        return ema;
    }

    public static decimal CalculateRsi(List<decimal> values, int period = 14)
    {
        if (values.Count <= period) return 50m;

        decimal gains = 0m;
        decimal losses = 0m;

        for (int i = 1; i <= period; i++)
        {
            var diff = values[i] - values[i - 1];
            if (diff >= 0) gains += diff;
            else losses += Math.Abs(diff);
        }

        var avgGain = gains / period;
        var avgLoss = losses / period;

        for (int i = period + 1; i < values.Count; i++)
        {
            var diff = values[i] - values[i - 1];
            var gain = diff >= 0 ? diff : 0m;
            var loss = diff < 0 ? Math.Abs(diff) : 0m;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    public static (decimal MacdLine, decimal MacdSignal, decimal Histogram) CalculateMacd(List<decimal> values)
    {
        if (values.Count < 26) return (0m, 0m, 0m);

        var ema12 = CalculateEmaSeries(values, 12);
        var ema26 = CalculateEmaSeries(values, 26);
        var macdSeries = ema12.Zip(ema26, (fast, slow) => fast - slow).ToList();
        var signalSeries = CalculateEmaSeries(macdSeries, 9);
        var macdLine = macdSeries[^1];
        var macdSignal = signalSeries[^1];
        var histogram = macdLine - macdSignal;

        return (macdLine, macdSignal, histogram);
    }

    private static IReadOnlyList<decimal> CalculateEmaSeries(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0) return [];
        var multiplier = 2m / (period + 1m);
        var ema = values.Take(Math.Min(period, values.Count)).Average();
        var result = new List<decimal>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            if (index < period - 1)
                ema = values.Take(index + 1).Average();
            else if (index == period - 1)
                ema = values.Take(period).Average();
            else
                ema = (values[index] - ema) * multiplier + ema;
            result.Add(ema);
        }
        return result;
    }

    public static (decimal Upper, decimal Middle, decimal Lower) CalculateBollingerBands(List<decimal> values, int period = 20, decimal stdDevMultiplier = 2m)
    {
        if (values.Count < period)
        {
            var avg = values.Count > 0 ? values.Average() : 0m;
            return (avg * 1.05m, avg, avg * 0.95m);
        }

        var slice = values.TakeLast(period).ToList();
        var sma = slice.Average();
        var variance = slice.Sum(v => (v - sma) * (v - sma)) / period;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        return (sma + (stdDevMultiplier * stdDev), sma, sma - (stdDevMultiplier * stdDev));
    }

    public static decimal CalculateAtr(IReadOnlyList<Candle> candles, int period = 14)
    {
        if (candles.Count < 2) return 0m;

        var trs = new List<decimal>();
        for (int i = 1; i < candles.Count; i++)
        {
            var current = candles[i];
            var prevClose = candles[i - 1].Close;

            var tr1 = current.High - current.Low;
            var tr2 = Math.Abs(current.High - prevClose);
            var tr3 = Math.Abs(current.Low - prevClose);

            trs.Add(Math.Max(tr1, Math.Max(tr2, tr3)));
        }

        return trs.TakeLast(Math.Min(period, trs.Count)).Average();
    }
}
