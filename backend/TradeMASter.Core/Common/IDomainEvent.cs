namespace TradeMASter.Core.Common;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
