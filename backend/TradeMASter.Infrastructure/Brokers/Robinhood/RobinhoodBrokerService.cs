using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Infrastructure.Brokers.Robinhood;

public sealed class RobinhoodBrokerService : IRobinhoodService
{
    private sealed record PendingOAuth(string CodeVerifier, string ClientId, string RedirectUri, DateTime ExpiresAtUtc);
    private sealed record OAuthTokenResponse(string AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc, string ClientId);
    private sealed record RobinhoodSnapshot(
        string AccountNumber,
        string AccountType,
        decimal TotalEquity,
        decimal CashAvailable,
        decimal BuyingPower,
        IReadOnlyList<RobinhoodHoldingItem> Holdings);
    private sealed record ParsedPosition(
        string Symbol,
        string Name,
        decimal Quantity,
        decimal AverageCostBasis,
        decimal CurrentPrice,
        decimal CurrentMarketValue);
    private sealed record ParsedAccount(string AccountNumber, string AccountType, JsonElement Payload);
    private sealed record AccountPortfolio(
        ParsedAccount Account,
        JsonElement FinancialPayload,
        decimal Cash,
        decimal BuyingPower,
        decimal TotalValue);

    private static readonly ConcurrentDictionary<string, PendingOAuth> PendingOAuthStates = new();
    private static readonly List<RobinhoodHoldingItem> DemoHoldings = new();
    private static readonly SemaphoreSlim DemoLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly RobinhoodMcpClient _mcpClient;
    private readonly IMarketDataService _marketData;
    private readonly TradeMASterDbContext _dbContext;
    private readonly IDataProtector _tokenProtector;
    private readonly ILogger<RobinhoodBrokerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _mcpServerUrl;
    private readonly string _authorizationUrl;
    private readonly string _tokenUrl;
    private readonly string _registrationUrl;
    private readonly string _scope;
    private readonly string? _preferredAccountNumber;

    public RobinhoodBrokerService(
        HttpClient httpClient,
        RobinhoodMcpClient mcpClient,
        IMarketDataService marketData,
        TradeMASterDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<RobinhoodBrokerService> logger)
    {
        _httpClient = httpClient;
        _mcpClient = mcpClient;
        _marketData = marketData;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        _tokenProtector = dataProtectionProvider.CreateProtector("TradeMASter.RobinhoodOAuthTokens.v1");
        _mcpServerUrl = configuration["Robinhood:McpServerUrl"] ?? "https://agent.robinhood.com/mcp/trading";
        _authorizationUrl = configuration["Robinhood:OAuthAuthorizationUrl"] ?? "https://robinhood.com/oauth";
        _tokenUrl = configuration["Robinhood:OAuthTokenUrl"] ?? "https://api.robinhood.com/oauth2/token/";
        _registrationUrl = configuration["Robinhood:OAuthRegistrationUrl"] ?? "https://agent.robinhood.com/oauth/trading/register";
        _scope = configuration["Robinhood:OAuthScope"] ?? "internal";
        _preferredAccountNumber = configuration["Robinhood:AccountNumber"];
    }

