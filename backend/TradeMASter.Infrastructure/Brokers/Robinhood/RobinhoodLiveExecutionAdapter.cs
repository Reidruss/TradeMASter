using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Infrastructure.Brokers.Robinhood;

public sealed class RobinhoodLiveExecutionAdapter(
    IRobinhoodService robinhoodService,
    RobinhoodMcpClient mcpClient,
    TradeMASterDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IRobinhoodLiveExecutionAdapter
{
    private readonly IDataProtector _tokenProtector =
        dataProtectionProvider.CreateProtector("TradeMASter.RobinhoodOAuthTokens.v1");

    public async Task<Result<BrokerExecutionSnapshot>> GetFreshPreflightSnapshotAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var normalizedSymbols = symbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalizedSymbols.Count == 0)
            return Result.Failure<BrokerExecutionSnapshot>("At least one symbol is required for broker preflight.");

        var accountResult = await robinhoodService.GetExecutionAccountSnapshotAsync(cancellationToken);
        if (accountResult.IsFailure) return Result.Failure<BrokerExecutionSnapshot>(accountResult.Error!);
        if (accountResult.Value.IsDemoMode)
            return Result.Failure<BrokerExecutionSnapshot>("Demo sessions cannot enter the Robinhood live-execution path.");

        try
        {
            var tools = await GetAuthorizedToolsAsync(cancellationToken);
            var ordersTool = RequiredTool(tools, "get_equity_orders");
            var quotesTool = RequiredTool(tools, "get_equity_quotes");
            var tradabilityTool = RequiredTool(tools, "get_equity_tradability");
            _ = RequiredTool(tools, "review_equity_order");
            var placeTool = RequiredTool(tools, "place_equity_order");
            if (!HasProperty(placeTool, "clientorderid", "idempotencykey"))
                return Result.Failure<BrokerExecutionSnapshot>(
                    "Robinhood place_equity_order does not advertise a client order ID; submission fails closed without broker idempotency.");

            var account = accountResult.Value;
            var retrievedAt = DateTime.UtcNow;
            var orderResult = await mcpClient.CallToolAsync(
                ordersTool.Name,
                BuildArguments(ordersTool, account.AccountNumber, null, null, retrievedAt.Date),
                cancellationToken);
            var orderPayload = Payload(orderResult);
            var allOrders = ParseOrders(orderPayload);
            var openOrders = allOrders.Where(order => IsOpenState(order.State)).ToList();
            var dailyNotional = ParseDailyFilledNotional(orderPayload, retrievedAt.Date);
            var dailyTurnover = account.TotalEquity > 0m ? dailyNotional / account.TotalEquity * 100m : 0m;

            var quoteResult = await mcpClient.CallToolAsync(
                quotesTool.Name,
                BuildArguments(quotesTool, account.AccountNumber, normalizedSymbols, null, null),
                cancellationToken);
            var quotes = ParseQuotes(Payload(quoteResult), normalizedSymbols, retrievedAt);

            var eligibility = new List<BrokerInstrumentEligibility>();
            foreach (var symbol in normalizedSymbols)
            {
                var tradabilityResult = await mcpClient.CallToolAsync(
                    tradabilityTool.Name,
                    BuildArguments(tradabilityTool, account.AccountNumber, [symbol], null, null),
                    cancellationToken);
                var parsed = ParseEligibility(Payload(tradabilityResult), symbol);
                if (string.IsNullOrWhiteSpace(parsed.Exchange))
                    parsed = parsed with { Exchange = await ResolveExchangeAsync(tools, account.AccountNumber, symbol, cancellationToken) };
                eligibility.Add(parsed);
            }

            return Result.Success(new BrokerExecutionSnapshot(
                account.AccountNumber,
                account.AccountType,
                account.TotalEquity,
                account.CashAvailable,
                account.BuyingPower,
                account.AsOfUtc,
                account.Holdings,
                openOrders,
                quotes,
                eligibility,
                Math.Round(dailyTurnover, 4)));
        }
        catch (Exception ex)
        {
            return Result.Failure<BrokerExecutionSnapshot>($"Robinhood broker preflight failed closed: {SafeMessage(ex)}");
        }
    }

    public async Task<Result<BrokerOrderReview>> ReviewOrderAsync(
        BrokerOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tools = await GetAuthorizedToolsAsync(cancellationToken);
            var tool = RequiredTool(tools, "review_equity_order");
            var result = await mcpClient.CallToolAsync(
                tool.Name,
                BuildArguments(tool, command.AccountNumber, [command.Symbol], command, null),
                cancellationToken);
            if (IsToolError(result))
                return Result.Failure<BrokerOrderReview>("Robinhood pre-trade review rejected the proposed order.");
            var payload = Payload(result);
            var warnings = FindMessages(payload, "warning", "warnings", "alert", "alerts", "error", "errors");
            var explicitApproval = FindBoolean(payload, "approved", "is_approved", "can_submit", "can_place");
            var approved = explicitApproval != false && warnings.Count == 0;
            var sanitized = JsonSerializer.Serialize(new { approved, warnings });
            return Result.Success(new BrokerOrderReview(approved, warnings, sanitized));
        }
        catch (Exception ex)
        {
            return Result.Failure<BrokerOrderReview>($"Robinhood pre-trade review failed closed: {SafeMessage(ex)}");
        }
    }

    public async Task<BrokerOrderSubmission> PlaceOrderAsync(
        BrokerOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tools = await GetAuthorizedToolsAsync(cancellationToken);
            var tool = RequiredTool(tools, "place_equity_order");
            if (!HasProperty(tool, "clientorderid", "idempotencykey"))
                return Unknown("Broker idempotency capability disappeared after preflight.");
            var result = await mcpClient.CallToolAsync(
                tool.Name,
                BuildArguments(tool, command.AccountNumber, [command.Symbol], command, null),
                cancellationToken);
            if (IsToolError(result))
                return new BrokerOrderSubmission(
                    BrokerSubmissionOutcome.Rejected, null, "rejected", "Robinhood rejected the order.",
                    JsonSerializer.Serialize(new { outcome = "rejected" }));
            var payload = Payload(result);
            var brokerOrderId = FindString(payload, "order_id", "orderid", "id");
            var state = FindString(payload, "state", "status") ?? "accepted";
            if (string.IsNullOrWhiteSpace(brokerOrderId)) return Unknown("Robinhood returned no broker order ID; reconciliation is required.");
            return new BrokerOrderSubmission(
                BrokerSubmissionOutcome.Accepted,
                brokerOrderId,
                state,
                "Robinhood accepted the client-order-id-bound order.",
                JsonSerializer.Serialize(new { brokerOrderId, state }));
        }
        catch
        {
            // A timeout or transport error can occur after broker acceptance. Never retry automatically.
            return Unknown("Robinhood acceptance could not be proven; reconciliation is required before any retry.");
        }
    }

    private async Task<IReadOnlyList<RobinhoodMcpTool>> GetAuthorizedToolsAsync(CancellationToken cancellationToken)
    {
        var session = await dbContext.RobinhoodSessions.OrderByDescending(item => item.LastConnectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No saved Robinhood OAuth session is available.");
        if (session.IsDemoMode || string.IsNullOrWhiteSpace(session.EncryptedAuthToken))
            throw new InvalidOperationException("A live Robinhood OAuth session is required.");
        var accessToken = _tokenProtector.Unprotect(session.EncryptedAuthToken);
        await mcpClient.InitializeAsync(accessToken, cancellationToken);
        return await mcpClient.ListToolsAsync(cancellationToken);
    }

    private async Task<string> ResolveExchangeAsync(
        IReadOnlyList<RobinhoodMcpTool> tools,
        string accountNumber,
        string symbol,
        CancellationToken cancellationToken)
    {
        var searchTool = tools.FirstOrDefault(tool => tool.Name.Equals("search", StringComparison.OrdinalIgnoreCase));
        if (searchTool is not null)
        {
            var result = await mcpClient.CallToolAsync(
                searchTool.Name,
                BuildArguments(searchTool, accountNumber, [symbol], null, null),
                cancellationToken);
            var exact = Objects(Payload(result)).FirstOrDefault(item =>
                string.Equals(FindDirectString(item, "symbol", "ticker"), symbol, StringComparison.OrdinalIgnoreCase));
            if (exact.ValueKind == JsonValueKind.Object)
            {
                var exchange = NormalizeExchange(FindString(exact, "exchange", "mic", "venue"));
                if (!string.IsNullOrWhiteSpace(exchange)) return exchange;
            }
        }
        var asset = await dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(
            item => item.Symbol == symbol,
            cancellationToken);
        return NormalizeExchange(asset?.Exchange);
    }

    private static IReadOnlyDictionary<string, object?> BuildArguments(
        RobinhoodMcpTool tool,
        string accountNumber,
        IReadOnlyList<string>? symbols,
        BrokerOrderCommand? command,
        DateTime? startUtc)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!tool.InputSchema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return values;
        foreach (var property in properties.EnumerateObject())
        {
            var name = Normalize(property.Name);
            object? value = name switch
            {
                "accountnumber" or "accountid" => accountNumber,
                "symbol" or "ticker" when symbols is { Count: > 0 } => symbols[0],
                "symbols" or "tickers" when symbols is { Count: > 0 } => PropertyValue(property.Value, symbols),
                "query" or "searchquery" when symbols is { Count: > 0 } => symbols[0],
                "side" or "direction" when command is not null => command.Side == OrderSide.Buy ? "buy" : "sell",
                "type" or "ordertype" when command is not null => "limit",
                "quantity" or "shares" or "assetquantity" when command is not null => PropertyValue(property.Value, command.Quantity),
                "limitprice" or "price" when command is not null => PropertyValue(property.Value, command.LimitPrice),
                "timeinforce" or "tif" when command is not null => command.TimeInForce,
                "clientorderid" or "idempotencykey" when command is not null => command.ClientOrderId.ToString(),
                "createdatstart" or "starttime" or "from" or "startdate" when startUtc.HasValue => startUtc.Value.ToString("O"),
                _ => null
            };
            if (value is not null) values[property.Name] = value;
        }
        if (tool.InputSchema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            var missing = required.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item))
                .FirstOrDefault(item => !values.ContainsKey(item!));
            if (missing is not null) throw new InvalidOperationException($"Robinhood MCP tool {tool.Name} requires unsupported field {missing}.");
        }
        return values;
    }

    private static object PropertyValue(JsonElement schema, object value)
    {
        var type = schema.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (value is IReadOnlyList<string> symbols)
            return string.Equals(type, "array", StringComparison.OrdinalIgnoreCase) ? symbols : string.Join(',', symbols);
        if (value is decimal number)
            return string.Equals(type, "string", StringComparison.OrdinalIgnoreCase)
                ? number.ToString(CultureInfo.InvariantCulture) : number;
        return value;
    }

    private static RobinhoodMcpTool RequiredTool(IReadOnlyList<RobinhoodMcpTool> tools, string name) =>
        tools.FirstOrDefault(tool => tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Robinhood MCP does not expose required tool {name}.");

    private static bool HasProperty(RobinhoodMcpTool tool, params string[] names)
    {
        if (!tool.InputSchema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return false;
        var allowed = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return properties.EnumerateObject().Any(property => allowed.Contains(Normalize(property.Name)));
    }

    private static IReadOnlyList<BrokerOpenOrderSnapshot> ParseOrders(JsonElement payload)
    {
        var orders = new List<BrokerOpenOrderSnapshot>();
        foreach (var item in Objects(payload))
        {
            var id = FindDirectString(item, "order_id", "orderid", "id");
            var symbol = FindDirectString(item, "symbol", "ticker");
            var state = FindDirectString(item, "state", "status");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(state)) continue;
            var side = string.Equals(FindDirectString(item, "side", "direction"), "sell", StringComparison.OrdinalIgnoreCase)
                ? OrderSide.Sell : OrderSide.Buy;
            orders.Add(new BrokerOpenOrderSnapshot(
                id, symbol.ToUpperInvariant(), side,
                FindDecimal(item, "quantity", "asset_quantity", "shares") ?? 0m,
                FindDecimal(item, "limit_price", "limitprice", "price"), state));
        }
        return orders;
    }

    private static decimal ParseDailyFilledNotional(JsonElement payload, DateTime startUtc)
    {
        decimal total = 0m;
        foreach (var item in Objects(payload))
        {
            var state = FindDirectString(item, "state", "status");
            if (!string.Equals(state, "filled", StringComparison.OrdinalIgnoreCase)) continue;
            var timestamp = FindDate(item, "filled_at", "updated_at", "created_at", "timestamp");
            if (timestamp.HasValue && timestamp.Value < startUtc) continue;
            var quantity = FindDecimal(item, "filled_quantity", "quantity", "asset_quantity", "shares") ?? 0m;
            var price = FindDecimal(item, "average_price", "filled_price", "limit_price", "price") ?? 0m;
            total += quantity * price;
        }
        return total;
    }

    private static IReadOnlyList<BrokerQuoteSnapshot> ParseQuotes(
        JsonElement payload,
        IReadOnlyList<string> requested,
        DateTime retrievedAt)
    {
        var quotes = new Dictionary<string, BrokerQuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Objects(payload))
        {
            var symbol = FindDirectString(item, "symbol", "ticker");
            if (string.IsNullOrWhiteSpace(symbol) || !requested.Contains(symbol, StringComparer.OrdinalIgnoreCase)) continue;
            var price = FindDecimal(item, "price", "last_trade_price", "mark_price", "current_price");
            if (price is null or <= 0m) continue;
            quotes[symbol] = new BrokerQuoteSnapshot(
                symbol.ToUpperInvariant(), price.Value,
                FindDecimal(item, "bid_price", "bid"), FindDecimal(item, "ask_price", "ask"),
                FindDate(item, "timestamp", "updated_at", "as_of") ?? retrievedAt,
                "Robinhood MCP get_equity_quotes");
        }
        return quotes.Values.ToList();
    }

    private static BrokerInstrumentEligibility ParseEligibility(JsonElement payload, string symbol)
    {
        var item = Objects(payload).FirstOrDefault(candidate =>
            string.Equals(FindDirectString(candidate, "symbol", "ticker"), symbol, StringComparison.OrdinalIgnoreCase));
        if (item.ValueKind != JsonValueKind.Object) item = payload;
        var tradable = FindBoolean(item, "tradable", "is_tradable", "tradeable") ?? false;
        var fractional = FindBoolean(item, "fractional", "fractional_tradable", "is_fractional_tradable", "supports_fractional") ?? false;
        var rawType = FindString(item, "asset_type", "instrument_type", "type") ?? "stock";
        var assetType = rawType.Contains("etf", StringComparison.OrdinalIgnoreCase) ? AssetType.Etf : AssetType.Stock;
        return new BrokerInstrumentEligibility(
            symbol, tradable, fractional, assetType,
            NormalizeExchange(FindString(item, "exchange", "mic", "venue")),
            "Robinhood MCP get_equity_tradability");
    }

    private static string NormalizeExchange(string? value) => Normalize(value ?? string.Empty) switch
    {
        "xnas" or "nasdaq" or "nasdaqgs" or "nasdaqgm" or "nasdaqcm" => "NASDAQ",
        "xnys" or "nyse" => "NYSE",
        "arcx" or "nysearca" or "arca" => "NYSEARCA",
        "xase" or "nyseamerican" or "amex" => "NYSEAMERICAN",
        "bats" or "cboe" => "BATS",
        _ => value?.Trim().ToUpperInvariant() ?? string.Empty
    };

    private static bool IsOpenState(string state) => Normalize(state) is
        "open" or "queued" or "pending" or "confirmed" or "unconfirmed" or "partiallyfilled" or "cancelpending";

    private static BrokerOrderSubmission Unknown(string message) => new(
        BrokerSubmissionOutcome.Unknown, null, "unknown", message,
        JsonSerializer.Serialize(new { outcome = "unknown", reconciliationRequired = true }));

    private static bool IsToolError(JsonElement result) =>
        result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True;

    private static JsonElement Payload(JsonElement result)
    {
        if (result.TryGetProperty("structuredContent", out var structured)) return structured.Clone();
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("text", out var text) || string.IsNullOrWhiteSpace(text.GetString())) continue;
                var raw = text.GetString()!.Trim();
                var start = new[] { raw.IndexOf('{'), raw.IndexOf('[') }.Where(index => index >= 0).DefaultIfEmpty(-1).Min();
                if (start >= 0)
                {
                    try { using var document = JsonDocument.Parse(raw[start..]); return document.RootElement.Clone(); }
                    catch { }
                }
            }
        }
        return result.Clone();
    }

    private static IEnumerable<JsonElement> Objects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            foreach (var property in root.EnumerateObject())
                foreach (var nested in Objects(property.Value)) yield return nested;
        }
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (var item in root.EnumerateArray())
                foreach (var nested in Objects(item)) yield return nested;
    }

    private static string? FindString(JsonElement root, params string[] names)
    {
        foreach (var item in Objects(root))
        {
            var value = FindDirectString(item, names);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static string? FindDirectString(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var allowed = names.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(Normalize(property.Name))) continue;
            if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
            if (property.Value.ValueKind == JsonValueKind.Number) return property.Value.GetRawText();
        }
        return null;
    }

    private static decimal? FindDecimal(JsonElement root, params string[] names)
    {
        var raw = FindDirectString(root, names);
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static bool? FindBoolean(JsonElement root, params string[] names)
    {
        var allowed = names.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Objects(root))
            foreach (var property in item.EnumerateObject())
            {
                if (!allowed.Contains(Normalize(property.Name))) continue;
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) return property.Value.GetBoolean();
                if (property.Value.ValueKind == JsonValueKind.String && bool.TryParse(property.Value.GetString(), out var value)) return value;
            }
        return null;
    }

    private static DateTime? FindDate(JsonElement root, params string[] names)
    {
        var raw = FindDirectString(root, names);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value : null;
    }

    private static IReadOnlyList<string> FindMessages(JsonElement root, params string[] names)
    {
        var allowed = names.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        foreach (var item in Objects(root))
            foreach (var property in item.EnumerateObject())
            {
                if (!allowed.Contains(Normalize(property.Name))) continue;
                if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    messages.Add(property.Value.GetString()!.Trim());
                else if (property.Value.ValueKind == JsonValueKind.Array)
                    messages.AddRange(property.Value.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        return messages.Distinct().Take(20).ToList();
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Robinhood authorization expired or was revoked.",
        OperationCanceledException => "The broker request was cancelled or timed out.",
        InvalidOperationException when exception.Message.StartsWith("Robinhood MCP does not expose required tool", StringComparison.Ordinal)
            || exception.Message.StartsWith("Robinhood MCP tool", StringComparison.Ordinal)
            => exception.Message.Length <= 300 ? exception.Message : exception.Message[..300],
        _ => "The Robinhood MCP dependency failed without a safe, actionable response."
    };

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
