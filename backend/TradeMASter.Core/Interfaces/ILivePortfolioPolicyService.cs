using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Interfaces;

public record UpdateLivePortfolioPolicyRequest(
    IReadOnlyList<AssetType> AllowedAssetTypes,
    IReadOnlyList<string> AllowedExchanges,
    IReadOnlyList<OrderType> AllowedOrderTypes,
    bool RegularMarketHoursOnly,
    bool FractionalSharesEnabled,
    decimal MinimumCashReservePercent,
    decimal MaxOrderNotionalPercent,
    decimal MaxOrderNotionalAmount,
    decimal MaxDailyTurnoverPercent,
    decimal MaxDailyLossPercent,
    decimal MaxPositionPercent,
    decimal MaxSectorPercent,
    decimal MaxAnnualizedVolatilityPercent,
    decimal MaxDailyVaR95Percent,
    decimal MaxDrawdownPercent,
    int MaxQuoteAgeSeconds,
    int MaxAccountSnapshotAgeSeconds,
    int ApprovalExpiryMinutes,
    decimal MaxPriceDriftPercent,
    decimal MaxPositionDriftPercent,
    int OrderTimeoutSeconds,
    bool CancelReplaceEnabled,
    int MaxCancelReplaceAttempts);

public record LivePortfolioPolicySnapshot(
    bool LiveTradingEnabled,
    IReadOnlyList<AssetType> AllowedAssetTypes,
    IReadOnlyList<string> AllowedExchanges,
    IReadOnlyList<OrderType> AllowedOrderTypes,
    bool RegularMarketHoursOnly,
    bool FractionalSharesEnabled,
    decimal MinimumCashReservePercent,
    decimal MaxOrderNotionalPercent,
    decimal MaxOrderNotionalAmount,
    decimal MaxDailyTurnoverPercent,
    decimal MaxDailyLossPercent,
    decimal MaxPositionPercent,
    decimal MaxSectorPercent,
    decimal MaxAnnualizedVolatilityPercent,
    decimal MaxDailyVaR95Percent,
    decimal MaxDrawdownPercent,
    int MaxQuoteAgeSeconds,
    int MaxAccountSnapshotAgeSeconds,
    int ApprovalExpiryMinutes,
    decimal MaxPriceDriftPercent,
    decimal MaxPositionDriftPercent,
    int OrderTimeoutSeconds,
    bool CancelReplaceEnabled,
    int MaxCancelReplaceAttempts,
    bool EmergencyHaltActive,
    string? EmergencyHaltReason,
    DateTime? EmergencyHaltedAtUtc,
    int PolicyVersion,
    DateTime UpdatedAtUtc);

public record LiveOrderPolicyContext(
    AssetType AssetType,
    string Exchange,
    decimal ReferencePrice,
    decimal TotalEquity,
    decimal AvailableCash,
    decimal CurrentPositionValue,
    DateTime QuoteAsOfUtc,
    DateTime AccountSnapshotAsOfUtc,
    decimal CurrentDailyTurnoverPercent = 0m,
    decimal CurrentDailyLossPercent = 0m,
    decimal CurrentDrawdownPercent = 0m,
    decimal ProjectedSectorPercent = 0m,
    decimal ProjectedPortfolioVolatilityPercent = 0m,
    decimal ProjectedDailyVaR95Percent = 0m,
    DateTime? EvaluationTimeUtc = null);

public interface ILivePortfolioPolicyService
{
    Task<LivePortfolioPolicySnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task<Result<LivePortfolioPolicySnapshot>> UpdateAsync(UpdateLivePortfolioPolicyRequest request, CancellationToken cancellationToken = default);
    Task<Result<LivePortfolioPolicySnapshot>> ActivateEmergencyHaltAsync(string reason, CancellationToken cancellationToken = default);
    Task<Result<LivePortfolioPolicySnapshot>> ClearEmergencyHaltAsync(string confirmation, CancellationToken cancellationToken = default);
    Task<Result> ValidateLiveOrderAsync(OrderRequest request, LiveOrderPolicyContext context, CancellationToken cancellationToken = default);
}
