using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Agents.Personas;

public sealed class MacroRegimeObserver(IMarketDataService marketData)
{
    public string PersonaName => "Macro Regime Observer";

    public async Task<MacroRegimeAssessment> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var vixTask = marketData.GetQuoteAsync("^VIX", cancellationToken);
        var yieldTask = marketData.GetQuoteAsync("^TNX", cancellationToken);
        var spyTask = marketData.GetQuoteAsync("SPY", cancellationToken);
        await Task.WhenAll(vixTask, yieldTask, spyTask);

        var vix = vixTask.Result.IsSuccess ? vixTask.Result.Value.Price : 20m;
        var tenYearYield = yieldTask.Result.IsSuccess ? yieldTask.Result.Value.Price : 4m;
        var spyChange = spyTask.Result.IsSuccess ? spyTask.Result.Value.ChangePercent24h : 0m;

        var regime = "Defensive";
        decimal equity = 50m;
        var risks = new List<string>();
        if (vix >= 30m)
        {
            regime = "Risk-Off";
            equity = 25m;
            risks.Add($"VIX is elevated at {vix:F1}.");
        }
        else if (tenYearYield >= 4.75m && spyChange < 0m)
        {
            regime = "Stagflation";
            equity = 35m;
            risks.Add($"10-year yield is {tenYearYield:F2}% while broad-equity momentum is negative.");
        }
        else if (vix < 20m && spyChange >= 0m)
        {
            regime = "Risk-On";
            equity = 75m;
        }
        else
        {
            risks.Add("Volatility or broad-market momentum does not support maximum equity exposure.");
        }

        return new MacroRegimeAssessment(
            regime,
            equity,
            100m - equity,
            Math.Round(vix, 2),
            Math.Round(tenYearYield, 2),
            $"{PersonaName}: VIX {vix:F1}, 10-year yield {tenYearYield:F2}%, and SPY daily change {spyChange:F2}% imply a {regime} regime.",
            risks);
    }
}

public sealed class AssetSelectionCandidateScreener
{
    public string PersonaName => "Asset Selection & Candidate Screener";

    public IReadOnlyList<(MarketUniverseAsset Asset, decimal Score)> Screen(
        MarketUniverseSnapshot universe,
        MarketScanRequest request)
    {
        static bool IsOperatingSecurity(MarketUniverseAsset asset)
        {
            var name = asset.Name.ToUpperInvariant();
            return !name.Contains(" WARRANT")
                && !name.Contains(" RIGHTS")
                && !name.Contains(" UNITS")
                && !name.Contains(" ACQUISITION")
                && !name.Contains(" DEPOSITARY SHARE")
                && !string.IsNullOrWhiteSpace(asset.Sector);
        }

        var eligible = universe.Securities
            .Where(asset => asset.LastPrice >= request.MinimumSharePrice
                && asset.MarketCap >= request.MinimumMarketCap
                && asset.Volume >= request.MinimumDailyVolume
                && IsOperatingSecurity(asset))
            .Select(asset =>
            {
                var sizeScale = (decimal)Math.Log10((double)Math.Max(asset.MarketCap / 500_000_000m, 1m));
                var dollarVolume = Math.Max(asset.LastPrice * asset.Volume, 1m);
                var liquidityScale = (decimal)Math.Log10((double)Math.Max(dollarVolume / 5_000_000m, 1m));
                var sizeScore = Math.Clamp(sizeScale / 3m * 40m, 0m, 40m);
                var liquidityScore = Math.Clamp(liquidityScale / 2.3m * 35m, 0m, 35m);
                var stabilityScore = 25m - Math.Clamp(Math.Abs(asset.ChangePercent) * 2m, 0m, 25m);
                return (Asset: asset, Score: Math.Round(sizeScore + liquidityScore + stabilityScore, 2));
            })
            .OrderByDescending(item => item.Score)
            .ToList();

        var sectorCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var diversified = new List<(MarketUniverseAsset, decimal)>();
        var maxPerSector = Math.Max(1, request.DeepAnalysisCount / 3);
        foreach (var item in eligible)
        {
            var sector = string.IsNullOrWhiteSpace(item.Asset.Sector) ? "Unknown" : item.Asset.Sector;
            sectorCounts.TryGetValue(sector, out var count);
            if (count >= maxPerSector) continue;
            diversified.Add(item);
            sectorCounts[sector] = count + 1;
            if (diversified.Count >= request.DeepAnalysisCount) break;
        }
        return diversified;
    }
}

