using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Entities;

public sealed class LiveExecutionOrderAttempt : BaseEntity
{
    public Guid BatchId { get; private set; }
    public int Sequence { get; private set; }
    public Guid ClientOrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Symbol { get; private set; } = string.Empty;
    public OrderSide Side { get; private set; }
    public OrderType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal LimitPrice { get; private set; }
    public decimal EstimatedNotional { get; private set; }
    public LiveExecutionAttemptStatus Status { get; private set; } = LiveExecutionAttemptStatus.Pending;
    public string SanitizedRequestJson { get; private set; } = string.Empty;
    public string? SanitizedReviewJson { get; private set; }
    public string? SanitizedResponseJson { get; private set; }
    public string? BrokerOrderId { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public string? FailureReason { get; private set; }
    public List<LiveExecutionOrderEvent> Events { get; private set; } = [];

    public LiveExecutionOrderAttempt() { }

    public LiveExecutionOrderAttempt(
        Guid batchId,
        int sequence,
        Guid clientOrderId,
        string idempotencyKey,
        string symbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal limitPrice,
        decimal estimatedNotional,
        string sanitizedRequestJson,
        string sanitizedReviewJson)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("Batch ID is required.", nameof(batchId));
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (clientOrderId == Guid.Empty) throw new ArgumentException("Client order ID is required.", nameof(clientOrderId));
        if (idempotencyKey.Length != 64) throw new ArgumentException("A SHA-256 idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (quantity <= 0m || limitPrice <= 0m || estimatedNotional <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        BatchId = batchId;
        Sequence = sequence;
        ClientOrderId = clientOrderId;
        IdempotencyKey = idempotencyKey.ToLowerInvariant();
        Symbol = symbol.Trim().ToUpperInvariant();
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        EstimatedNotional = estimatedNotional;
        SanitizedRequestJson = sanitizedRequestJson;
        SanitizedReviewJson = sanitizedReviewJson;
    }

    public void MarkSubmitting(DateTime utcNow)
    {
        if (Status != LiveExecutionAttemptStatus.Pending) throw new InvalidOperationException("Only a pending attempt can be claimed.");
        Status = LiveExecutionAttemptStatus.Submitting;
        AttemptCount++;
        LastAttemptAtUtc = utcNow;
        UpdatedAt = utcNow;
    }

    public void MarkAccepted(string brokerOrderId, string sanitizedResponseJson, DateTime utcNow)
    {
        if (Status != LiveExecutionAttemptStatus.Submitting) throw new InvalidOperationException("Only a submitting attempt can be accepted.");
        BrokerOrderId = brokerOrderId;
        SanitizedResponseJson = sanitizedResponseJson;
        Status = LiveExecutionAttemptStatus.BrokerAccepted;
        UpdatedAt = utcNow;
    }

    public void MarkRejected(string reason, string sanitizedResponseJson, DateTime utcNow)
    {
        FailureReason = Normalize(reason);
        SanitizedResponseJson = sanitizedResponseJson;
        Status = LiveExecutionAttemptStatus.BrokerRejected;
        UpdatedAt = utcNow;
    }

    public void MarkReconciliationRequired(string reason, string? sanitizedResponseJson, DateTime utcNow)
    {
        FailureReason = Normalize(reason);
        SanitizedResponseJson = sanitizedResponseJson;
        Status = LiveExecutionAttemptStatus.ReconciliationRequired;
        UpdatedAt = utcNow;
    }

    public void RecoverBrokerAcceptance(string brokerOrderId, string sanitizedResponseJson, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(brokerOrderId)) throw new ArgumentException("Broker order ID is required.", nameof(brokerOrderId));
        if (BrokerOrderId is not null && !BrokerOrderId.Equals(brokerOrderId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovered broker order ID conflicts with the persisted receipt.");
        BrokerOrderId = brokerOrderId.Trim();
        SanitizedResponseJson = sanitizedResponseJson;
        Status = LiveExecutionAttemptStatus.BrokerAccepted;
        FailureReason = null;
        UpdatedAt = utcNow;
    }

    public void MarkSkipped(string reason, DateTime utcNow)
    {
        if (Status != LiveExecutionAttemptStatus.Pending) return;
        FailureReason = Normalize(reason);
        Status = LiveExecutionAttemptStatus.Skipped;
        UpdatedAt = utcNow;
    }

    public void ApplyBrokerState(string brokerState, decimal filledQuantity, DateTime utcNow)
    {
        if (filledQuantity < 0m || filledQuantity > Quantity + 0.000001m)
            throw new InvalidOperationException("Broker filled quantity is outside the approved order quantity.");
        var state = NormalizeState(brokerState);
        Status = state switch
        {
            "filled" when filledQuantity + 0.000001m >= Quantity => LiveExecutionAttemptStatus.Filled,
            "partiallyfilled" or "partialfill" => LiveExecutionAttemptStatus.PartiallyFilled,
            "cancelpending" or "pendingcancel" => LiveExecutionAttemptStatus.CancelPending,
            "cancelled" or "canceled" => LiveExecutionAttemptStatus.Cancelled,
            "expired" => LiveExecutionAttemptStatus.Expired,
            "rejected" or "failed" => LiveExecutionAttemptStatus.BrokerRejected,
            "open" or "queued" or "pending" or "confirmed" or "unconfirmed" when filledQuantity > 0m
                => LiveExecutionAttemptStatus.PartiallyFilled,
            "open" or "queued" or "pending" or "confirmed" or "unconfirmed"
                => LiveExecutionAttemptStatus.BrokerAccepted,
            _ => LiveExecutionAttemptStatus.ReconciliationRequired
        };
        FailureReason = Status switch
        {
            LiveExecutionAttemptStatus.BrokerRejected => "Robinhood reported the order as rejected or failed.",
            LiveExecutionAttemptStatus.ReconciliationRequired => $"Unrecognized Robinhood order state '{brokerState}' requires manual intervention.",
            _ => null
        };
        UpdatedAt = utcNow;
    }

    public void MarkCancelPending(DateTime utcNow)
    {
        if (Status is not LiveExecutionAttemptStatus.BrokerAccepted and not LiveExecutionAttemptStatus.PartiallyFilled)
            throw new InvalidOperationException("Only an active broker order can enter cancel-pending state.");
        Status = LiveExecutionAttemptStatus.CancelPending;
        UpdatedAt = utcNow;
    }

    private static string NormalizeState(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Normalize(string reason)
    {
        var value = reason.Trim();
        return value.Length <= 1000 ? value : value[..1000];
    }
}
