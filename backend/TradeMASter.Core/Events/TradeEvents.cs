using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Events;

public record OrderPlacedEvent(
    Guid OrderId,
    Guid PortfolioId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice,
    DateTime OccurredOn) : IDomainEvent;

public record OrderFilledEvent(
    Guid OrderId,
    Guid PortfolioId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal FilledPrice,
    DateTime OccurredOn) : IDomainEvent;

public record RiskViolatedEvent(
    Guid PortfolioId,
    string Symbol,
    string RuleViolated,
    string Details,
    DateTime OccurredOn) : IDomainEvent;

public record PriceUpdatedEvent(
    string Symbol,
    decimal Price,
    decimal ChangePercent24h,
    DateTime OccurredOn) : IDomainEvent;

public record DecisionReachedEvent(
    Guid SessionId,
    string Symbol,
    DecisionVerdict Verdict,
    string Summary,
    bool IsRiskApproved,
    DateTime OccurredOn) : IDomainEvent;