public record CandidateGateResult(bool IsApproved, IReadOnlyList<string> RiskFlags);

public sealed class CandidateApprovalGate
{
    public CandidateGateResult Evaluate(
        decimal conviction,
        decimal fundamentalHealth,
        decimal annualizedVolatility,
        int priceObservationCount,
        SignalDirection sentimentDirection,
        double sentimentConfidence,
        bool hasVerifiedFundamentals,
        MarketScanRequest request)
    {
        var riskFlags = new List<string>();
        if (sentimentDirection is SignalDirection.Bearish or SignalDirection.StrongSell
            && sentimentConfidence >= 0.7)
            riskFlags.Add("Sentiment Scout detected a high-confidence deteriorating news cycle.");
        if (annualizedVolatility > 80m)
            riskFlags.Add($"Annualized volatility is elevated at {annualizedVolatility:F1}%.");
        if (priceObservationCount < 60)
            riskFlags.Add("Less than 60 daily observations were available.");
        if (!request.IsMockRun && !hasVerifiedFundamentals)
            riskFlags.Add("Verified SEC fundamentals were unavailable; live allocation is blocked for this candidate.");
        if (fundamentalHealth < request.MinimumFundamentalHealthScore)
            riskFlags.Add($"Fundamental health {fundamentalHealth:F1} is below the {request.MinimumFundamentalHealthScore:F1} minimum.");
        if (conviction < 55m)
            riskFlags.Add($"Composite conviction {conviction:F1} is below the 55.0 allocation minimum.");
        if (annualizedVolatility > request.MaxCandidateVolatilityPercent)
            riskFlags.Add($"Volatility breaches the {request.MaxCandidateVolatilityPercent:F1}% candidate limit.");

        var approved = conviction >= 55m
            && fundamentalHealth >= request.MinimumFundamentalHealthScore
            && annualizedVolatility <= request.MaxCandidateVolatilityPercent
            && (request.IsMockRun || hasVerifiedFundamentals)
            && !riskFlags.Any(flag => flag.Contains("deteriorating", StringComparison.OrdinalIgnoreCase));
        return new CandidateGateResult(approved, riskFlags);
    }
}

public sealed class QuantitativeAllocator
{
    public string PersonaName => "Quantitative Allocator";

