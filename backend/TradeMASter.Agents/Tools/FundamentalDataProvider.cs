namespace TradeMASter.Agents.Tools;

public record FundamentalDataSnapshot(
    string Symbol,
    string CompanyName,
    decimal PeRatio,
    decimal ForwardPe,
    decimal EvToEbitda,
    decimal RevenueGrowthYoyPercent,
    decimal ProfitMarginPercent,
    decimal DebtToEquityRatio,
    decimal FreeCashFlowYieldPercent,
    string MacroInterestRateImpact,
    string ValuationAssessment,
    decimal HealthScore,
    bool IsSynthetic,
    string DataQuality,
    DateTime AsOfUtc,
    IReadOnlyList<string> Sources);

public static class FundamentalDataProvider
{
    private static readonly Dictionary<string, FundamentalDataSnapshot> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVDA"] = Synthetic("NVDA", "NVIDIA Corporation", 48.2m, 32.5m, 38.4m, 122.0m, 55.0m, 0.41m, 3.2m, 86m, "Premium Growth Valuation"),
        ["AAPL"] = Synthetic("AAPL", "Apple Inc.", 33.4m, 29.1m, 24.2m, 6.1m, 26.3m, 1.45m, 4.1m, 73m, "Fair Value Quality"),
        ["MSFT"] = Synthetic("MSFT", "Microsoft Corporation", 36.8m, 31.0m, 22.8m, 15.2m, 35.8m, 0.38m, 3.5m, 84m, "High Quality Moat"),
        ["TSLA"] = Synthetic("TSLA", "Tesla, Inc.", 62.5m, 54.0m, 42.1m, 8.5m, 12.1m, 0.12m, 1.8m, 51m, "Speculative Growth")
    };

    public static FundamentalDataSnapshot GetSnapshot(string symbol)
    {
        var upper = symbol.ToUpperInvariant();
        if (Profiles.TryGetValue(upper, out var snapshot))
        {
            return snapshot;
        }

        var hash = upper.Aggregate(17, (current, character) => unchecked(current * 31 + character)) & int.MaxValue;
        var growth = 3m + hash % 150 / 10m;
        var margin = 5m + hash % 220 / 10m;
        var leverage = 0.3m + hash % 220 / 100m;
        var fcfYield = 1m + hash % 70 / 10m;
        var pe = 12m + hash % 300 / 10m;
        var health = Math.Clamp(45m + growth * 0.7m + margin * 0.5m + fcfYield - leverage * 4m, 20m, 85m);
        return Synthetic(
            Symbol: upper,
            CompanyName: $"{upper} Corp",
            PeRatio: pe,
            ForwardPe: pe * 0.9m,
            EvToEbitda: 14.2m,
            RevenueGrowthYoyPercent: growth,
            ProfitMarginPercent: margin,
            DebtToEquityRatio: leverage,
            FreeCashFlowYieldPercent: fcfYield,
            HealthScore: health,
            ValuationAssessment: "Synthetic mock scenario");
    }

    private static FundamentalDataSnapshot Synthetic(
        string Symbol,
        string CompanyName,
        decimal PeRatio,
        decimal ForwardPe,
        decimal EvToEbitda,
        decimal RevenueGrowthYoyPercent,
        decimal ProfitMarginPercent,
        decimal DebtToEquityRatio,
        decimal FreeCashFlowYieldPercent,
        decimal HealthScore,
        string ValuationAssessment) =>
        new(Symbol, CompanyName, PeRatio, ForwardPe, EvToEbitda, RevenueGrowthYoyPercent,
            ProfitMarginPercent, DebtToEquityRatio, FreeCashFlowYieldPercent,
            "Synthetic scenario; not an observed company metric", ValuationAssessment,
            Math.Round(HealthScore, 1), true, "Synthetic mock data", DateTime.UtcNow,
            ["TradeMASter deterministic mock scenario"]);
}
