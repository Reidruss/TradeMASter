using TradeMASter.Core.Common;

namespace TradeMASter.Core.Entities;

public class RobinhoodSession : BaseEntity
{
    public string AccountNumber { get; set; } = string.Empty;
    public string? EncryptedAuthToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public string? OAuthClientId { get; set; }
    public DateTime? TokenExpiresAtUtc { get; set; }
    public string? Username { get; set; }
    public bool AutoLoginEnabled { get; set; } = true;
    public bool IsDemoMode { get; set; } = false;
    public DateTime LastConnectedAtUtc { get; set; } = DateTime.UtcNow;

    public RobinhoodSession() { }

    public RobinhoodSession(
        string accountNumber,
        string? authToken,
        string? refreshToken,
        string? oauthClientId,
        DateTime? tokenExpiresAtUtc,
        string? username,
        bool isDemoMode,
        bool autoLogin = true)
    {
        AccountNumber = accountNumber;
        EncryptedAuthToken = authToken;
        EncryptedRefreshToken = refreshToken;
        OAuthClientId = oauthClientId;
        TokenExpiresAtUtc = tokenExpiresAtUtc;
        Username = username;
        IsDemoMode = isDemoMode;
        AutoLoginEnabled = autoLogin;
        LastConnectedAtUtc = DateTime.UtcNow;
    }
}
