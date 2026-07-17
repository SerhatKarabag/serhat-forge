namespace Serhat.Forge.CloudScript.Infrastructure.Idempotency;

/// <summary>
/// Interface for idempotency store.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Tries to get an existing idempotency record.
    /// </summary>
    Task<IdempotencyGetResult> TryGetAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>
    /// Tries to begin a new idempotent operation (marks as InProgress).
    /// </summary>
    Task<IdempotencyBeginResult> TryBeginAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>
    /// Marks an operation as completed and stores the response.
    /// </summary>
    Task CompleteAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        string responsePayload,
        CancellationToken ct = default);

    /// <summary>
    /// Marks an operation as failed.
    /// </summary>
    Task FailAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        string errorCode,
        string errorMessage,
        CancellationToken ct = default);
}

/// <summary>
/// Status of an idempotency record.
/// </summary>
public enum IdempotencyStatus
{
    NotFound,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// Result of trying to get an idempotency record.
/// </summary>
public sealed class IdempotencyGetResult
{
    public IdempotencyStatus Status { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>
/// Result of trying to begin an idempotent operation.
/// </summary>
public sealed class IdempotencyBeginResult
{
    public bool Success { get; set; }
    public IdempotencyStatus ExistingStatus { get; set; }
    public string? ExistingResponsePayload { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Entity representing an idempotency record.
/// </summary>
public sealed class IdempotencyRecord
{
    public string PartitionKey { get; set; } = string.Empty; // "{titleId}:{functionName}"
    public string RowKey { get; set; } = string.Empty;       // "{playerId}:{idempotencyKey}"
    public IdempotencyStatus Status { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