    public IReadOnlyList<TargetAllocation> Allocate(
        IReadOnlyList<MarketCandidateAssessment> candidates,
        Portfolio portfolio,
        MacroRegimeAssessment macro,
        MarketScanRequest request,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>>? dailyReturns = null,
        IReadOnlyDictionary<string, string>? sectorLookup = null)
    {
        var approved = candidates.Where(candidate => candidate.IsApproved && candidate.LastPrice > 0).ToList();
        if (approved.Count == 0 || portfolio.TotalEquity <= 0) return [];

        // Hierarchical risk parity uses the empirical covariance structure, then conviction
        // tilts the risk-balanced result. This avoids treating highly correlated names as
        // independent bets and does not require unstable expected-return point estimates.
        var covariance = CovarianceMatrix(approved, dailyReturns);
        var hrp = HierarchicalRiskParity(covariance);
        var tilted = hrp.Select((weight, index) =>
            weight * (decimal)Math.Sqrt((double)Math.Max(1m, approved[index].CompositeConvictionScore - 50m))).ToArray();
        var tiltedTotal = tilted.Sum();
        if (tiltedTotal <= 0m) return [];
        var normalized = tilted.Select(value => value / tiltedTotal).ToArray();
        var desiredCandidateWeights = ApplyCaps(approved, normalized, macro.TargetEquityPercent, request);

        var currentWeights = portfolio.Positions
            .Where(position => position.CurrentMarketValue > 0)
            .ToDictionary(
                position => position.Symbol,
                position => position.CurrentMarketValue / portfolio.TotalEquity * 100m,
                StringComparer.OrdinalIgnoreCase);
        var desiredWeights = desiredCandidateWeights.ToDictionary(
            item => item.Key.Symbol, item => item.Value, StringComparer.OrdinalIgnoreCase);
        var allSymbols = currentWeights.Keys.Concat(desiredWeights.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unconstrainedTurnover = allSymbols.Sum(symbol => Math.Abs(
            desiredWeights.GetValueOrDefault(symbol) - currentWeights.GetValueOrDefault(symbol)));
        var transitionScale = unconstrainedTurnover > 0m
            ? Math.Min(1m, request.MaxTurnoverPercent * 0.999m / unconstrainedTurnover)
            : 1m;

        var candidatesBySymbol = approved.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var positionsBySymbol = portfolio.Positions.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
        var allocations = new List<TargetAllocation>();
        foreach (var symbol in allSymbols)
        {
            var currentWeight = currentWeights.GetValueOrDefault(symbol);
            var desiredWeight = desiredWeights.GetValueOrDefault(symbol);
            var targetWeight = currentWeight + transitionScale * (desiredWeight - currentWeight);
            var delta = targetWeight - currentWeight;
            // Keep unchanged invested positions in the allocation ledger so the risk
            // auditor measures the entire target portfolio, not only proposed trades.
            if (targetWeight < 0.01m && currentWeight < 0.01m) continue;
            candidatesBySymbol.TryGetValue(symbol, out var candidate);
            positionsBySymbol.TryGetValue(symbol, out var position);
            var price = candidate?.LastPrice ?? position?.CurrentPrice ?? 0m;
            if (price <= 0m) continue;
            var targetValue = portfolio.TotalEquity * targetWeight / 100m;
            var sector = candidate?.Sector
                ?? (sectorLookup?.GetValueOrDefault(symbol) ?? "Unclassified");
            allocations.Add(new TargetAllocation(
                symbol,
                sector,
                Math.Round(targetWeight, 2),
                Math.Round(targetValue, 2),
                Math.Round(currentWeight, 2),
                Math.Round(delta, 2),
                Math.Floor(targetValue / price * 10_000m) / 10_000m,
                candidate?.AtrStopLossPrice ?? 0m));
        }
        return allocations.OrderByDescending(item => Math.Abs(item.WeightDeltaPercent)).ToList();
    }

    private static IReadOnlyDictionary<MarketCandidateAssessment, decimal> ApplyCaps(
        IReadOnlyList<MarketCandidateAssessment> candidates,
        IReadOnlyList<decimal> normalizedWeights,
        decimal totalEquityWeight,
        MarketScanRequest request)
    {
        var result = candidates.ToDictionary(item => item, _ => 0m);
        var sectorUsed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var active = Enumerable.Range(0, candidates.Count).ToHashSet();
        var remaining = totalEquityWeight;
        for (var pass = 0; pass < candidates.Count + 2 && active.Count > 0 && remaining > 0.001m; pass++)
        {
            var activeTotal = active.Sum(index => normalizedWeights[index]);
            if (activeTotal <= 0m) break;
            var distributed = 0m;
            var capped = new List<int>();
            foreach (var index in active)
            {
                var candidate = candidates[index];
                sectorUsed.TryGetValue(candidate.Sector, out var sectorWeight);
                var capacity = Math.Min(
                    request.MaxSingleAssetPercent - result[candidate],
                    request.MaxSectorPercent - sectorWeight);
                var proposed = remaining * normalizedWeights[index] / activeTotal;
                var added = Math.Max(0m, Math.Min(proposed, capacity));
                result[candidate] += added;
                sectorUsed[candidate.Sector] = sectorWeight + added;
                distributed += added;
                if (capacity - added <= 0.001m) capped.Add(index);
            }
            remaining -= distributed;
            foreach (var index in capped) active.Remove(index);
            if (capped.Count == 0) break;
        }
        return result;
    }

    private static decimal[,] CovarianceMatrix(
        IReadOnlyList<MarketCandidateAssessment> candidates,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>>? returnSeries)
    {
        var count = candidates.Count;
        var covariance = new decimal[count, count];
        for (var left = 0; left < count; left++)
        {
            for (var right = left; right < count; right++)
            {
                var leftReturns = returnSeries?.GetValueOrDefault(candidates[left].Symbol) ?? [];
                var rightReturns = returnSeries?.GetValueOrDefault(candidates[right].Symbol) ?? [];
                var observations = Math.Min(leftReturns.Count, rightReturns.Count);
                decimal value;
                if (observations >= 30)
                {
                    var leftSlice = leftReturns.TakeLast(observations).ToArray();
                    var rightSlice = rightReturns.TakeLast(observations).ToArray();
                    var leftMean = leftSlice.Average();
                    var rightMean = rightSlice.Average();
                    value = leftSlice.Zip(rightSlice, (a, b) => (a - leftMean) * (b - rightMean)).Sum()
                        / (observations - 1) * 252m;
                    if (left != right) value *= 0.65m; // shrink noisy sample correlation toward zero
                }
                else
                {
                    value = left == right
                        ? (decimal)Math.Pow((double)(candidates[left].AnnualizedVolatilityPercent / 100m), 2)
                        : 0m;
                }
                covariance[left, right] = value;
                covariance[right, left] = value;
            }
            covariance[left, left] = Math.Max(covariance[left, left], 0.0001m);
        }
        return covariance;
    }

    private static decimal[] HierarchicalRiskParity(decimal[,] covariance)
    {
        var count = covariance.GetLength(0);
        if (count == 1) return [1m];
        var clusters = Enumerable.Range(0, count).Select(index => new List<int> { index }).ToList();
        while (clusters.Count > 1)
        {
            var bestLeft = 0;
            var bestRight = 1;
            var bestDistance = decimal.MaxValue;
            for (var left = 0; left < clusters.Count; left++)
            for (var right = left + 1; right < clusters.Count; right++)
            {
                var distance = clusters[left].SelectMany(_ => clusters[right], (a, b) => CorrelationDistance(covariance, a, b)).Min();
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestLeft = left;
                bestRight = right;
            }
            clusters[bestLeft].AddRange(clusters[bestRight]);
            clusters.RemoveAt(bestRight);
        }

        var weights = Enumerable.Repeat(1m, count).ToArray();
        var queue = new Queue<List<int>>();
        queue.Enqueue(clusters[0]);
        while (queue.Count > 0)
        {
            var cluster = queue.Dequeue();
            if (cluster.Count <= 1) continue;
            var midpoint = cluster.Count / 2;
            var left = cluster.Take(midpoint).ToList();
            var right = cluster.Skip(midpoint).ToList();
            var leftVariance = ClusterVariance(covariance, left);
            var rightVariance = ClusterVariance(covariance, right);
            var alpha = leftVariance + rightVariance > 0m
                ? rightVariance / (leftVariance + rightVariance)
                : 0.5m;
            foreach (var index in left) weights[index] *= alpha;
            foreach (var index in right) weights[index] *= 1m - alpha;
            queue.Enqueue(left);
            queue.Enqueue(right);
        }
        var total = weights.Sum();
        return weights.Select(weight => weight / total).ToArray();
    }

    private static decimal CorrelationDistance(decimal[,] covariance, int left, int right)
    {
        var denominator = (decimal)Math.Sqrt((double)(covariance[left, left] * covariance[right, right]));
        var correlation = denominator > 0m ? Math.Clamp(covariance[left, right] / denominator, -1m, 1m) : 0m;
        return (decimal)Math.Sqrt((double)((1m - correlation) / 2m));
    }

    private static decimal ClusterVariance(decimal[,] covariance, IReadOnlyList<int> cluster)
    {
        var inverse = cluster.Select(index => 1m / Math.Max(covariance[index, index], 0.0001m)).ToArray();
        var inverseTotal = inverse.Sum();
        var weights = inverse.Select(value => value / inverseTotal).ToArray();
        decimal variance = 0m;
        for (var left = 0; left < cluster.Count; left++)
        for (var right = 0; right < cluster.Count; right++)
            variance += weights[left] * weights[right] * covariance[cluster[left], cluster[right]];
        return Math.Max(variance, 0.000001m);
    }
}

public record PortfolioRiskReview(
    bool IsApproved,
    decimal TurnoverPercent,
    decimal ProjectedAnnualizedVolatilityPercent,
    decimal ParametricDailyVaR95Percent,
    string Feedback);

public sealed class RiskComplianceAuditor
{
    public string PersonaName => "Risk & Compliance Auditor";

