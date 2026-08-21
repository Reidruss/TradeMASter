namespace TradeMASter.Core.Enums;

public enum LiveExecutionBatchStatus
{
    PreflightPassed = 0,
    SubmissionBlocked = 1,
    Submitting = 2,
    Submitted = 3,
    Failed = 4,
    ReconciliationRequired = 5
}

public enum LiveExecutionAttemptStatus
{
    Pending = 0,
    Submitting = 1,
    BrokerAccepted = 2,
    BrokerRejected = 3,
    ReconciliationRequired = 4,
    Skipped = 5
}

public enum BrokerSubmissionOutcome
{
    Accepted = 0,
    Rejected = 1,
    Unknown = 2
}
