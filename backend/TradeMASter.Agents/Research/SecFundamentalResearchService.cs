using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradeMASter.Agents.Tools;

namespace TradeMASter.Agents.Research;

public sealed class SecFundamentalResearchService(HttpClient httpClient, IConfiguration configuration)
{
    private sealed record FactPoint(decimal Value, DateTime End, DateTime Filed, int? FiscalYear, string Accession);

    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, (DateTime ExpiresUtc, FundamentalDataSnapshot Snapshot)> Cache = new();
    private static IReadOnlyDictionary<string, long>? _tickerCiks;
    private static DateTime _tickerCiksExpiresUtc;
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    public async Task<FundamentalDataSnapshot> GetAsync(
        string symbol,
        decimal marketCap,
        bool isMockRun,
        CancellationToken cancellationToken)
    {
        if (isMockRun) return FundamentalDataProvider.GetSnapshot(symbol);
        var normalized = symbol.Trim().ToUpperInvariant();
        if (Cache.TryGetValue(normalized, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Snapshot;

        try
        {
            var cikMap = await GetTickerCiksAsync(cancellationToken);
            if (!cikMap.TryGetValue(normalized, out var cik))
                return Unavailable(normalized, "No SEC CIK mapping was found for this listing.");

            using var document = await GetJsonAsync(
                $"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik:0000000000}.json",
                cancellationToken);
            var root = document.RootElement;
            var entityName = root.TryGetProperty("entityName", out var entity) ? entity.GetString() ?? normalized : normalized;
            if (!root.TryGetProperty("facts", out var facts)
                || !facts.TryGetProperty("us-gaap", out var gaap))
                return Unavailable(normalized, "SEC Company Facts contained no US-GAAP facts.");

            var revenues = Annual(gaap,
                "RevenueFromContractWithCustomerExcludingAssessedTax", "Revenues", "SalesRevenueNet");
            var income = Annual(gaap, "NetIncomeLoss", "ProfitLoss");
            var operatingCash = Annual(gaap, "NetCashProvidedByUsedInOperatingActivities");
            var capex = Annual(gaap, "PaymentsToAcquirePropertyPlantAndEquipment");
            var equity = LatestInstant(gaap, "StockholdersEquity", "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest");
            var debt = LatestInstant(gaap, "LongTermDebtAndFinanceLeaseObligations", "LongTermDebt", "LongTermDebtCurrent");
            var liabilities = LatestInstant(gaap, "Liabilities");

            if (revenues.Count == 0 || income.Count == 0 || equity is null)
                return Unavailable(normalized, "Required revenue, income, or equity facts were unavailable from SEC XBRL.");

            var latestRevenue = revenues[0];
            var priorRevenue = revenues.Skip(1).FirstOrDefault();
            var latestIncome = income[0];
            var growth = priorRevenue is not null && priorRevenue.Value != 0
                ? (latestRevenue.Value - priorRevenue.Value) / Math.Abs(priorRevenue.Value) * 100m
                : 0m;
            var margin = latestRevenue.Value != 0 ? latestIncome.Value / latestRevenue.Value * 100m : 0m;
            var leverageNumerator = debt?.Value ?? liabilities?.Value ?? 0m;
            var leverage = equity.Value > 0 ? leverageNumerator / equity.Value : 10m;
            var fcf = (operatingCash.FirstOrDefault()?.Value ?? 0m) - (capex.FirstOrDefault()?.Value ?? 0m);
            var fcfYield = marketCap > 0 ? fcf / marketCap * 100m : 0m;
            var pe = latestIncome.Value > 0 && marketCap > 0 ? marketCap / latestIncome.Value : 0m;
            var health = HealthScore(growth, margin, leverage, fcfYield, pe);
            var latestFiled = new[]
            {
                latestRevenue.Filed,
                latestIncome.Filed,
                equity.Filed,
                operatingCash.FirstOrDefault()?.Filed ?? DateTime.MinValue
            }.Max();
            var accessions = new[]
            {
                latestRevenue.Accession,
                latestIncome.Accession,
                equity.Accession,
                operatingCash.FirstOrDefault()?.Accession
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
            var sources = new List<string>
            {
                $"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik:0000000000}.json"
            };
            sources.AddRange(accessions.Select(accession =>
                $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession!.Replace("-", string.Empty, StringComparison.Ordinal)}/"));

            var snapshot = new FundamentalDataSnapshot(
                normalized,
                entityName,
                Math.Round(pe, 2),
                0m,
                0m,
                Math.Round(growth, 2),
                Math.Round(margin, 2),
                Math.Round(leverage, 2),
                Math.Round(fcfYield, 2),
                "Rate sensitivity is evaluated by the separate macro regime layer.",
                ValuationLabel(pe, fcfYield),
                health,
                false,
                $"Verified SEC XBRL; latest filing {latestFiled:yyyy-MM-dd}",
                latestFiled,
                sources);
            Cache[normalized] = (DateTime.UtcNow.AddHours(12), snapshot);
            return snapshot;
        }
        catch (Exception ex)
        {
            return Unavailable(normalized, $"SEC research failed: {ex.Message}");
        }
    }

    private async Task<IReadOnlyDictionary<string, long>> GetTickerCiksAsync(CancellationToken cancellationToken)
    {
        if (_tickerCiks is not null && _tickerCiksExpiresUtc > DateTime.UtcNow) return _tickerCiks;
        using var document = await GetJsonAsync("https://www.sec.gov/files/company_tickers_exchange.json", cancellationToken);
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
        {
            if (row.GetArrayLength() < 3) continue;
            map[row[2].GetString() ?? string.Empty] = row[0].GetInt64();
        }
        _tickerCiks = map;
        _tickerCiksExpiresUtc = DateTime.UtcNow.AddHours(12);
        return map;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken);
        try
        {
            var remaining = TimeSpan.FromMilliseconds(120) - (DateTime.UtcNow - _lastRequestUtc);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent",
                configuration["Sec:UserAgent"] ?? "TradeMASter/1.0 trademaster-local@localhost");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            _lastRequestUtc = DateTime.UtcNow;
            response.EnsureSuccessStatusCode();
            return await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static IReadOnlyList<FactPoint> Annual(JsonElement gaap, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryUsdUnits(gaap, name, out var units)) continue;
            var points = units.EnumerateArray()
                .Where(item => item.TryGetProperty("form", out var form) && form.GetString() == "10-K"
                    && item.TryGetProperty("fp", out var fp) && fp.GetString() == "FY"
                    && item.TryGetProperty("start", out _))
                .Select(ParsePoint)
                .Where(point => point is not null)
                .Cast<FactPoint>()
                .GroupBy(point => point.End)
                .Select(group => group.OrderByDescending(point => point.Filed).First())
                .OrderByDescending(point => point.End)
                .Take(3)
                .ToList();
            if (points.Count > 0) return points;
        }
        return [];
    }

    private static FactPoint? LatestInstant(JsonElement gaap, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryUsdUnits(gaap, name, out var units)) continue;
            var point = units.EnumerateArray()
                .Where(item => item.TryGetProperty("form", out var form)
                    && form.GetString() is "10-K" or "10-Q"
                    && !item.TryGetProperty("start", out _))
                .Select(ParsePoint)
                .Where(value => value is not null)
                .Cast<FactPoint>()
                .OrderByDescending(value => value.End)
                .ThenByDescending(value => value.Filed)
                .FirstOrDefault();
            if (point is not null) return point;
        }
        return null;
    }

