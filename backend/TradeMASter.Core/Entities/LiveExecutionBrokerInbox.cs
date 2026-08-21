using TradeMASter.Core.Common;

namespace TradeMASter.Core.Entities;

/// <summary>
/// Durable, sanitized evidence that Robinhood accepted an order submission.
/// Unique client and broker order identifiers make receipt ingestion idempotent.
/// </summary>
public sealed class LiveExecutionBrokerInbox : BaseEntity
{
    public Guid BatchId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid ClientOrderId { get; private set; }
    public string BrokerOrderId { get; private set; } = string.Empty;
    public string BrokerState { get; private set; } = string.Empty;
    public string SanitizedPayloadJson { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }

    public LiveExecutionBrokerInbox() { }

    public LiveExecutionBrokerInbox(
        Guid batchId,
        Guid attemptId,
        Guid clientOrderId,
        string brokerOrderId,
        string brokerState,
        string sanitizedPayloadJson,
        DateTime receivedAtUtc)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("Batch ID is required.", nameof(batchId));
        if (attemptId == Guid.Empty) throw new ArgumentException("Attempt ID is required.", nameof(attemptId));
        if (clientOrderId == Guid.Empty) throw new ArgumentException("Client order ID is required.", nameof(clientOrderId));
        if (string.IsNullOrWhiteSpace(brokerOrderId)) throw new ArgumentException("Broker order ID is required.", nameof(brokerOrderId));
        if (string.IsNullOrWhiteSpace(brokerState)) throw new ArgumentException("Broker state is required.", nameof(brokerState));
        if (string.IsNullOrWhiteSpace(sanitizedPayloadJson)) throw new ArgumentException("A sanitized broker payload is required.", nameof(sanitizedPayloadJson));

        BatchId = batchId;
        AttemptId = attemptId;
        ClientOrderId = clientOrderId;
        BrokerOrderId = brokerOrderId.Trim();
        BrokerState = brokerState.Trim();
        SanitizedPayloadJson = sanitizedPayloadJson;
        ReceivedAtUtc = DateTime.SpecifyKind(receivedAtUtc, DateTimeKind.Utc);
    }
}