    public PortfolioRiskReview Review(
        IReadOnlyList<TargetAllocation> allocations,
        Portfolio portfolio,
        MarketScanRequest request,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>>? returnSeries = null,
        IReadOnlyDictionary<string, decimal>? fallbackVolatilityPercent = null,
        LivePortfolioPolicySnapshot? livePolicy = null)
    {
        var violations = new List<string>();
        foreach (var allocation in allocations.Where(item =>
            item.TargetWeightPercent > request.MaxSingleAssetPercent
            && item.TargetWeightPercent >= item.CurrentWeightPercent - 0.01m))
            violations.Add($"{allocation.Symbol} exceeds the {request.MaxSingleAssetPercent:F1}% single-asset cap without reducing the exposure.");
        foreach (var sector in allocations.GroupBy(item => item.Sector))
        {
            var weight = sector.Sum(item => item.TargetWeightPercent);
            var currentWeight = sector.Sum(item => item.CurrentWeightPercent);
            if (weight > request.MaxSectorPercent && weight >= currentWeight - 0.01m)
                violations.Add($"{sector.Key} reaches {weight:F1}%, above the {request.MaxSectorPercent:F1}% sector cap without reducing the exposure.");
        }

        var targetEquity = allocations.Sum(item => item.TargetWeightPercent);
        if (targetEquity > 100m + 0.01m)
            violations.Add($"Target equity exposure {targetEquity:F1}% exceeds total portfolio equity.");
        var drawdownLimit = Math.Min(
            portfolio.RiskConfig.MaxPortfolioDrawdownPercent,
            livePolicy?.MaxDrawdownPercent ?? portfolio.RiskConfig.MaxPortfolioDrawdownPercent);
        if (portfolio.TotalPnLPercent <= -drawdownLimit
            && allocations.Any(item => item.WeightDeltaPercent > 0.01m))
            violations.Add($"Portfolio drawdown {portfolio.TotalPnLPercent:F1}% breached the {drawdownLimit:F1}% circuit breaker; new exposure is blocked, but reduction-only trades remain eligible.");
        foreach (var allocation in allocations.Where(item => item.WeightDeltaPercent > 0m
            && (item.StopLossPrice <= 0m || item.EstimatedQuantity <= 0m)))
            violations.Add($"{allocation.Symbol} has no valid volatility stop or target quantity.");

        var turnover = allocations.Sum(item => Math.Abs(item.WeightDeltaPercent));
        if (turnover > request.MaxTurnoverPercent + 0.01m)
            violations.Add($"Estimated turnover {turnover:F1}% exceeds the {request.MaxTurnoverPercent:F1}% circuit breaker.");
        var projectedVolatility = ProjectedVolatility(allocations, returnSeries, fallbackVolatilityPercent);
        var dailyVar95 = 1.645m * projectedVolatility / (decimal)Math.Sqrt(252d);
        if (projectedVolatility > request.MaxProjectedPortfolioVolatilityPercent)
            violations.Add($"Projected annualized volatility {projectedVolatility:F1}% exceeds the {request.MaxProjectedPortfolioVolatilityPercent:F1}% limit.");
        if (dailyVar95 > request.MaxDailyVaR95Percent)
            violations.Add($"Parametric one-day 95% VaR {dailyVar95:F2}% exceeds the {request.MaxDailyVaR95Percent:F2}% limit.");
        return new PortfolioRiskReview(
            violations.Count == 0,
            Math.Round(turnover, 2),
            Math.Round(projectedVolatility, 2),
            Math.Round(dailyVar95, 2),
            violations.Count == 0
                ? $"{PersonaName}: approved phased allocation within asset, sector, turnover, projected volatility, and VaR limits."
                : $"{PersonaName}: rejected. {string.Join(" ", violations)} Recalculate before execution.");
    }

