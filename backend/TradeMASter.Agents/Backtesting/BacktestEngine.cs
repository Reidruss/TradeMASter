using TradeMASter.Agents.Backtesting.Strategies;
using TradeMASter.Core.Backtesting;
using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;
using TradeMASter.Infrastructure.MarketData;

namespace TradeMASter.Agents.Backtesting;

public class BacktestEngine : IBacktestEngine
{
    private readonly IMarketDataService _marketData;
    private readonly Dictionary<StrategyType, IStrategy> _strategies;

    public BacktestEngine(IMarketDataService marketData)
    {
        _marketData = marketData;
        _strategies = new Dictionary<StrategyType, IStrategy>
        {
            [StrategyType.CommitteeConsensus] = new CommitteeConsensusStrategy(),
            [StrategyType.MacdRsiMomentum] = new MacdRsiMomentumStrategy(),
            [StrategyType.EmaTrendBreakout] = new EmaTrendBreakoutStrategy(),
            [StrategyType.MeanReversionBollinger] = new MeanReversionBollingerStrategy()
        };
    }

    public IReadOnlyList<StrategyInfo> GetAvailableStrategies()
    {
        return _strategies.Values.Select(s => new StrategyInfo(
            s.Type,
            s.Name,
            s.Description,
            "1D"
        )).ToList();
    }

    public async Task<Result<BacktestResult>> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        if (!_strategies.TryGetValue(request.Strategy, out var strategy))
        {
            return Result.Failure<BacktestResult>($"Strategy '{request.Strategy}' is not registered.");
        }

        var candlesRes = await _marketData.GetCandlesAsync(request.Symbol, request.TimeFrame, request.CandleLimit, cancellationToken);
        if (candlesRes.IsFailure || candlesRes.Value.Count < 30)
        {
            return Result.Failure<BacktestResult>($"Insufficient historical candle data for {request.Symbol} (found {candlesRes.Value?.Count ?? 0} bars).");
        }

        var candles = candlesRes.Value.OrderBy(c => c.Timestamp).ToList();

        decimal cash = request.InitialBalance;
        decimal positionQty = 0m;
        decimal positionEntryPrice = 0m;
        DateTime positionEntryTime = DateTime.MinValue;
        Guid currentTradeId = Guid.Empty;

        var trades = new List<BacktestTrade>();
        var equityCurve = new List<EquityPoint>();
        var dailyReturns = new List<double>();
        decimal peakEquity = request.InitialBalance;

        var slippageFactor = request.SlippagePercent / 100m;
        var stopLossFactor = request.StopLossPercent / 100m;
        var takeProfitFactor = request.TakeProfitPercent / 100m;

