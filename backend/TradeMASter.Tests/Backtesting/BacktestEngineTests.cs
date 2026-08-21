using FluentAssertions;
using Moq;
using TradeMASter.Agents.Backtesting;
using TradeMASter.Core.Backtesting;
using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;
using TradeMASter.Infrastructure.MarketData;
using Xunit;

namespace TradeMASter.Tests.Backtesting;

public class BacktestEngineTests
{
    private readonly Mock<IMarketDataService> _mockMarketData = new();

    private List<Candle> GenerateCandleSeries(int count, decimal initialPrice)
    {
        var list = new List<Candle>();
        var price = initialPrice;
        var now = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            var change = (i % 2 == 0 ? 1.0m : -0.5m);
            price += change;
            list.Add(new Candle("AAPL", TimeFrame.OneDay, price - 0.5m, price + 1m, price - 1m, price, 500_000m, now.AddDays(i)));
        }

        return list;
    }

    [Fact]
    public async Task RunBacktestAsync_WithValidCandles_CalculatesMetricsAndEquityCurve()
    {
        var mockCandles = GenerateCandleSeries(100, 150m);
        _mockMarketData
            .Setup(m => m.GetCandlesAsync("AAPL", TimeFrame.OneDay, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Candle>>(mockCandles));

        var engine = new BacktestEngine(_mockMarketData.Object);
        var request = new BacktestRequest(
            Symbol: "AAPL",
            TimeFrame: TimeFrame.OneDay,
            Strategy: StrategyType.MacdRsiMomentum,
            CandleLimit: 100,
            InitialBalance: 50_000m,
            SlippagePercent: 0.05m,
            CommissionPerTrade: 1.0m
        );

        var result = await engine.RunBacktestAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.EquityCurve.Should().NotBeEmpty();
        result.Value.Metrics.InitialBalance.Should().Be(50_000m);
        result.Value.Metrics.FinalEquity.Should().BeGreaterThan(0m);
        result.Value.Metrics.MaxDrawdownPercent.Should().BeGreaterThanOrEqualTo(0m);
    }
}