    private static decimal ProjectedVolatility(
        IReadOnlyList<TargetAllocation> allocations,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>>? returnSeries,
        IReadOnlyDictionary<string, decimal>? fallbackVolatilityPercent)
    {
        var invested = allocations.Where(item => item.TargetWeightPercent > 0.001m).ToList();
        decimal variance = 0m;
        for (var left = 0; left < invested.Count; left++)
        for (var right = 0; right < invested.Count; right++)
        {
            var covariance = AnnualizedCovariance(
                invested[left].Symbol,
                invested[right].Symbol,
                returnSeries,
                fallbackVolatilityPercent);
            variance += invested[left].TargetWeightPercent / 100m
                * invested[right].TargetWeightPercent / 100m
                * covariance;
        }
        return variance > 0m ? (decimal)Math.Sqrt((double)variance) * 100m : 0m;
    }

    private static decimal AnnualizedCovariance(
        string leftSymbol,
        string rightSymbol,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>>? returnSeries,
        IReadOnlyDictionary<string, decimal>? fallbackVolatilityPercent)
    {
        var leftReturns = returnSeries?.GetValueOrDefault(leftSymbol) ?? [];
        var rightReturns = returnSeries?.GetValueOrDefault(rightSymbol) ?? [];
        var observations = Math.Min(leftReturns.Count, rightReturns.Count);
        if (observations >= 30)
        {
            var leftSlice = leftReturns.TakeLast(observations).ToArray();
            var rightSlice = rightReturns.TakeLast(observations).ToArray();
            var leftMean = leftSlice.Average();
            var rightMean = rightSlice.Average();
            var covariance = leftSlice.Zip(rightSlice, (a, b) => (a - leftMean) * (b - rightMean)).Sum()
                / (observations - 1) * 252m;
            // Apply the same off-diagonal shrinkage used by the allocator.
            return leftSymbol.Equals(rightSymbol, StringComparison.OrdinalIgnoreCase)
                ? Math.Max(covariance, 0.0001m)
                : covariance * 0.65m;
        }

        if (!leftSymbol.Equals(rightSymbol, StringComparison.OrdinalIgnoreCase)) return 0m;
        var fallback = fallbackVolatilityPercent?.GetValueOrDefault(leftSymbol) ?? 35m;
        return fallback / 100m * fallback / 100m;
    }
}

public sealed class ExecutionRebalancingManager
{
    public string PersonaName => "Execution & Rebalancing Manager";

