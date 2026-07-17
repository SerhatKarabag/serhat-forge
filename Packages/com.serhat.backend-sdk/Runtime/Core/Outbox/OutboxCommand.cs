#nullable enable
using System;
using System.Collections.Generic;

namespace Serhat.Backend.Core.Outbox
{
    /// <summary>
    /// Represents a queued command in the outbox.
    /// </summary>
    [Serializable]
    public sealed class OutboxCommand
    {
        public string CommandId { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string FunctionName { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string PayloadTypeName { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public DateTime NextAttemptAtUtc { get; set; }
        public int Priority { get; set; } = 5;
        public OutboxCommandStatus Status { get; set; } = OutboxCommandStatus.Pending;
        public string? LastErrorCode { get; set; }
        public string? LastErrorMessage { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    /// <summary>
    /// Status of an outbox command.
    /// </summary>
    public enum OutboxCommandStatus
    {
        Pending,
        InProgress,
        Completed,
        DeadLetter
    }

    /// <summary>
    /// Dead letter entry with additional failure information.
    /// </summary>
    [Serializable]
    public sealed class DeadLetterEntry
    {
        public OutboxCommand Command { get; set; } = new();
        public string FailureReason { get; set; } = string.Empty;
        public DateTime MovedToDeadLetterAtUtc { get; set; }
        public List<AttemptRecord> AttemptHistory { get; set; } = new();
    }

    /// <summary>
    /// Record of a single attempt.
    /// </summary>
    [Serializable]
    public sealed class AttemptRecord
    {
        public int AttemptNumber { get; set; }
        public DateTime AttemptedAtUtc { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// Persisted state of the outbox.
    /// </summary>
    [Serializable]
    public sealed class OutboxState
    {
        public List<OutboxCommand> PendingCommands { get; set; } = new();
        public List<DeadLetterEntry> DeadLetters { get; set; } = new();
        public DateTime LastModifiedUtc { get; set; }
        public int Version { get; set; } = 1;
    }
}
