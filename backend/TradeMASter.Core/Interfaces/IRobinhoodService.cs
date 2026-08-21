using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;

namespace TradeMASter.Core.Interfaces;

public record RobinhoodAuthRequest(
    string? Username,
    string? Password,
    string? MfaCode,
    string? BearerToken,
    bool RememberMe = true,
    bool UseDemoMode = false);

public record RobinhoodAccountInfo(
    string AccountNumber,
    string AccountType,
    decimal TotalEquity,
    decimal CashAvailable,
    decimal BuyingPower,
    bool IsConnected,
    DateTime LastSyncedUtc,
    string StatusMessage,
    string? Username = null,
    bool IsDemoMode = false);

public record RobinhoodHoldingItem(
    string Symbol,
    string Name,
    decimal Quantity,
    decimal AverageCostBasis,
    decimal CurrentPrice,
    decimal CurrentMarketValue,
    decimal UnrealizedPnL,
    decimal UnrealizedPnLPercent,
    decimal PortfolioWeightPercent);

public record RobinhoodExecutionAccountSnapshot(
    string AccountNumber,
    string AccountType,
    decimal TotalEquity,
    decimal CashAvailable,
    decimal BuyingPower,
    DateTime AsOfUtc,
    bool IsDemoMode,
    IReadOnlyList<RobinhoodHoldingItem> Holdings);

public record SavedRobinhoodSessionDto(
    bool HasSavedSession,
    string? AccountNumber,
    string? Username,
    bool IsDemoMode,
    DateTime? LastConnectedAtUtc);

public record RobinhoodOAuthUrlResponse(
    string AuthorizationUrl,
    string State,
    string ClientId);

public record RobinhoodOAuthExchangeRequest(
    string Code,
    string State);

public interface IRobinhoodService
{
    Task<Result<RobinhoodOAuthUrlResponse>> GetOAuthAuthorizationUrlAsync(string redirectUri, CancellationToken cancellationToken = default);
    Task<Result<RobinhoodAccountInfo>> ExchangeOAuthCodeAsync(RobinhoodOAuthExchangeRequest request, CancellationToken cancellationToken = default);
    Task<Result<RobinhoodAccountInfo>> ConnectAsync(RobinhoodAuthRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default);
    Task<Result<RobinhoodAccountInfo>> GetAccountStatusAsync(CancellationToken cancellationToken = default);
    Task<Result<SavedRobinhoodSessionDto>> GetSavedSessionAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<RobinhoodHoldingItem>>> GetLiveHoldingsAsync(CancellationToken cancellationToken = default);
    Task<Result<RobinhoodExecutionAccountSnapshot>> GetExecutionAccountSnapshotAsync(CancellationToken cancellationToken = default);
    Task<Result<Portfolio>> SyncHoldingsToPortfolioAsync(Guid? targetPortfolioId = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<RobinhoodHoldingItem>>> SetCustomHoldingsAsync(IReadOnlyList<RobinhoodHoldingItem> customHoldings, CancellationToken cancellationToken = default);
}
