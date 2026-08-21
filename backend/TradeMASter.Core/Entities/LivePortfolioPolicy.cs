using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Core.Entities;

public sealed class LivePortfolioPolicy : BaseEntity
{
    public static readonly Guid SingletonId = Guid.Parse("72f45ef6-45c9-4fab-9f5c-3aa280880001");

    public bool LiveTradingEnabled { get; private set; }
    public string AllowedAssetTypesCsv { get; private set; } = "Stock,Etf";
    public string AllowedExchangesCsv { get; private set; } = "NASDAQ,NYSE,NYSEARCA,NYSEAMERICAN,ARCA,BATS,CBOE";
    public string AllowedOrderTypesCsv { get; private set; } = "Limit";
    public bool RegularMarketHoursOnly { get; private set; } = true;
    public bool FractionalSharesEnabled { get; private set; }
    public decimal MinimumCashReservePercent { get; private set; } = 20m;
    public decimal MaxOrderNotionalPercent { get; private set; } = 10m;
    public decimal MaxOrderNotionalAmount { get; private set; } = 100m;
    public decimal MaxDailyTurnoverPercent { get; private set; } = 10m;
    public decimal MaxDailyLossPercent { get; private set; } = 2m;
    public decimal MaxPositionPercent { get; private set; } = 20m;
    public decimal MaxSectorPercent { get; private set; } = 40m;
    public decimal MaxAnnualizedVolatilityPercent { get; private set; } = 35m;
    public decimal MaxDailyVaR95Percent { get; private set; } = 3m;
    public decimal MaxDrawdownPercent { get; private set; } = 10m;
    public int MaxQuoteAgeSeconds { get; private set; } = 60;
    public int MaxAccountSnapshotAgeSeconds { get; private set; } = 60;
    public int ApprovalExpiryMinutes { get; private set; } = 5;
    public decimal MaxPriceDriftPercent { get; private set; } = 1m;
    public decimal MaxPositionDriftPercent { get; private set; } = 1m;
    public int OrderTimeoutSeconds { get; private set; } = 120;
    public bool CancelReplaceEnabled { get; private set; }
    public int MaxCancelReplaceAttempts { get; private set; }
    public bool EmergencyHaltActive { get; private set; }
    public string? EmergencyHaltReason { get; private set; }
    public DateTime? EmergencyHaltedAtUtc { get; private set; }
    public int PolicyVersion { get; private set; } = 1;

    public IReadOnlyList<AssetType> AllowedAssetTypes => ParseEnums<AssetType>(AllowedAssetTypesCsv);
    public IReadOnlyList<string> AllowedExchanges => Split(AllowedExchangesCsv);
    public IReadOnlyList<OrderType> AllowedOrderTypes => ParseEnums<OrderType>(AllowedOrderTypesCsv);

    public LivePortfolioPolicy() : base(SingletonId)
    {
        // Phase 1 deliberately has no method that can enable live trading.
        LiveTradingEnabled = false;
    }

