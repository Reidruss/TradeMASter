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

    public void MarkSkipped(string reason, DateTime utcNow)
    {
        if (Status != LiveExecutionAttemptStatus.Pending) return;
        FailureReason = Normalize(reason);
        Status = LiveExecutionAttemptStatus.Skipped;
        UpdatedAt = utcNow;
    }

    private static string Normalize(string reason)
    {
        var value = reason.Trim();
        return value.Length <= 1000 ? value : value[..1000];
    }
}