        for (int i = 25; i < candles.Count; i++)
        {
            var currentCandle = candles[i];
            var historicalSlice = candles.Take(i + 1).ToList();

            // 1. Check open position triggers (Stop-Loss or Take-Profit)
            if (positionQty > 0)
            {
                var stopPrice = positionEntryPrice * (1m - stopLossFactor);
                var tpPrice = positionEntryPrice * (1m + takeProfitFactor);

                bool exited = false;
                decimal exitPrice = 0m;
                string exitReason = "";

                if (currentCandle.Low <= stopPrice)
                {
                    exitPrice = Math.Min(currentCandle.Open, stopPrice) * (1m - slippageFactor);
                    exitReason = "Stop-Loss Triggered";
                    exited = true;
                }
                else if (currentCandle.High >= tpPrice)
                {
                    exitPrice = Math.Max(currentCandle.Open, tpPrice) * (1m - slippageFactor);
                    exitReason = "Take-Profit Target Reached";
                    exited = true;
                }

                if (exited)
                {
                    var grossProceeds = positionQty * exitPrice;
                    var netProceeds = grossProceeds - request.CommissionPerTrade;
                    cash += netProceeds;

                    var totalCost = positionQty * positionEntryPrice;
                    var pnl = netProceeds - totalCost;
                    var returnPct = totalCost > 0 ? (pnl / totalCost) * 100m : 0m;

                    trades.Add(new BacktestTrade(
                        currentTradeId,
                        request.Symbol,
                        OrderSide.Buy,
                        positionEntryTime,
                        positionEntryPrice,
                        currentCandle.Timestamp,
                        Math.Round(exitPrice, 2),
                        positionQty,
                        Math.Round(pnl, 2),
                        Math.Round(returnPct, 2),
                        exitReason
                    ));

                    positionQty = 0m;
                    positionEntryPrice = 0m;
                }
            }

            // 2. Evaluate Strategy Signal
            var signal = strategy.Evaluate(historicalSlice, i);

            if (signal == StrategySignal.EnterLong && positionQty == 0)
            {
                var currentPrice = currentCandle.Close;
                var fillPrice = currentPrice * (1m + slippageFactor);

                var maxAlloc = (cash + (positionQty * currentPrice)) * (request.MaxPositionSizePercent / 100m);
                var orderCash = Math.Min(cash - request.CommissionPerTrade, maxAlloc);

                if (orderCash > currentPrice)
                {
                    var shares = Math.Floor(orderCash / fillPrice);
                    if (shares > 0)
                    {
                        var orderCost = (shares * fillPrice) + request.CommissionPerTrade;
                        cash -= orderCost;
                        positionQty = shares;
                        positionEntryPrice = fillPrice;
                        positionEntryTime = currentCandle.Timestamp;
                        currentTradeId = Guid.NewGuid();
                    }
                }
            }
            else if (signal == StrategySignal.ExitLong && positionQty > 0)
            {
                var exitPrice = currentCandle.Close * (1m - slippageFactor);
                var grossProceeds = positionQty * exitPrice;
                var netProceeds = grossProceeds - request.CommissionPerTrade;
                cash += netProceeds;

                var totalCost = positionQty * positionEntryPrice;
                var pnl = netProceeds - totalCost;
                var returnPct = totalCost > 0 ? (pnl / totalCost) * 100m : 0m;

                trades.Add(new BacktestTrade(
                    currentTradeId,
                    request.Symbol,
                    OrderSide.Buy,
                    positionEntryTime,
                    positionEntryPrice,
                    currentCandle.Timestamp,
                    Math.Round(exitPrice, 2),
                    positionQty,
                    Math.Round(pnl, 2),
                    Math.Round(returnPct, 2),
                    "Strategy Signal Exit"
                ));

                positionQty = 0m;
                positionEntryPrice = 0m;
            }

            // 3. Calculate Portfolio Equity Point
            var currentPosValue = positionQty * currentCandle.Close;
            var currentTotalEquity = cash + currentPosValue;

            if (currentTotalEquity > peakEquity)
            {
                peakEquity = currentTotalEquity;
            }

            var drawdownPct = peakEquity > 0 ? ((peakEquity - currentTotalEquity) / peakEquity) * 100m : 0m;

            if (equityCurve.Count > 0)
            {
                var prevEq = (double)equityCurve.Last().Equity;
                var curEq = (double)currentTotalEquity;
                if (prevEq > 0)
                {
                    dailyReturns.Add((curEq - prevEq) / prevEq);
                }
            }

            equityCurve.Add(new EquityPoint(
                currentCandle.Timestamp,
                Math.Round(currentTotalEquity, 2),
                Math.Round(cash, 2),
                Math.Round(drawdownPct, 2)
            ));
        }

        // Close any remaining position at end of period
        if (positionQty > 0 && candles.Count > 0)
        {
            var lastCandle = candles.Last();
            var exitPrice = lastCandle.Close * (1m - slippageFactor);
            var grossProceeds = positionQty * exitPrice;
            var netProceeds = grossProceeds - request.CommissionPerTrade;
            cash += netProceeds;

            var totalCost = positionQty * positionEntryPrice;
            var pnl = netProceeds - totalCost;
            var returnPct = totalCost > 0 ? (pnl / totalCost) * 100m : 0m;

            trades.Add(new BacktestTrade(
                currentTradeId,
                request.Symbol,
                OrderSide.Buy,
                positionEntryTime,
                positionEntryPrice,
                lastCandle.Timestamp,
                Math.Round(exitPrice, 2),
                positionQty,
                Math.Round(pnl, 2),
                Math.Round(returnPct, 2),
                "End of Backtest Period"
            ));

            positionQty = 0m;
        }

        // 4. Calculate Final Performance Metrics
        var finalEquity = equityCurve.Count > 0 ? equityCurve.Last().Equity : request.InitialBalance;
        var netProfit = finalEquity - request.InitialBalance;
        var totalReturnPct = request.InitialBalance > 0 ? (netProfit / request.InitialBalance) * 100m : 0m;

