using FluentAssertions;
using TradeMASter.Agents.Tools;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;
using Xunit;

namespace TradeMASter.Tests.Indicators;

public class TechnicalIndicatorTests
{
    private List<Candle> GenerateMockCandles(int count, decimal startPrice, decimal drift)
    {
        var candles = new List<Candle>();
        var price = startPrice;
        var time = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            price += drift;
            candles.Add(new Candle("NVDA", TimeFrame.OneDay, price - 1, price + 2, price - 2, price, 1_000_000m, time.AddDays(i)));
        }

        return candles;
    }

    [Fact]
    public void TechnicalIndicatorCalculator_CalculatesIndicators_WithinExpectedRanges()
    {
        var candles = GenerateMockCandles(60, 100m, 1.5m);
        var snapshot = TechnicalIndicatorCalculator.Calculate(candles);

        snapshot.LastClose.Should().BeGreaterThan(100m);
        snapshot.Rsi14.Should().BeInRange(0m, 100m);
        snapshot.BollingerUpper.Should().BeGreaterThanOrEqualTo(snapshot.BollingerMiddle);
        snapshot.BollingerMiddle.Should().BeGreaterThanOrEqualTo(snapshot.BollingerLower);
        snapshot.Atr14.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void CalculateEma_ReturnsSensibleMovingAverage()
    {
        var prices = new List<decimal> { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        var ema = TechnicalIndicatorCalculator.CalculateEma(prices, 5);

        ema.Should().BeGreaterThan(10m);
        ema.Should().BeLessThanOrEqualTo(20m);
    }

    [Fact]
    public void CalculateRsi_InStrongUptrend_ReturnsHighRsi()
    {
        var prices = Enumerable.Range(1, 30).Select(i => (decimal)i * 2m).ToList();
        var rsi = TechnicalIndicatorCalculator.CalculateRsi(prices, 14);

        rsi.Should().BeGreaterThan(70m);
    }
}
