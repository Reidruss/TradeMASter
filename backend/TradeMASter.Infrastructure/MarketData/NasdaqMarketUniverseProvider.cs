using System.Globalization;
using System.Text.Json;
using TradeMASter.Core.Common;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.MarketData;

public sealed class NasdaqMarketUniverseProvider(HttpClient httpClient) : IMarketUniverseProvider
{
    private const string ScreenerUrl =
        "https://api.nasdaq.com/api/screener/stocks?tableonly=true&limit=10000&offset=0&download=true";

    public async Task<Result<MarketUniverseSnapshot>> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ScreenerUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 TradeMASter/1.0");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<MarketUniverseSnapshot>($"Nasdaq market screener returned {(int)response.StatusCode}.");

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var rows = document.RootElement.GetProperty("data").GetProperty("rows");
            var securities = new List<MarketUniverseAsset>();
            foreach (var row in rows.EnumerateArray())
            {
                var symbol = Text(row, "symbol").ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;
                securities.Add(new MarketUniverseAsset(
                    symbol,
                    Text(row, "name"),
                    Text(row, "sector"),
                    Text(row, "industry"),
                    Text(row, "country"),
                    Number(row, "lastsale"),
                    Number(row, "pctchange"),
                    Number(row, "volume"),
                    Number(row, "marketCap")));
            }

            return Result.Success(new MarketUniverseSnapshot(
                DateTime.UtcNow,
                "Nasdaq public market screener",
                securities.Count,
                securities));
        }
        catch (Exception ex)
        {
            return Result.Failure<MarketUniverseSnapshot>($"Full-market scan failed: {ex.Message}");
        }
    }

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static decimal Number(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value)) return 0m;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        text = text.Replace("$", string.Empty).Replace("%", string.Empty).Replace(",", string.Empty).Trim();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0m;
    }
}