        var firstCandlePrice = candles.First().Close;
        var lastCandlePrice = candles.Last().Close;
        var buyAndHoldReturn = firstCandlePrice > 0 ? ((lastCandlePrice - firstCandlePrice) / firstCandlePrice) * 100m : 0m;

        var maxDrawdownPct = equityCurve.Count > 0 ? equityCurve.Max(e => e.DrawdownPercent) : 0m;
        var maxDrawdownDollars = peakEquity * (maxDrawdownPct / 100m);

        var winTrades = trades.Where(t => t.PnL > 0).ToList();
        var lossTrades = trades.Where(t => t.PnL <= 0).ToList();
        var winRatePct = trades.Count > 0 ? ((decimal)winTrades.Count / trades.Count) * 100m : 0m;

        var grossProfit = winTrades.Sum(t => t.PnL);
        var grossLoss = Math.Abs(lossTrades.Sum(t => t.PnL));
        var profitFactor = grossLoss > 0 ? grossProfit / grossLoss : (grossProfit > 0 ? 99.9m : 1.0m);

        var avgTradeReturn = trades.Count > 0 ? trades.Average(t => t.ReturnPercent) : 0m;
        var avgWin = winTrades.Count > 0 ? winTrades.Average(t => t.ReturnPercent) : 0m;
        var avgLoss = lossTrades.Count > 0 ? lossTrades.Average(t => t.ReturnPercent) : 0m;

        var largestWin = winTrades.Count > 0 ? winTrades.Max(t => t.PnL) : 0m;
        var largestLoss = lossTrades.Count > 0 ? Math.Abs(lossTrades.Min(t => t.PnL)) : 0m;

        // Sharpe and Sortino ratios (assuming risk-free rate 4% annualized ~ 0.015% per day)
        double sharpe = 0.0;
        double sortino = 0.0;

        if (dailyReturns.Count > 5)
        {
            var meanReturn = dailyReturns.Average();
            var rfDaily = 0.04 / 252.0;
            var excessReturn = meanReturn - rfDaily;

            var variance = dailyReturns.Sum(r => Math.Pow(r - meanReturn, 2)) / (dailyReturns.Count - 1);
            var stdDev = Math.Sqrt(variance);

            if (stdDev > 0)
            {
                sharpe = Math.Round((excessReturn / stdDev) * Math.Sqrt(252), 2);
            }

            var downsideReturns = dailyReturns.Where(r => r < 0).ToList();
            if (downsideReturns.Count > 0)
            {
                var downsideVariance = downsideReturns.Sum(r => Math.Pow(r, 2)) / downsideReturns.Count;
                var downsideStdDev = Math.Sqrt(downsideVariance);
                if (downsideStdDev > 0)
                {
                    sortino = Math.Round((excessReturn / downsideStdDev) * Math.Sqrt(252), 2);
                }
            }
        }

        var metrics = new BacktestPerformanceMetrics(
            InitialBalance: request.InitialBalance,
            FinalEquity: Math.Round(finalEquity, 2),
            NetProfit: Math.Round(netProfit, 2),
            TotalReturnPercent: Math.Round(totalReturnPct, 2),
            BuyAndHoldReturnPercent: Math.Round(buyAndHoldReturn, 2),
            SharpeRatio: sharpe,
            SortinoRatio: sortino,
            MaxDrawdownPercent: Math.Round(maxDrawdownPct, 2),
            MaxDrawdownDollars: Math.Round(maxDrawdownDollars, 2),
            TotalTrades: trades.Count,
            WinningTrades: winTrades.Count,
            LosingTrades: lossTrades.Count,
            WinRatePercent: Math.Round(winRatePct, 2),
            ProfitFactor: Math.Round(profitFactor, 2),
            AverageTradeReturnPercent: Math.Round(avgTradeReturn, 2),
            AverageWinPercent: Math.Round(avgWin, 2),
            AverageLossPercent: Math.Round(avgLoss, 2),
            LargestWinDollars: Math.Round(largestWin, 2),
            LargestLossDollars: Math.Round(largestLoss, 2)
        );

        return Result.Success(new BacktestResult(
            request,
            metrics,
            trades,
            equityCurve
        ));
    }
}