    public IReadOnlyList<OrderRequest> BuildPaperOrders(
        IReadOnlyList<TargetAllocation> allocations,
        Portfolio portfolio,
        bool riskApproved,
        LivePortfolioPolicySnapshot? livePolicy = null)
    {
        if (!riskApproved) return [];
        var orders = new List<OrderRequest>();
        foreach (var allocation in allocations.Where(item => Math.Abs(item.WeightDeltaPercent) >= 0.25m))
        {
            var position = portfolio.Positions.FirstOrDefault(item =>
                item.Symbol.Equals(allocation.Symbol, StringComparison.OrdinalIgnoreCase));
            var targetQuantity = allocation.EstimatedQuantity;
            var currentQuantity = position?.Quantity ?? 0m;
            var delta = Math.Round(targetQuantity - currentQuantity, 4);
            if (Math.Abs(delta) < 0.0001m) continue;
            var referencePrice = targetQuantity > 0m && allocation.TargetValue > 0m
                ? allocation.TargetValue / targetQuantity
                : position?.CurrentPrice ?? 0m;
            if (referencePrice <= 0m || Math.Abs(delta) * referencePrice < 5m) continue;
            var limitPrice = delta > 0m
                ? Math.Round(referencePrice * 1.001m, 2)
                : Math.Round(referencePrice * 0.999m, 2);
            if (livePolicy is not null)
            {
                var maxNotional = Math.Min(
                    livePolicy.MaxOrderNotionalAmount,
                    portfolio.TotalEquity * livePolicy.MaxOrderNotionalPercent / 100m);
                var maxQuantity = maxNotional / limitPrice;
                var absoluteQuantity = Math.Min(Math.Abs(delta), maxQuantity);
                absoluteQuantity = livePolicy.FractionalSharesEnabled
                    ? Math.Floor(absoluteQuantity * 10_000m) / 10_000m
                    : decimal.Floor(absoluteQuantity);
                delta = delta > 0m ? absoluteQuantity : -absoluteQuantity;
                if (Math.Abs(delta) < 0.0001m) continue;
            }
            orders.Add(new OrderRequest(
                portfolio.Id,
                allocation.Symbol,
                delta > 0 ? OrderSide.Buy : OrderSide.Sell,
                OrderType.Limit,
                Math.Abs(delta),
                LimitPrice: limitPrice,
                StopPrice: delta > 0 ? allocation.StopLossPrice : null));
        }
        return orders;
    }
}

public record ReflectionResult(string Summary, PortfolioPerformanceSnapshot Metrics);

public sealed class PostMortemReflectionAgent(TradeMASterDbContext dbContext)
{
    public string PersonaName => "Post-Mortem & Reflection Agent";

