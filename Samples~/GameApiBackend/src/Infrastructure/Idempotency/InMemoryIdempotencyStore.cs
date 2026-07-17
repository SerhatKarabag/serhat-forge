using System.Collections.Concurrent;

namespace Serhat.Forge.CloudScript.Infrastructure.Idempotency;

/// <summary>
/// In-memory idempotency store for local development.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new();
    private readonly TimeSpan _ttl = TimeSpan.FromHours(24);

    public Task<IdempotencyGetResult> TryGetAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var key = GetKey(playerId, functionName, idempotencyKey);

        if (!_records.TryGetValue(key, out var record))
        {
            return Task.FromResult(new IdempotencyGetResult { Status = IdempotencyStatus.NotFound });
        }

        if (record.ExpiresAtUtc < DateTime.UtcNow)
        {
            _records.TryRemove(key, out _);
            return Task.FromResult(new IdempotencyGetResult { Status = IdempotencyStatus.NotFound });
        }

        return Task.FromResult(new IdempotencyGetResult
        {
            Status = record.Status,
            ResponsePayload = record.ResponsePayload,
            ErrorCode = record.ErrorCode,
            ErrorMessage = record.ErrorMessage,
            CreatedAtUtc = record.CreatedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc
        });
    }

    public Task<IdempotencyBeginResult> TryBeginAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var key = GetKey(playerId, functionName, idempotencyKey);
        var now = DateTime.UtcNow;

        var newRecord = new IdempotencyRecord
        {
            PartitionKey = functionName,
            RowKey = $"{playerId}:{idempotencyKey}",
            Status = IdempotencyStatus.InProgress,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(_ttl)
        };

        if (_records.TryAdd(key, newRecord))
        {
            return Task.FromResult(new IdempotencyBeginResult { Success = true });
        }

        // Already exists
        if (_records.TryGetValue(key, out var existing))
        {
            if (existing.ExpiresAtUtc < DateTime.UtcNow)
            {
                // Expired, replace it
                _records[key] = newRecord;
                return Task.FromResult(new IdempotencyBeginResult { Success = true });
            }

            return Task.FromResult(new IdempotencyBeginResult
            {
                Success = false,
                ExistingStatus = existing.Status,
                ExistingResponsePayload = existing.ResponsePayload,
                ErrorCode = existing.ErrorCode,
                ErrorMessage = existing.ErrorMessage
            });
        }

        // Race condition, try again
        return TryBeginAsync(playerId, functionName, idempotencyKey, ct);
    }

    public Task CompleteAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        string responsePayload,
        CancellationToken ct = default)
    {
        var key = GetKey(playerId, functionName, idempotencyKey);

        if (_records.TryGetValue(key, out var record))
        {
            record.Status = IdempotencyStatus.Completed;
            record.ResponsePayload = responsePayload;
            record.CompletedAtUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        string errorCode,
        string errorMessage,
        CancellationToken ct = default)
    {
        var key = GetKey(playerId, functionName, idempotencyKey);

        if (_records.TryGetValue(key, out var record))
        {
            record.Status = IdempotencyStatus.Failed;
            record.ErrorCode = errorCode;
            record.ErrorMessage = errorMessage;
            record.CompletedAtUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    private static string GetKey(string playerId, string functionName, string idempotencyKey) =>
        $"{functionName}:{playerId}:{idempotencyKey}";
}