    public async Task<Result<RobinhoodOAuthUrlResponse>> GetOAuthAuthorizationUrlAsync(
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateRedirectUri(redirectUri);
            RemoveExpiredOAuthStates();
            var clientId = _configuration["Robinhood:OAuthClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientId = await RegisterOAuthClientAsync(redirectUri, cancellationToken);
            }

            var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            PendingOAuthStates[state] = new PendingOAuth(codeVerifier, clientId, redirectUri, DateTime.UtcNow.AddMinutes(10));

            var query = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = _scope,
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["resource"] = _mcpServerUrl
            };
            var authorizationUrl = _authorizationUrl + "?" + string.Join("&",
                query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
            return Result.Success(new RobinhoodOAuthUrlResponse(authorizationUrl, state, clientId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Robinhood MCP OAuth flow.");
            return Result.Failure<RobinhoodOAuthUrlResponse>($"Unable to start Robinhood authorization: {ex.Message}");
        }
    }

    public async Task<Result<RobinhoodAccountInfo>> ExchangeOAuthCodeAsync(
        RobinhoodOAuthExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
        {
            return Result.Failure<RobinhoodAccountInfo>("The OAuth code and state are required.");
        }
        if (!PendingOAuthStates.TryRemove(request.State, out var pending) || pending.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Result.Failure<RobinhoodAccountInfo>("The Robinhood authorization request expired or its state did not match. Start sign-in again.");
        }

        try
        {
            var token = await ExchangeCodeForTokenAsync(request.Code, pending, cancellationToken);
            var snapshot = await FetchSnapshotAsync(token.AccessToken, cancellationToken);
            await SaveSessionAsync(snapshot, token, "Robinhood Agentic Account", false, cancellationToken);
            await ApplySnapshotToPortfolioAsync(snapshot, null, cancellationToken);
            return Result.Success(ToAccountInfo(snapshot, false, "Connected through Robinhood Trading MCP"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Robinhood MCP OAuth exchange failed.");
            return Result.Failure<RobinhoodAccountInfo>($"Robinhood authorization failed: {ex.Message}");
        }
    }

    public async Task<Result<RobinhoodAccountInfo>> ConnectAsync(
        RobinhoodAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.UseDemoMode)
            {
                var snapshot = await CreateDemoSnapshotAsync(cancellationToken);
                await SaveSessionAsync(snapshot, new OAuthTokenResponse("demo", null, null, "demo"), "Demo Trader", true, cancellationToken);
                await ApplySnapshotToPortfolioAsync(snapshot, null, cancellationToken);
                return Result.Success(ToAccountInfo(snapshot, true, "Connected to local paper-trading demo"));
            }
            if (!string.IsNullOrWhiteSpace(request.Username) || !string.IsNullOrWhiteSpace(request.Password))
            {
                return Result.Failure<RobinhoodAccountInfo>(
                    "Username/password login is disabled. Use Robinhood's official OAuth flow so TradeMASter never receives your password.");
            }
            if (string.IsNullOrWhiteSpace(request.BearerToken))
            {
                return Result.Failure<RobinhoodAccountInfo>("Use Robinhood OAuth or provide a Robinhood MCP access token.");
            }

            var accessToken = request.BearerToken.Trim();
            var liveSnapshot = await FetchSnapshotAsync(accessToken, cancellationToken);
            await SaveSessionAsync(liveSnapshot, new OAuthTokenResponse(accessToken, null, null, "external-token"),
                "Robinhood Agentic Account", false, cancellationToken);
            await ApplySnapshotToPortfolioAsync(liveSnapshot, null, cancellationToken);
            return Result.Success(ToAccountInfo(liveSnapshot, false, "Connected through Robinhood Trading MCP"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Robinhood MCP.");
            return Result.Failure<RobinhoodAccountInfo>($"Failed to connect to Robinhood MCP: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.RobinhoodSessions.ToListAsync(cancellationToken);
        _dbContext.RobinhoodSessions.RemoveRange(sessions);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    public async Task<Result<SavedRobinhoodSessionDto>> GetSavedSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetLatestSessionAsync(cancellationToken);
        return Result.Success(new SavedRobinhoodSessionDto(
            session is not null, session?.AccountNumber, session?.Username,
            session?.IsDemoMode ?? false, session?.LastConnectedAtUtc));
    }

    public async Task<Result<RobinhoodAccountInfo>> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetLatestSessionAsync(cancellationToken);
        if (session is null)
        {
            var environmentToken = Environment.GetEnvironmentVariable("ROBINHOOD_MCP_ACCESS_TOKEN")
                ?? _configuration["Robinhood:AccessToken"];
            if (!string.IsNullOrWhiteSpace(environmentToken))
            {
                return await ConnectAsync(new RobinhoodAuthRequest(null, null, null, environmentToken, true, false), cancellationToken);
            }
            return Result.Success(DisconnectedAccount("Not connected. Sign in with Robinhood OAuth."));
        }

        try
        {
            if (session.IsDemoMode)
            {
                return Result.Success(ToAccountInfo(await CreateDemoSnapshotAsync(cancellationToken), true,
                    "Connected to local paper-trading demo"));
            }
            var snapshot = await FetchSnapshotAsync(await GetUsableAccessTokenAsync(session, cancellationToken), cancellationToken);
            await UpdateSessionMetadataAsync(session, snapshot, cancellationToken);
            return Result.Success(ToAccountInfo(snapshot, false, "Connected through Robinhood Trading MCP"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood MCP status check failed.");
            return Result.Success(DisconnectedAccount($"Robinhood MCP unavailable: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<RobinhoodHoldingItem>>> GetLiveHoldingsAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetLatestSessionAsync(cancellationToken);
        if (session is null)
        {
            return Result.Failure<IReadOnlyList<RobinhoodHoldingItem>>("Robinhood MCP is not connected.");
        }
        try
        {
            if (session.IsDemoMode)
            {
                return Result.Success<IReadOnlyList<RobinhoodHoldingItem>>((await CreateDemoSnapshotAsync(cancellationToken)).Holdings);
            }
            var snapshot = await FetchSnapshotAsync(await GetUsableAccessTokenAsync(session, cancellationToken), cancellationToken);
            await UpdateSessionMetadataAsync(session, snapshot, cancellationToken);
            return Result.Success(snapshot.Holdings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read holdings through Robinhood MCP.");
            return Result.Failure<IReadOnlyList<RobinhoodHoldingItem>>($"Robinhood MCP holdings sync failed: {ex.Message}");
        }
    }

    public async Task<Result<RobinhoodExecutionAccountSnapshot>> GetExecutionAccountSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await GetLatestSessionAsync(cancellationToken);
        if (session is null)
            return Result.Failure<RobinhoodExecutionAccountSnapshot>("Robinhood MCP is not connected.");
        try
        {
            var snapshot = session.IsDemoMode
                ? await CreateDemoSnapshotAsync(cancellationToken)
                : await FetchSnapshotAsync(await GetUsableAccessTokenAsync(session, cancellationToken), cancellationToken);
            if (!session.IsDemoMode) await UpdateSessionMetadataAsync(session, snapshot, cancellationToken);
            return Result.Success(new RobinhoodExecutionAccountSnapshot(
                snapshot.AccountNumber,
                snapshot.AccountType,
                snapshot.TotalEquity,
                snapshot.CashAvailable,
                snapshot.BuyingPower,
                DateTime.UtcNow,
                session.IsDemoMode,
                snapshot.Holdings));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood MCP execution snapshot failed.");
            return Result.Failure<RobinhoodExecutionAccountSnapshot>($"Robinhood MCP execution snapshot failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<RobinhoodHoldingItem>>> SetCustomHoldingsAsync(
        IReadOnlyList<RobinhoodHoldingItem> customHoldings,
        CancellationToken cancellationToken = default)
    {
        var session = await GetLatestSessionAsync(cancellationToken);
        if (session is null || !session.IsDemoMode)
        {
            return Result.Failure<IReadOnlyList<RobinhoodHoldingItem>>(
                "Custom holdings are only available in local demo mode and can never overwrite a live Robinhood account.");
        }
        await DemoLock.WaitAsync(cancellationToken);
        try
        {
            DemoHoldings.Clear();
            DemoHoldings.AddRange(WithWeights(customHoldings));
        }
        finally
        {
            DemoLock.Release();
        }
        var snapshot = await CreateDemoSnapshotAsync(cancellationToken);
        await ApplySnapshotToPortfolioAsync(snapshot, null, cancellationToken);
        return Result.Success(snapshot.Holdings);
    }

    public async Task<Result<Portfolio>> SyncHoldingsToPortfolioAsync(
        Guid? targetPortfolioId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await GetLatestSessionAsync(cancellationToken);
            if (session is null) return Result.Failure<Portfolio>("Robinhood MCP is not connected; portfolio sync was not performed.");
            var snapshot = session.IsDemoMode
                ? await CreateDemoSnapshotAsync(cancellationToken)
                : await FetchSnapshotAsync(await GetUsableAccessTokenAsync(session, cancellationToken), cancellationToken);
            if (!session.IsDemoMode) await UpdateSessionMetadataAsync(session, snapshot, cancellationToken);
            return Result.Success(await ApplySnapshotToPortfolioAsync(snapshot, targetPortfolioId, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood MCP portfolio sync failed.");
            return Result.Failure<Portfolio>($"Robinhood MCP portfolio sync failed: {ex.Message}");
        }
    }

    private async Task<RobinhoodSnapshot> FetchSnapshotAsync(string accessToken, CancellationToken cancellationToken)
    {
        await _mcpClient.InitializeAsync(accessToken, cancellationToken);
        var tools = await _mcpClient.ListToolsAsync(cancellationToken);
        var accountsTool = FindTool(tools, "get_accounts", "get_account")
            ?? throw new InvalidOperationException("Robinhood MCP does not expose an account-reading tool.");
        var accountsPayload = ToolPayload(await _mcpClient.CallToolAsync(
            accountsTool.Name, BuildArguments(accountsTool, null, null), cancellationToken));
        var accounts = ParseAccounts(accountsPayload);
        if (accounts.Count == 0)
            throw new InvalidOperationException("Robinhood MCP returned no readable accounts.");

        var portfolioTool = FindTool(tools, "get_portfolio");
        var positionsTool = FindTool(tools, "get_equity_positions", "get_positions", "list_positions")
            ?? throw new InvalidOperationException("Robinhood MCP does not expose an equity-position reading tool.");

        var accountPortfolios = new List<AccountPortfolio>();
        foreach (var account in accounts)
        {
            JsonElement? portfolioPayload = null;
            if (portfolioTool is not null)
            {
                portfolioPayload = ToolPayload(await _mcpClient.CallToolAsync(
                    portfolioTool.Name, BuildArguments(portfolioTool, account.AccountNumber, null), cancellationToken));
            }

            var financialPayload = portfolioPayload ?? account.Payload;
            var accountCash = FindDecimal(financialPayload,
                "cash_available_for_withdrawal", "cash_available", "cash", "withdrawable_amount")
                ?? FindDecimal(account.Payload, "cash_available_for_withdrawal", "cash_available", "cash", "withdrawable_amount")
                ?? 0m;
            var accountBuyingPower = FindDecimal(financialPayload,
                "buying_power", "buyingPower", "cash_buying_power", "unleveraged_buying_power") ?? accountCash;
            var totalValue = FindDecimal(financialPayload,
                "total_value", "total_equity", "equity", "portfolio_value", "market_value")
                ?? accountCash;
            accountPortfolios.Add(new AccountPortfolio(
                account, financialPayload, accountCash, accountBuyingPower, totalValue));
        }

        AccountPortfolio selectedAccount;
        if (!string.IsNullOrWhiteSpace(_preferredAccountNumber))
        {
            selectedAccount = accountPortfolios.FirstOrDefault(item =>
                item.Account.AccountNumber.Equals(_preferredAccountNumber, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The configured Robinhood account was not returned by MCP.");
        }
        else
        {
            selectedAccount = accountPortfolios
                .OrderByDescending(item => item.TotalValue)
                .ThenByDescending(item => item.BuyingPower)
                .First();
        }

        var positionsPayload = ToolPayload(await _mcpClient.CallToolAsync(
            positionsTool.Name,
            BuildArguments(positionsTool, selectedAccount.Account.AccountNumber, null),
            cancellationToken));
        var rawPositions = ParsePositions(positionsPayload);
        var cash = selectedAccount.Cash;
        var buyingPower = selectedAccount.BuyingPower;

        JsonElement? quotesPayload = null;
        var missingQuoteSymbols = rawPositions.Where(position => position.CurrentPrice <= 0)
            .Select(position => position.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var quotesTool = FindTool(tools, "get_equity_quotes", "get_quotes");
        if (quotesTool is not null && missingQuoteSymbols.Length > 0)
        {
            quotesPayload = ToolPayload(await _mcpClient.CallToolAsync(
                quotesTool.Name, BuildArguments(quotesTool, null, missingQuoteSymbols), cancellationToken));
        }

        var ungroupedHoldings = new List<RobinhoodHoldingItem>();
        foreach (var raw in rawPositions)
        {
            var currentPrice = raw.CurrentPrice;
            if (currentPrice <= 0 && quotesPayload.HasValue) currentPrice = FindQuotePrice(quotesPayload.Value, raw.Symbol) ?? 0m;
            if (currentPrice <= 0)
            {
                var quote = await _marketData.GetQuoteAsync(raw.Symbol, cancellationToken);
                currentPrice = quote.IsSuccess ? quote.Value.Price : raw.AverageCostBasis;
            }
            var marketValue = raw.CurrentMarketValue > 0 ? raw.CurrentMarketValue : raw.Quantity * currentPrice;
            var costBasis = raw.Quantity * raw.AverageCostBasis;
            var pnl = marketValue - costBasis;
            ungroupedHoldings.Add(new RobinhoodHoldingItem(
                raw.Symbol, raw.Name, raw.Quantity, Math.Round(raw.AverageCostBasis, 4), Math.Round(currentPrice, 4),
                Math.Round(marketValue, 2), Math.Round(pnl, 2),
                costBasis > 0 ? Math.Round(pnl / costBasis * 100m, 2) : 0m, 0m));
        }

        var holdings = ungroupedHoldings
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var quantity = group.Sum(item => item.Quantity);
                var costBasis = group.Sum(item => item.Quantity * item.AverageCostBasis);
                var marketValue = group.Sum(item => item.CurrentMarketValue);
                var pnl = marketValue - costBasis;
                return new RobinhoodHoldingItem(
                    group.Key,
                    group.Select(item => item.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key,
                    quantity,
                    quantity > 0 ? Math.Round(costBasis / quantity, 4) : 0m,
                    quantity > 0 ? Math.Round(marketValue / quantity, 4) : 0m,
                    Math.Round(marketValue, 2),
                    Math.Round(pnl, 2),
                    costBasis > 0 ? Math.Round(pnl / costBasis * 100m, 2) : 0m,
                    0m);
            })
            .ToList();
        var weightedHoldings = WithWeights(holdings);
        var holdingsValue = weightedHoldings.Sum(item => item.CurrentMarketValue);
        var totalEquity = selectedAccount.TotalValue > 0 ? selectedAccount.TotalValue : holdingsValue + cash;
        var accountNumber = selectedAccount.Account.AccountNumber;
        var accountType = selectedAccount.Account.AccountType;
        return new RobinhoodSnapshot(accountNumber, accountType, Math.Round(totalEquity, 2),
            Math.Round(cash, 2), Math.Round(buyingPower, 2), weightedHoldings);
    }

    private static IReadOnlyList<ParsedAccount> ParseAccounts(JsonElement root)
    {
        var accounts = new Dictionary<string, ParsedAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateObjects(root))
        {
            var accountNumber = FindDirectString(candidate, "account_number", "accountNumber", "account_id", "accountId");
            if (string.IsNullOrWhiteSpace(accountNumber)) continue;
            accounts.TryAdd(accountNumber, new ParsedAccount(
                accountNumber,
                FindDirectString(candidate, "account_type", "accountType", "type") ?? "Robinhood",
                candidate.Clone()));
        }
        return accounts.Values.ToList();
    }

    private static IReadOnlyList<ParsedPosition> ParsePositions(JsonElement root)
    {
        var positions = new Dictionary<string, ParsedPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateObjects(root))
        {
            var symbol = FindString(candidate, "symbol", "ticker");
            var quantity = FindDecimal(candidate, "quantity", "total_quantity", "shares");
            if (string.IsNullOrWhiteSpace(symbol) || quantity is null or <= 0) continue;
            var normalizedSymbol = symbol.Trim().ToUpperInvariant();
            positions[normalizedSymbol] = new ParsedPosition(
                normalizedSymbol,
                FindString(candidate, "name", "simple_name", "instrument_name") ?? normalizedSymbol,
                quantity.Value,
                FindDecimal(candidate, "average_buy_price", "average_price", "average_cost", "average_cost_basis", "cost_basis_price") ?? 0m,
                FindDecimal(candidate, "current_price", "price", "last_trade_price", "mark_price") ?? 0m,
                FindDecimal(candidate, "market_value", "equity", "position_value") ?? 0m);
        }
        return positions.Values.ToList();
    }

    private async Task<string> RegisterOAuthClientAsync(string redirectUri, CancellationToken cancellationToken)
    {
        var registrationRequest = new
        {
            client_name = "TradeMASter",
            redirect_uris = new[] { redirectUri },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none"
        };
        using var response = await _httpClient.PostAsJsonAsync(_registrationUrl, registrationRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Robinhood OAuth client registration failed ({(int)response.StatusCode}): {body}");
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("client_id", out var clientIdElement)
            || string.IsNullOrWhiteSpace(clientIdElement.GetString()))
            throw new InvalidOperationException("Robinhood OAuth registration returned no client_id.");
        return clientIdElement.GetString()!;
    }

    private Task<OAuthTokenResponse> ExchangeCodeForTokenAsync(string code, PendingOAuth pending, CancellationToken cancellationToken)
    {
        return RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = pending.ClientId,
            ["code"] = code,
            ["code_verifier"] = pending.CodeVerifier,
            ["redirect_uri"] = pending.RedirectUri,
            ["resource"] = _mcpServerUrl
        }, pending.ClientId, cancellationToken);
    }

    private async Task<OAuthTokenResponse> RefreshAccessTokenAsync(RobinhoodSession session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.EncryptedRefreshToken) || string.IsNullOrWhiteSpace(session.OAuthClientId))
            throw new UnauthorizedAccessException("Robinhood authorization expired. Sign in again.");
        var refreshToken = _tokenProtector.Unprotect(session.EncryptedRefreshToken);
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = session.OAuthClientId,
            ["refresh_token"] = refreshToken,
            ["resource"] = _mcpServerUrl
        }, session.OAuthClientId, cancellationToken);
        session.EncryptedAuthToken = _tokenProtector.Protect(token.AccessToken);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken)) session.EncryptedRefreshToken = _tokenProtector.Protect(token.RefreshToken);
        session.TokenExpiresAtUtc = token.ExpiresAtUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return token;
    }

    private async Task<OAuthTokenResponse> RequestTokenAsync(
        Dictionary<string, string> form,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(_tokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new UnauthorizedAccessException($"Robinhood token endpoint rejected authorization ({(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(body);
        var accessToken = document.RootElement.TryGetProperty("access_token", out var accessTokenElement)
            ? accessTokenElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Robinhood token endpoint returned no access_token.");
        var refreshToken = document.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString() : null;
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
            && expiresInElement.TryGetInt32(out var seconds) ? seconds : (int?)null;
        return new OAuthTokenResponse(accessToken, refreshToken,
            expiresIn.HasValue ? DateTime.UtcNow.AddSeconds(expiresIn.Value) : null, clientId);
    }

    private async Task<string> GetUsableAccessTokenAsync(RobinhoodSession session, CancellationToken cancellationToken)
    {
        if (session.TokenExpiresAtUtc.HasValue && session.TokenExpiresAtUtc.Value <= DateTime.UtcNow.AddMinutes(1))
            return (await RefreshAccessTokenAsync(session, cancellationToken)).AccessToken;
        if (string.IsNullOrWhiteSpace(session.EncryptedAuthToken))
            throw new UnauthorizedAccessException("No Robinhood MCP access token is saved.");
        return _tokenProtector.Unprotect(session.EncryptedAuthToken);
    }

    private async Task SaveSessionAsync(
        RobinhoodSnapshot snapshot,
        OAuthTokenResponse token,
        string username,
        bool isDemoMode,
        CancellationToken cancellationToken)
    {
        var existingSessions = await _dbContext.RobinhoodSessions.ToListAsync(cancellationToken);
        _dbContext.RobinhoodSessions.RemoveRange(existingSessions);
        var session = new RobinhoodSession(
            snapshot.AccountNumber,
            isDemoMode ? null : _tokenProtector.Protect(token.AccessToken),
            !isDemoMode && !string.IsNullOrWhiteSpace(token.RefreshToken) ? _tokenProtector.Protect(token.RefreshToken) : null,
            token.ClientId,
            token.ExpiresAtUtc,
            username,
            isDemoMode,
            autoLogin: true);
        await _dbContext.RobinhoodSessions.AddAsync(session, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateSessionMetadataAsync(RobinhoodSession session, RobinhoodSnapshot snapshot, CancellationToken cancellationToken)
    {
        session.AccountNumber = snapshot.AccountNumber;
        session.LastConnectedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<RobinhoodSession?> GetLatestSessionAsync(CancellationToken cancellationToken) =>
        _dbContext.RobinhoodSessions.OrderByDescending(session => session.LastConnectedAtUtc).FirstOrDefaultAsync(cancellationToken);

    private async Task<Portfolio> ApplySnapshotToPortfolioAsync(
        RobinhoodSnapshot snapshot,
        Guid? targetPortfolioId,
        CancellationToken cancellationToken)
    {
        var portfolio = targetPortfolioId.HasValue
            ? await _dbContext.Portfolios.Include(item => item.Positions)
                .FirstOrDefaultAsync(item => item.Id == targetPortfolioId.Value, cancellationToken)
            : await _dbContext.Portfolios.Include(item => item.Positions)
                .OrderBy(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            portfolio = new Portfolio($"Robinhood ({snapshot.AccountNumber})", snapshot.TotalEquity);
            await _dbContext.Portfolios.AddAsync(portfolio, cancellationToken);
        }

        portfolio.Name = $"Robinhood ({snapshot.AccountNumber})";
        portfolio.CashBalance = snapshot.CashAvailable;
        if (portfolio.InitialBalance <= 0) portfolio.InitialBalance = snapshot.TotalEquity;
        var holdingSymbols = snapshot.Holdings.Select(item => item.Symbol.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stalePosition in portfolio.Positions.Where(position => !holdingSymbols.Contains(position.Symbol)).ToList())
            portfolio.Positions.Remove(stalePosition);
        foreach (var holding in snapshot.Holdings)
        {
            var position = portfolio.Positions.FirstOrDefault(item => item.Symbol.Equals(holding.Symbol, StringComparison.OrdinalIgnoreCase));
            if (position is null)
            {
                position = new Position(portfolio.Id, holding.Symbol, holding.Quantity, holding.AverageCostBasis);
                portfolio.Positions.Add(position);
            }
            else
            {
                position.Quantity = holding.Quantity;
                position.AverageEntryPrice = holding.AverageCostBasis;
            }
            position.UpdateCurrentPrice(holding.CurrentPrice);
        }
        portfolio.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return portfolio;
    }

    private async Task<RobinhoodSnapshot> CreateDemoSnapshotAsync(CancellationToken cancellationToken)
    {
        await DemoLock.WaitAsync(cancellationToken);
        try
        {
            if (DemoHoldings.Count == 0)
            {
                var portfolio = await _dbContext.Portfolios.Include(item => item.Positions)
                    .OrderBy(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
                if (portfolio is not null)
                {
                    foreach (var position in portfolio.Positions.Where(item => item.Quantity > 0))
                    {
                        var quote = await _marketData.GetQuoteAsync(position.Symbol, cancellationToken);
                        var price = quote.IsSuccess ? quote.Value.Price : position.CurrentPrice;
                        var value = position.Quantity * price;
                        var cost = position.Quantity * position.AverageEntryPrice;
                        DemoHoldings.Add(new RobinhoodHoldingItem(position.Symbol, position.Symbol, position.Quantity,
                            position.AverageEntryPrice, price, value, value - cost,
                            cost > 0 ? (value - cost) / cost * 100m : 0m, 0m));
                    }
                }
            }
            var holdings = WithWeights(DemoHoldings);
            var holdingsValue = holdings.Sum(item => item.CurrentMarketValue);
            const decimal cash = 24_500m;
            return new RobinhoodSnapshot("RH-DEMO-PAPER", "Local paper-trading demo",
                holdingsValue + cash, cash, cash, holdings);
        }
        finally
        {
            DemoLock.Release();
        }
    }

    private static IReadOnlyList<RobinhoodHoldingItem> WithWeights(IEnumerable<RobinhoodHoldingItem> holdings)
    {
        var list = holdings.ToList();
        var total = list.Sum(item => item.CurrentMarketValue);
        return list.Select(item => item with
        {
            PortfolioWeightPercent = total > 0 ? Math.Round(item.CurrentMarketValue / total * 100m, 2) : 0m
        }).ToList();
    }

    private static RobinhoodMcpTool? FindTool(IReadOnlyList<RobinhoodMcpTool> tools, params string[] names)
    {
        foreach (var name in names)
        {
            var exact = tools.FirstOrDefault(tool => tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }
        return tools.FirstOrDefault(tool => names.Any(name => tool.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyDictionary<string, object?> BuildArguments(
        RobinhoodMcpTool tool,
        string? accountNumber,
        IReadOnlyList<string>? symbols)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!tool.InputSchema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return arguments;
        foreach (var property in properties.EnumerateObject())
        {
            var normalized = Normalize(property.Name);
            if (!string.IsNullOrWhiteSpace(accountNumber) && normalized is "accountnumber" or "accountid")
                arguments[property.Name] = accountNumber;
            else if (symbols is { Count: > 0 } && normalized == "symbol")
                arguments[property.Name] = symbols[0];
            else if (symbols is { Count: > 0 } && normalized is "symbols" or "tickers")
            {
                var type = property.Value.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                arguments[property.Name] = string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)
                    ? symbols : string.Join(",", symbols);
            }
        }
        return arguments;
    }

    private static JsonElement ToolPayload(JsonElement toolResult)
    {
        if (toolResult.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("Robinhood MCP tool returned an error result.");
        if (toolResult.TryGetProperty("structuredContent", out var structured)
            && structured.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return structured.Clone();
        if (toolResult.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textElement)
                    && !string.IsNullOrWhiteSpace(textElement.GetString())
                    && TryParseEmbeddedJson(textElement.GetString()!, out var parsed)) return parsed;
            }
        }
        return toolResult.Clone();
    }

    private static bool TryParseEmbeddedJson(string text, out JsonElement element)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline) trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        var firstObject = trimmed.IndexOf('{');
        var firstArray = trimmed.IndexOf('[');
        var start = firstObject < 0 ? firstArray : firstArray < 0 ? firstObject : Math.Min(firstObject, firstArray);
        if (start > 0) trimmed = trimmed[start..];
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            element = document.RootElement.Clone();
            return true;
        }
        catch
        {
            element = default;
            return false;
        }
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            foreach (var property in root.EnumerateObject())
                foreach (var nested in EnumerateObjects(property.Value)) yield return nested;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                foreach (var nested in EnumerateObjects(item)) yield return nested;
        }
    }

    private static string? FindString(JsonElement root, params string[] names)
    {
        var targets = names.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateObjects(root))
            foreach (var property in candidate.EnumerateObject())
                if (targets.Contains(Normalize(property.Name)))
                {
                    if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
                    if (property.Value.ValueKind == JsonValueKind.Number) return property.Value.GetRawText();
                }
        return null;
    }

    private static string? FindDirectString(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var targets = names.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!targets.Contains(Normalize(property.Name))) continue;
            if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
            if (property.Value.ValueKind == JsonValueKind.Number) return property.Value.GetRawText();
        }
        return null;
    }

    private static decimal? FindDecimal(JsonElement root, params string[] names)
    {
        var targets = names.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateObjects(root))
            foreach (var property in candidate.EnumerateObject())
                if (targets.Contains(Normalize(property.Name)))
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var number)) return number;
                    if (property.Value.ValueKind == JsonValueKind.String
                        && decimal.TryParse(property.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
                }
        return null;
    }

    private static decimal? FindQuotePrice(JsonElement root, string symbol)
    {
        foreach (var candidate in EnumerateObjects(root))
        {
            if (!string.Equals(FindString(candidate, "symbol", "ticker"), symbol, StringComparison.OrdinalIgnoreCase)) continue;
            return FindDecimal(candidate, "price", "last_trade_price", "mark_price", "current_price");
        }
        return null;
    }

    private static void ValidateRedirectUri(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) throw new ArgumentException("The OAuth redirect URI is invalid.");
        if (uri.Scheme == Uri.UriSchemeHttps) return;
        if (uri.Scheme == Uri.UriSchemeHttp
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))) return;
        throw new ArgumentException("Robinhood OAuth redirects must use HTTPS, except localhost development callbacks.");
    }

    private static void RemoveExpiredOAuthStates()
    {
        foreach (var item in PendingOAuthStates.Where(item => item.Value.ExpiresAtUtc <= DateTime.UtcNow))
            PendingOAuthStates.TryRemove(item.Key, out _);
    }

    private static RobinhoodAccountInfo ToAccountInfo(RobinhoodSnapshot snapshot, bool isDemoMode, string message) =>
        new(snapshot.AccountNumber, snapshot.AccountType, snapshot.TotalEquity, snapshot.CashAvailable,
            snapshot.BuyingPower, true, DateTime.UtcNow, message,
            isDemoMode ? "Demo Trader" : "Robinhood Agentic Account", isDemoMode);

    private static RobinhoodAccountInfo DisconnectedAccount(string message) =>
        new("Not Connected", "None", 0m, 0m, 0m, false, DateTime.UtcNow, message);

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