    public async Task<ReflectionResult> ReflectAndPersistAsync(
        Portfolio portfolio,
        int candidateCount,
        decimal turnover,
        bool riskApproved,
        bool isMockRun,
        CancellationToken cancellationToken)
    {
        var observationSymbol = isMockRun ? "MARKET-WIDE-MOCK" : "MARKET-WIDE";
        var priorSessions = await dbContext.DeliberationSessions
            .Where(item => item.Symbol == observationSymbol)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new { item.CreatedAt, item.RiskNotes })
            .ToListAsync(cancellationToken);
        var observations = priorSessions.Select(item => TryObservation(item.CreatedAt, item.RiskNotes))
            .Where(item => item is not null).Cast<EquityObservation>().ToList();
        observations.Add(new EquityObservation(DateTime.UtcNow, portfolio.TotalEquity));
        var metrics = CalculateMetrics(observations);
        var summary = $"{PersonaName}: {(isMockRun ? "mock " : string.Empty)}structured observation {metrics.ObservationCount}; equity ${portfolio.TotalEquity:N2}, "
            + $"unrealized P&L ${portfolio.TotalUnrealizedPnL:N2}, {candidateCount} approved candidates, "
            + $"and planned turnover {turnover:F1}%. "
            + (metrics.AnnualizedSharpeRatio.HasValue
                ? $"Observed Sharpe {metrics.AnnualizedSharpeRatio:F2}, max drawdown {metrics.MaxDrawdownPercent:F2}%, and win rate {metrics.WinRatePercent:F1}%."
                : "More distinct equity observations are required for a stable Sharpe estimate.");
        var observationJson = JsonSerializer.Serialize(new
        {
            observedAtUtc = observations[^1].ObservedAtUtc,
            equity = observations[^1].Equity,
            turnover,
            riskApproved
        });
        var session = new DeliberationSession(observationSymbol)
        {
            FinalConsensusSummary = summary,
            FinalVerdict = DecisionVerdict.Hold,
            OverallConfidence = 0.75,
            IsRiskApproved = riskApproved,
            RiskNotes = observationJson
        };
        dbContext.DeliberationSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReflectionResult(summary, metrics);
    }

    private sealed record EquityObservation(DateTime ObservedAtUtc, decimal Equity);

    private static EquityObservation? TryObservation(DateTime fallbackDate, string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{')) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("equity", out var equity) || !equity.TryGetDecimal(out var value)) return null;
            var date = root.TryGetProperty("observedAtUtc", out var observed)
                && observed.TryGetDateTime(out var parsed) ? parsed : fallbackDate;
            return new EquityObservation(date, value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PortfolioPerformanceSnapshot CalculateMetrics(IReadOnlyList<EquityObservation> observations)
    {
        if (observations.Count == 0)
            return new PortfolioPerformanceSnapshot(0, null, 0m, 0m, 0m);
        var ordered = observations.OrderBy(item => item.ObservedAtUtc).ToList();
        var dailyReturns = new List<double>();
        var simpleReturns = new List<decimal>();
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index - 1].Equity <= 0m || ordered[index].Equity <= 0m) continue;
            var simple = ordered[index].Equity / ordered[index - 1].Equity - 1m;
            simpleReturns.Add(simple);
            var days = Math.Max(1d, (ordered[index].ObservedAtUtc - ordered[index - 1].ObservedAtUtc).TotalDays);
            dailyReturns.Add(Math.Log((double)(ordered[index].Equity / ordered[index - 1].Equity)) / days);
        }
        decimal? sharpe = null;
        if (dailyReturns.Count >= 3)
        {
            var mean = dailyReturns.Average();
            var variance = dailyReturns.Sum(value => Math.Pow(value - mean, 2)) / (dailyReturns.Count - 1);
            if (variance > 0d) sharpe = Math.Round((decimal)(mean / Math.Sqrt(variance) * Math.Sqrt(252d)), 2);
        }
        var peak = ordered[0].Equity;
        var maxDrawdown = 0m;
        foreach (var observation in ordered)
        {
            peak = Math.Max(peak, observation.Equity);
            if (peak > 0m) maxDrawdown = Math.Max(maxDrawdown, (peak - observation.Equity) / peak * 100m);
        }
        var winRate = simpleReturns.Count > 0
            ? simpleReturns.Count(value => value > 0m) / (decimal)simpleReturns.Count * 100m : 0m;
        var cumulative = ordered[0].Equity > 0m
            ? (ordered[^1].Equity / ordered[0].Equity - 1m) * 100m : 0m;
        return new PortfolioPerformanceSnapshot(
            ordered.Count,
            sharpe,
            Math.Round(maxDrawdown, 2),
            Math.Round(winRate, 2),
            Math.Round(cumulative, 2));
    }
}
