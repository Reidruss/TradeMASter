using TradeMASter.Agents.LLM;
using TradeMASter.Agents.Personas;
using TradeMASter.Agents.Research;
using TradeMASter.Agents.Tools;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Core.ValueObjects;
using TradeMASter.Infrastructure.MarketData;

namespace TradeMASter.Agents.Orchestration;

public sealed class MarketIntelligenceOrchestrator(
    IMarketUniverseProvider universeProvider,
    IMarketDataService marketData,
    IRobinhoodService robinhoodService,
    ILivePortfolioPolicyService livePortfolioPolicyService,
    ITradePlanService tradePlanService,
    ILlmClient llmClient,
    SimulatedLlmClient simulatedLlmClient,
    SecFundamentalResearchService fundamentalDataService,
    MacroRegimeObserver macroObserver,
    FundamentalAnalyst fundamentalResearcher,
    TechnicalAnalyst technicalStrategist,
    SentimentAnalyst sentimentScout,
    AssetSelectionCandidateScreener candidateScreener,
    CandidateApprovalGate candidateApprovalGate,
    QuantitativeAllocator allocator,
    RiskComplianceAuditor riskAuditor,
    ExecutionRebalancingManager executionManager,
    PostMortemReflectionAgent reflectionAgent) : IMarketIntelligenceService
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);
    private static MarketIntelligenceRun? _latestRun;

    public Task<Result<MarketIntelligenceRun?>> GetLatestRunAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<MarketIntelligenceRun?>(_latestRun));

    public async Task<Result<MarketIntelligenceRun>> RunMarketScanAsync(
        MarketScanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RunLock.WaitAsync(0, cancellationToken))
            return Result.Failure<MarketIntelligenceRun>("A full-market intelligence run is already in progress.");

        try
        {
            request = request with
            {
                DeepAnalysisCount = Math.Clamp(request.DeepAnalysisCount, 3, 20),
                MaxSingleAssetPercent = Math.Clamp(request.MaxSingleAssetPercent, 1m, 20m),
                MaxSectorPercent = Math.Clamp(request.MaxSectorPercent, 5m, 40m),
                MaxTurnoverPercent = Math.Clamp(request.MaxTurnoverPercent, 1m, 25m),
                MockPortfolioEquity = Math.Clamp(request.MockPortfolioEquity, 100m, 10_000_000m),
                MinimumFundamentalHealthScore = Math.Clamp(request.MinimumFundamentalHealthScore, 0m, 100m),
                MaxCandidateVolatilityPercent = Math.Clamp(request.MaxCandidateVolatilityPercent, 20m, 150m),
                MaxProjectedPortfolioVolatilityPercent = Math.Clamp(request.MaxProjectedPortfolioVolatilityPercent, 5m, 100m),
                MaxDailyVaR95Percent = Math.Clamp(request.MaxDailyVaR95Percent, 0.5m, 10m)
            };
            var startedAt = DateTime.UtcNow;
            Portfolio portfolio;
            LivePortfolioPolicySnapshot? livePolicy = null;
            if (request.IsMockRun)
            {
                portfolio = new Portfolio("Mock Market Analysis", request.MockPortfolioEquity);
            }
            else
            {
                var accountStatus = await robinhoodService.GetAccountStatusAsync(cancellationToken);
                if (accountStatus.IsFailure || !accountStatus.Value.IsConnected)
                    return Result.Failure<MarketIntelligenceRun>("Connect the Robinhood Agentic account before running a live market-wide allocation review, or select Mock Run.");
                if (!accountStatus.Value.IsDemoMode && llmClient.ProviderName != "OpenAI")
                    return Result.Failure<MarketIntelligenceRun>(
                        "Live market-wide research requires OPENAI_API_KEY for current news and sentiment verification. SEC fundamentals and quantitative signals remain deterministic. Mock Run works without it.");

                livePolicy = await livePortfolioPolicyService.GetAsync(cancellationToken);
                request = request with
                {
                    MaxSingleAssetPercent = Math.Min(request.MaxSingleAssetPercent, livePolicy.MaxPositionPercent),
                    MaxSectorPercent = Math.Min(request.MaxSectorPercent, livePolicy.MaxSectorPercent),
                    MaxTurnoverPercent = Math.Min(request.MaxTurnoverPercent, livePolicy.MaxDailyTurnoverPercent),
                    MaxProjectedPortfolioVolatilityPercent = Math.Min(
                        request.MaxProjectedPortfolioVolatilityPercent,
                        livePolicy.MaxAnnualizedVolatilityPercent),
                    MaxDailyVaR95Percent = Math.Min(request.MaxDailyVaR95Percent, livePolicy.MaxDailyVaR95Percent)
                };

                var sync = await robinhoodService.SyncHoldingsToPortfolioAsync(cancellationToken: cancellationToken);
                if (sync.IsFailure) return Result.Failure<MarketIntelligenceRun>(sync.Error!);
                portfolio = sync.Value;
            }

            var universeResult = await universeProvider.ScanAsync(cancellationToken);
            if (universeResult.IsFailure) return Result.Failure<MarketIntelligenceRun>(universeResult.Error!);
            var universe = universeResult.Value;
            var eligibleCount = universe.Securities.Count(asset =>
                asset.LastPrice >= request.MinimumSharePrice
                && asset.MarketCap >= request.MinimumMarketCap
                && asset.Volume >= request.MinimumDailyVolume);
            var screened = candidateScreener.Screen(universe, request);
            if (screened.Count == 0)
                return Result.Failure<MarketIntelligenceRun>("The broad-market liquidity screen returned no eligible operating companies.");

            var macroTask = macroObserver.AnalyzeAsync(cancellationToken);
            var runFundamentalResearcher = request.IsMockRun
                ? new FundamentalAnalyst(simulatedLlmClient)
                : fundamentalResearcher;
            var runTechnicalStrategist = request.IsMockRun
                ? new TechnicalAnalyst(simulatedLlmClient)
                : technicalStrategist;
            var runSentimentScout = request.IsMockRun
                ? new SentimentAnalyst(simulatedLlmClient)
                : sentimentScout;
            var candidateTasks = screened.Select(item => AnalyzeCandidateAsync(
                item.Asset,
                item.Score,
                portfolio,
                runFundamentalResearcher,
                runTechnicalStrategist,
                runSentimentScout,
                request,
                cancellationToken));
            var candidateAssessmentsTask = Task.WhenAll(candidateTasks);
            await Task.WhenAll(macroTask, candidateAssessmentsTask);
            var macro = await macroTask;
            if (livePolicy is not null)
            {
                var policyEquityCeiling = 100m - livePolicy.MinimumCashReservePercent;
                var boundedEquity = Math.Min(macro.TargetEquityPercent, policyEquityCeiling);
                macro = macro with
                {
                    TargetEquityPercent = boundedEquity,
                    TargetCashPercent = 100m - boundedEquity,
                    Rationale = macro.Rationale
                        + $" Persisted live policy v{livePolicy.PolicyVersion} requires at least {livePolicy.MinimumCashReservePercent:F1}% cash."
                };
            }
            var analyses = await candidateAssessmentsTask;
            var candidates = analyses.Select(item => item.Assessment)
                .OrderByDescending(candidate => candidate.CompositeConvictionScore)
                .ToList();

            var returnSeries = analyses.ToDictionary(
                item => item.Assessment.Symbol,
                item => item.DailyReturns,
                StringComparer.OrdinalIgnoreCase);
            var sectorLookup = universe.Securities
                .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Sector, StringComparer.OrdinalIgnoreCase);
            var allocations = allocator.Allocate(candidates, portfolio, macro, request, returnSeries, sectorLookup);
            var candidateVolatility = candidates.ToDictionary(
                item => item.Symbol,
                item => item.AnnualizedVolatilityPercent,
                StringComparer.OrdinalIgnoreCase);
            var review = riskAuditor.Review(allocations, portfolio, request, returnSeries, candidateVolatility, livePolicy);
            if (livePolicy?.EmergencyHaltActive == true
                && allocations.Any(item => item.WeightDeltaPercent > 0.01m))
            {
                review = review with
                {
                    IsApproved = false,
                    Feedback = $"Risk & Compliance Auditor: emergency halt blocks new exposure. {livePolicy.EmergencyHaltReason}"
                };
            }
            var orders = executionManager.BuildPaperOrders(allocations, portfolio, review.IsApproved, livePolicy);
            var reflection = await reflectionAgent.ReflectAndPersistAsync(
                portfolio,
                candidates.Count(candidate => candidate.IsApproved),
                review.TurnoverPercent,
                review.IsApproved,
                request.IsMockRun,
                cancellationToken);
            var targetEquity = allocations.Sum(item => item.TargetWeightPercent);

            var run = new MarketIntelligenceRun(
                Guid.NewGuid(),
                request.IsMockRun,
                startedAt,
                DateTime.UtcNow,
                universe.TotalSecuritiesScanned,
                eligibleCount,
                macro,
                candidates,
                allocations,
                Math.Round(100m - targetEquity, 2),
                review.TurnoverPercent,
                review.ProjectedAnnualizedVolatilityPercent,
                review.ParametricDailyVaR95Percent,
                review.IsApproved,
                review.Feedback,
                orders,
                reflection.Summary,
                reflection.Metrics,
                $"Market-wide discovery: {universe.Source} ({universe.TotalSecuritiesScanned:N0} listings). "
                    + (request.IsMockRun
                        ? $"Mock analysis: Yahoo Finance price history plus explicitly synthetic deterministic fundamentals and sentiment over {screened.Count} finalists using a synthetic ${portfolio.TotalEquity:N2} all-cash portfolio. No Robinhood, SEC, or OpenAI calls were made. "
                        : $"Live analysis: Yahoo Finance price history, verified SEC Company Facts fundamentals, and current OpenAI-assisted news research over {screened.Count} finalists. Robinhood MCP supplies account balances and holdings. ")
                    + "Allocation uses covariance-aware hierarchical risk parity with conviction tilts, exposure caps, turnover budgeting, and portfolio volatility/VaR circuit breakers. "
                    + (livePolicy is null ? string.Empty : $"Persisted live policy v{livePolicy.PolicyVersion} bounded all user-requested limits. ")
                    + "Proposed limit orders remain paper-only.");
            if (!request.IsMockRun && run.IsRiskApproved && run.ProposedPaperOrders.Count > 0)
            {
                var tradePlanResult = await tradePlanService.CreateFromMarketRunAsync(run, portfolio, cancellationToken);
                if (tradePlanResult.IsFailure)
                    return Result.Failure<MarketIntelligenceRun>($"Analysis completed, but immutable plan creation failed: {tradePlanResult.Error}");
                if (tradePlanResult.Value is { } tradePlan)
                {
                    run = run with
                    {
                        TradePlanId = tradePlan.Id,
                        TradePlanHash = tradePlan.PlanHash,
                        TradePlanStatus = tradePlan.Status,
                        TradePlanExpiresAtUtc = tradePlan.ExpiresAtUtc
                    };
                }
            }
            _latestRun = run;
            return Result.Success(run);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<MarketIntelligenceRun>("The market scan was cancelled.");
        }
        catch (Exception ex)
        {
            return Result.Failure<MarketIntelligenceRun>($"Market intelligence pipeline failed: {ex.Message}");
        }
        finally
        {
            RunLock.Release();
        }
    }

    private sealed record CandidateAnalysis(
        MarketCandidateAssessment Assessment,
        IReadOnlyList<decimal> DailyReturns);

    private async Task<CandidateAnalysis> AnalyzeCandidateAsync(
        MarketUniverseAsset asset,
        decimal screenScore,
        Portfolio portfolio,
        FundamentalAnalyst runFundamentalResearcher,
        TechnicalAnalyst runTechnicalStrategist,
        SentimentAnalyst runSentimentScout,
        MarketScanRequest request,
        CancellationToken cancellationToken)
    {
        var candlesResult = await marketData.GetCandlesAsync(asset.Symbol, TimeFrame.OneDay, 252, cancellationToken);
        var candles = candlesResult.IsSuccess ? candlesResult.Value : [];
        var indicators = TechnicalIndicatorCalculator.Calculate(candles);
        var quote = new PriceTick(
            asset.Symbol,
            asset.LastPrice,
            asset.Volume,
            DateTime.UtcNow,
            asset.LastPrice,
            asset.LastPrice,
            asset.LastPrice * asset.ChangePercent / 100m,
            asset.ChangePercent);
        var fundamentals = await fundamentalDataService.GetAsync(
            asset.Symbol, asset.MarketCap, request.IsMockRun, cancellationToken);
        var context = new MarketAnalysisContext(
            asset.Symbol,
            quote,
            candles,
            indicators,
            fundamentals,
            SentimentEvaluator.Evaluate(asset.Symbol),
            portfolio,
            portfolio.RiskConfig);

        var fundamentalTask = runFundamentalResearcher.AnalyzeAsync(context, cancellationToken);
        var technicalTask = runTechnicalStrategist.AnalyzeAsync(context, cancellationToken);
        var sentimentTask = runSentimentScout.AnalyzeAsync(context, cancellationToken);
        await Task.WhenAll(fundamentalTask, technicalTask, sentimentTask);
        var fundamental = await fundamentalTask;
        var technical = await technicalTask;
        var sentiment = await sentimentTask;

        var fundamentalScore = fundamentals.HealthScore;
        var technicalScore = DecisionScore(technical);
        var sentimentScore = DecisionScore(sentiment);
        var conviction = fundamentalScore * 0.40m + technicalScore * 0.35m + sentimentScore * 0.25m;
        var volatility = AnnualizedVolatility(candles);
        var gate = candidateApprovalGate.Evaluate(
            conviction,
            fundamentalScore,
            volatility,
            candles.Count,
            sentiment.Direction,
            sentiment.ConfidenceScore,
            !fundamentals.IsSynthetic,
            request);
        var direction = conviction >= 70m ? SignalDirection.StrongBuy
            : conviction >= 55m ? SignalDirection.Bullish
            : conviction < 35m ? SignalDirection.Bearish
            : SignalDirection.Neutral;
        var atrStop = Math.Max(0.01m, asset.LastPrice - Math.Max(indicators.Atr14 * 1.5m, asset.LastPrice * 0.025m));

        var assessment = new MarketCandidateAssessment(
            asset.Symbol,
            asset.Name,
            asset.Sector,
            asset.LastPrice,
            asset.MarketCap,
            asset.Volume,
            screenScore,
            Math.Round(fundamentalScore, 1),
            Math.Round(technicalScore, 1),
            Math.Round(sentimentScore, 1),
            Math.Round(conviction, 1),
            Math.Round(volatility, 1),
            Math.Round(atrStop, 2),
            direction,
            gate.IsApproved,
            $"Fundamental Researcher: {fundamental.Reasoning} Technical Strategist: {technical.Reasoning} Sentiment Scout: {sentiment.Reasoning}",
            gate.RiskFlags,
            !fundamentals.IsSynthetic,
            fundamentals.DataQuality,
            fundamentals.Sources);
        var dailyReturns = candles.Zip(candles.Skip(1), (previous, current) =>
                previous.Close > 0 ? (current.Close - previous.Close) / previous.Close : 0m)
            .ToList();
        return new CandidateAnalysis(assessment, dailyReturns);
    }

    private static decimal DecisionScore(AgentDecision decision)
    {
        var directional = decision.Direction switch
        {
            SignalDirection.StrongBuy => 1m,
            SignalDirection.Bullish => 0.6m,
            SignalDirection.Bearish => -0.6m,
            SignalDirection.StrongSell => -1m,
            _ => 0m
        };
        return Math.Clamp(50m + directional * (decimal)decision.ConfidenceScore * 50m, 0m, 100m);
    }

    private static decimal AnnualizedVolatility(IReadOnlyList<Candle> candles)
    {
        var returns = candles.Zip(candles.Skip(1), (previous, current) =>
                previous.Close > 0 ? (double)((current.Close - previous.Close) / previous.Close) : 0d)
            .ToList();
        if (returns.Count < 2) return 50m;
        var average = returns.Average();
        var variance = returns.Sum(value => Math.Pow(value - average, 2)) / (returns.Count - 1);
        return (decimal)(Math.Sqrt(variance) * Math.Sqrt(252d) * 100d);
    }
}
