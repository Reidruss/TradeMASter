using TradeMASter.Core.Common;

namespace TradeMASter.Core.Entities;

/// <summary>Append-only, sanitized evidence for one observed broker lifecycle state.</summary>
public sealed class LiveExecutionOrderEvent : BaseEntity
{
    public Guid BatchId { get; private set; }
    public Guid AttemptId { get; private set; }
    public string EventKey { get; private set; } = string.Empty;
    public string BrokerOrderId { get; private set; } = string.Empty;
    public string BrokerState { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public decimal FilledQuantity { get; private set; }
    public decimal? AverageFillPrice { get; private set; }
    public DateTime BrokerUpdatedAtUtc { get; private set; }
    public DateTime ObservedAtUtc { get; private set; }
    public string SanitizedPayloadJson { get; private set; } = string.Empty;

    public LiveExecutionOrderEvent() { }

    public LiveExecutionOrderEvent(Guid batchId, Guid attemptId, string eventKey, string brokerOrderId,
        string brokerState, decimal orderedQuantity, decimal filledQuantity, decimal? averageFillPrice,
        DateTime brokerUpdatedAtUtc, DateTime observedAtUtc, string sanitizedPayloadJson)
    {
        if (batchId == Guid.Empty || attemptId == Guid.Empty) throw new ArgumentException("Batch and attempt IDs are required.");
        if (eventKey.Length != 64) throw new ArgumentException("A SHA-256 event key is required.", nameof(eventKey));
        if (string.IsNullOrWhiteSpace(brokerOrderId) || string.IsNullOrWhiteSpace(brokerState))
            throw new ArgumentException("Broker order identity and state are required.");
        if (orderedQuantity <= 0m || filledQuantity < 0m || filledQuantity > orderedQuantity + 0.000001m)
            throw new ArgumentOutOfRangeException(nameof(filledQuantity));
        BatchId = batchId;
        AttemptId = attemptId;
        EventKey = eventKey.ToLowerInvariant();
        BrokerOrderId = brokerOrderId.Trim();
        BrokerState = brokerState.Trim();
        OrderedQuantity = orderedQuantity;
        FilledQuantity = filledQuantity;
        AverageFillPrice = averageFillPrice;
        BrokerUpdatedAtUtc = DateTime.SpecifyKind(brokerUpdatedAtUtc, DateTimeKind.Utc);
        ObservedAtUtc = DateTime.SpecifyKind(observedAtUtc, DateTimeKind.Utc);
        SanitizedPayloadJson = sanitizedPayloadJson;
    }
}