    public Result Apply(UpdateLivePortfolioPolicyRequest request)
    {
        var validation = Validate(request);
        if (validation.IsFailure) return validation;

        AllowedAssetTypesCsv = Join(request.AllowedAssetTypes.Select(value => value.ToString()));
        AllowedExchangesCsv = Join(request.AllowedExchanges.Select(NormalizeExchange));
        AllowedOrderTypesCsv = Join(request.AllowedOrderTypes.Select(value => value.ToString()));
        RegularMarketHoursOnly = request.RegularMarketHoursOnly;
        FractionalSharesEnabled = request.FractionalSharesEnabled;
        MinimumCashReservePercent = request.MinimumCashReservePercent;
        MaxOrderNotionalPercent = request.MaxOrderNotionalPercent;
        MaxOrderNotionalAmount = request.MaxOrderNotionalAmount;
        MaxDailyTurnoverPercent = request.MaxDailyTurnoverPercent;
        MaxDailyLossPercent = request.MaxDailyLossPercent;
        MaxPositionPercent = request.MaxPositionPercent;
        MaxSectorPercent = request.MaxSectorPercent;
        MaxAnnualizedVolatilityPercent = request.MaxAnnualizedVolatilityPercent;
        MaxDailyVaR95Percent = request.MaxDailyVaR95Percent;
        MaxDrawdownPercent = request.MaxDrawdownPercent;
        MaxQuoteAgeSeconds = request.MaxQuoteAgeSeconds;
        MaxAccountSnapshotAgeSeconds = request.MaxAccountSnapshotAgeSeconds;
        ApprovalExpiryMinutes = request.ApprovalExpiryMinutes;
        MaxPriceDriftPercent = request.MaxPriceDriftPercent;
        MaxPositionDriftPercent = request.MaxPositionDriftPercent;
        OrderTimeoutSeconds = request.OrderTimeoutSeconds;
        CancelReplaceEnabled = request.CancelReplaceEnabled;
        MaxCancelReplaceAttempts = request.CancelReplaceEnabled ? request.MaxCancelReplaceAttempts : 0;
        PolicyVersion++;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result ActivateEmergencyHalt(string reason)
    {
        var normalized = reason.Trim();
        if (normalized.Length < 5 || normalized.Length > 500)
            return Result.Failure("Emergency halt reason must contain between 5 and 500 characters.");
        EmergencyHaltActive = true;
        EmergencyHaltReason = normalized;
        EmergencyHaltedAtUtc = DateTime.UtcNow;
        PolicyVersion++;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result ClearEmergencyHalt(string confirmation)
    {
        if (!string.Equals(confirmation.Trim(), "RESUME SUPERVISED OPERATIONS", StringComparison.Ordinal))
            return Result.Failure("Exact confirmation 'RESUME SUPERVISED OPERATIONS' is required.");
        EmergencyHaltActive = false;
        EmergencyHaltReason = null;
        EmergencyHaltedAtUtc = null;
        PolicyVersion++;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public LivePortfolioPolicySnapshot ToSnapshot() => new(
        LiveTradingEnabled,
        AllowedAssetTypes,
        AllowedExchanges,
        AllowedOrderTypes,
        RegularMarketHoursOnly,
        FractionalSharesEnabled,
        MinimumCashReservePercent,
        MaxOrderNotionalPercent,
        MaxOrderNotionalAmount,
        MaxDailyTurnoverPercent,
        MaxDailyLossPercent,
        MaxPositionPercent,
        MaxSectorPercent,
        MaxAnnualizedVolatilityPercent,
        MaxDailyVaR95Percent,
        MaxDrawdownPercent,
        MaxQuoteAgeSeconds,
        MaxAccountSnapshotAgeSeconds,
        ApprovalExpiryMinutes,
        MaxPriceDriftPercent,
        MaxPositionDriftPercent,
        OrderTimeoutSeconds,
        CancelReplaceEnabled,
        MaxCancelReplaceAttempts,
        EmergencyHaltActive,
        EmergencyHaltReason,
        EmergencyHaltedAtUtc,
        PolicyVersion,
        UpdatedAt ?? CreatedAt);

    private static Result Validate(UpdateLivePortfolioPolicyRequest request)
    {
        var supportedExchanges = new HashSet<string>(
            ["NASDAQ", "NYSE", "NYSEARCA", "NYSEAMERICAN", "ARCA", "BATS", "CBOE"],
            StringComparer.OrdinalIgnoreCase);
        if (request.AllowedAssetTypes.Count == 0
            || request.AllowedAssetTypes.Any(value => value is not AssetType.Stock and not AssetType.Etf))
            return Result.Failure("Initial live policy permits only Stock and Etf asset types.");
        if (request.AllowedOrderTypes.Count != 1 || request.AllowedOrderTypes[0] != OrderType.Limit)
            return Result.Failure("Initial live policy permits Limit orders only.");
        if (request.AllowedExchanges.Count == 0
            || request.AllowedExchanges.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 30))
            return Result.Failure("At least one valid exchange is required.");
        if (request.AllowedExchanges.Any(value => !supportedExchanges.Contains(NormalizeExchange(value))))
            return Result.Failure("Initial live policy permits supported U.S. equity exchanges only.");
        if (!request.RegularMarketHoursOnly)
            return Result.Failure("Initial live policy requires regular market hours only.");
        if (request.FractionalSharesEnabled)
            return Result.Failure("Fractional live orders remain disabled until broker eligibility preflight is implemented.");

        var percentages = new Dictionary<string, decimal>
        {
            [nameof(request.MinimumCashReservePercent)] = request.MinimumCashReservePercent,
            [nameof(request.MaxOrderNotionalPercent)] = request.MaxOrderNotionalPercent,
            [nameof(request.MaxDailyTurnoverPercent)] = request.MaxDailyTurnoverPercent,
            [nameof(request.MaxDailyLossPercent)] = request.MaxDailyLossPercent,
            [nameof(request.MaxPositionPercent)] = request.MaxPositionPercent,
            [nameof(request.MaxSectorPercent)] = request.MaxSectorPercent,
            [nameof(request.MaxAnnualizedVolatilityPercent)] = request.MaxAnnualizedVolatilityPercent,
            [nameof(request.MaxDailyVaR95Percent)] = request.MaxDailyVaR95Percent,
            [nameof(request.MaxDrawdownPercent)] = request.MaxDrawdownPercent,
            [nameof(request.MaxPriceDriftPercent)] = request.MaxPriceDriftPercent,
            [nameof(request.MaxPositionDriftPercent)] = request.MaxPositionDriftPercent
        };
        var invalid = percentages.FirstOrDefault(item => item.Value <= 0m || item.Value > 100m);
        if (!string.IsNullOrWhiteSpace(invalid.Key))
            return Result.Failure($"{invalid.Key} must be greater than 0 and no more than 100.");
        if (request.MinimumCashReservePercent >= 100m)
            return Result.Failure("MinimumCashReservePercent must be below 100.");
        if (request.MaxPositionPercent > request.MaxSectorPercent)
            return Result.Failure("MaxPositionPercent cannot exceed MaxSectorPercent.");
        if (request.MaxDailyLossPercent > request.MaxDrawdownPercent)
            return Result.Failure("MaxDailyLossPercent cannot exceed MaxDrawdownPercent.");
        if (request.MinimumCashReservePercent < 20m)
            return Result.Failure("MinimumCashReservePercent cannot be loosened below 20% in phase one.");
        if (request.MaxOrderNotionalPercent > 10m || request.MaxOrderNotionalAmount > 100m)
            return Result.Failure("Phase-one order notional cannot exceed 10% of equity or $100, whichever is lower.");
        if (request.MaxOrderNotionalAmount <= 0m)
            return Result.Failure("MaxOrderNotionalAmount must be greater than 0.");
        if (request.MaxDailyTurnoverPercent > 10m
            || request.MaxDailyLossPercent > 2m
            || request.MaxPositionPercent > 20m
            || request.MaxSectorPercent > 40m
            || request.MaxAnnualizedVolatilityPercent > 35m
            || request.MaxDailyVaR95Percent > 3m
            || request.MaxDrawdownPercent > 10m)
            return Result.Failure("One or more portfolio limits exceed the phase-one safety envelope.");
        if (request.MaxQuoteAgeSeconds is < 5 or > 60)
            return Result.Failure("MaxQuoteAgeSeconds must be between 5 and 60.");
        if (request.MaxAccountSnapshotAgeSeconds is < 5 or > 60)
            return Result.Failure("MaxAccountSnapshotAgeSeconds must be between 5 and 60.");
        if (request.ApprovalExpiryMinutes is < 1 or > 5)
            return Result.Failure("ApprovalExpiryMinutes must be between 1 and 5.");
        if (request.MaxPriceDriftPercent > 1m || request.MaxPositionDriftPercent > 1m)
            return Result.Failure("Price and position drift tolerances cannot exceed 1% in phase one.");
        if (request.OrderTimeoutSeconds is < 15 or > 120)
            return Result.Failure("OrderTimeoutSeconds must be between 15 and 120.");
        if (request.CancelReplaceEnabled || request.MaxCancelReplaceAttempts != 0)
            return Result.Failure("Automatic cancel/replace remains disabled until order reconciliation is implemented.");
        return Result.Success();
    }

    private static string NormalizeExchange(string value) => value.Trim().ToUpperInvariant();
    private static string Join(IEnumerable<string> values) => string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase));
    private static IReadOnlyList<string> Split(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static IReadOnlyList<T> ParseEnums<T>(string csv) where T : struct, Enum =>
        Split(csv).Select(value => Enum.Parse<T>(value, true)).ToList();
}