    private static bool TryUsdUnits(JsonElement gaap, string name, out JsonElement units)
    {
        units = default;
        return gaap.TryGetProperty(name, out var fact)
            && fact.TryGetProperty("units", out var allUnits)
            && allUnits.TryGetProperty("USD", out units);
    }

    private static FactPoint? ParsePoint(JsonElement item)
    {
        if (!item.TryGetProperty("val", out var value)
            || !item.TryGetProperty("end", out var end)
            || !DateTime.TryParse(end.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var endDate))
            return null;
        var filedDate = item.TryGetProperty("filed", out var filed)
            && DateTime.TryParse(filed.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedFiled)
            ? parsedFiled : endDate;
        int? fy = item.TryGetProperty("fy", out var fyElement) && fyElement.TryGetInt32(out var fiscalYear)
            ? fiscalYear : null;
        var accession = item.TryGetProperty("accn", out var accn) ? accn.GetString() ?? string.Empty : string.Empty;
        return new FactPoint(value.GetDecimal(), endDate, filedDate, fy, accession);
    }

    private static decimal HealthScore(decimal growth, decimal margin, decimal leverage, decimal fcfYield, decimal pe)
    {
        var profitability = Math.Clamp((margin + 10m) / 35m * 25m, 0m, 25m);
        var growthScore = Math.Clamp((growth + 20m) / 40m * 20m, 0m, 20m);
        var cashFlow = Math.Clamp((fcfYield + 2m) / 10m * 20m, 0m, 20m);
        var balanceSheet = Math.Clamp((4m - leverage) / 3.5m * 20m, 0m, 20m);
        var valuation = pe switch
        {
            <= 0m => 2m,
            <= 10m => 12m,
            <= 25m => 15m,
            <= 40m => 10m,
            <= 60m => 5m,
            _ => 2m
        };
        return Math.Round(Math.Clamp(profitability + growthScore + cashFlow + balanceSheet + valuation, 0m, 100m), 1);
    }

    private static string ValuationLabel(decimal pe, decimal fcfYield) =>
        pe <= 0m ? "Loss-making or earnings data unavailable"
        : pe <= 25m && fcfYield >= 3m ? "Attractive earnings and cash-flow valuation"
        : pe <= 40m ? "Moderate valuation"
        : "Premium valuation";

    private static FundamentalDataSnapshot Unavailable(string symbol, string reason) =>
        new(symbol, symbol, 0m, 0m, 0m, 0m, 0m, 10m, 0m,
            "Unavailable", "Insufficient verified filing data", 0m, true,
            reason, DateTime.UtcNow, []);
}
