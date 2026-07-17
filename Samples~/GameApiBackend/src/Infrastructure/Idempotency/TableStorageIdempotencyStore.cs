using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Infrastructure.Idempotency;

/// <summary>
/// Azure Table Storage implementation of idempotency store.
/// </summary>
public sealed class TableStorageIdempotencyStore : IIdempotencyStore
{
    private readonly TableClient _tableClient;
    private readonly TimeSpan _ttl;
    private readonly ILogger<TableStorageIdempotencyStore> _logger;
    private readonly string _titleId;

    public TableStorageIdempotencyStore(
        string connectionString,
        string tableName,
        string titleId,
        TimeSpan ttl,
        ILogger<TableStorageIdempotencyStore> logger)
    {
        _titleId = titleId;
        _ttl = ttl;
        _logger = logger;

        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(tableName);
        _tableClient.CreateIfNotExists();
    }

    public async Task<IdempotencyGetResult> TryGetAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetKeys(playerId, functionName, idempotencyKey);

        try
        {
            var response = await _tableClient.GetEntityAsync<IdempotencyEntity>(
                partitionKey, rowKey, cancellationToken: ct);

            var entity = response.Value;

            // Check if expired
            if (entity.ExpiresAtUtc < DateTime.UtcNow)
            {
                _logger.LogDebug("Idempotency record expired: {PartitionKey}/{RowKey}", partitionKey, rowKey);
                return new IdempotencyGetResult { Status = IdempotencyStatus.NotFound };
            }

            return new IdempotencyGetResult
            {
                Status = Enum.Parse<IdempotencyStatus>(entity.Status),
                ResponsePayload = entity.ResponsePayload,
                ErrorCode = entity.ErrorCode,
                ErrorMessage = entity.ErrorMessage,
                CreatedAtUtc = entity.CreatedAtUtc,
                CompletedAtUtc = entity.CompletedAtUtc
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new IdempotencyGetResult { Status = IdempotencyStatus.NotFound };
        }
    }

    public async Task<IdempotencyBeginResult> TryBeginAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetKeys(playerId, functionName, idempotencyKey);
        var now = DateTime.UtcNow;

        // First check if exists
        var existing = await TryGetAsync(playerId, functionName, idempotencyKey, ct);
        if (existing.Status != IdempotencyStatus.NotFound)
        {
            return new IdempotencyBeginResult
            {
                Success = false,
                ExistingStatus = existing.Status,
                ExistingResponsePayload = existing.ResponsePayload,
                ErrorCode = existing.ErrorCode,
                ErrorMessage = existing.ErrorMessage
            };
        }

        // Try to insert new record
        var entity = new IdempotencyEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            Status = IdempotencyStatus.InProgress.ToString(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(_ttl)
        };

        try
        {
            await _tableClient.AddEntityAsync(entity, ct);
            return new IdempotencyBeginResult { Success = true };
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Race condition - another request beat us
            _logger.LogDebug("Idempotency conflict: {PartitionKey}/{RowKey}", partitionKey, rowKey);
            existing = await TryGetAsync(playerId, functionName, idempotencyKey, ct);
            return new IdempotencyBeginResult
            {
                Success = false,
                ExistingStatus = existing.Status,
                ExistingResponsePayload = existing.ResponsePayload,
                ErrorCode = existing.ErrorCode,
                ErrorMessage = existing.ErrorMessage
            };
        }
    }

    public async Task CompleteAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        string responsePayload,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetKeys(playerId, functionName, idempotencyKey);

        try
        {
            var response = await _tableClient.GetEntityAsync<IdempotencyEntity>(
                partitionKey, rowKey, cancellationToken: ct);

            var entity = response.Value;
            entity.Status = IdempotencyStatus.Completed.ToString();
            entity.ResponsePayload = responsePayload;
            entity.CompletedAtUtc = DateTime.UtcNow;

            await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to complete idempotency record: {PartitionKey}/{RowKey}",
                partitionKey, rowKey);
            throw;
        }
    }

    public async Task FailAsync(
        string playerId,
        string functionName,
        string idempotencyKey,
        string errorCode,
        string errorMessage,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetKeys(playerId, functionName, idempotencyKey);

        try
        {
            var response = await _tableClient.GetEntityAsync<IdempotencyEntity>(
                partitionKey, rowKey, cancellationToken: ct);

            var entity = response.Value;
            entity.Status = IdempotencyStatus.Failed.ToString();
            entity.ErrorCode = errorCode;
            entity.ErrorMessage = errorMessage;
            entity.CompletedAtUtc = DateTime.UtcNow;

            await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to mark idempotency record as failed: {PartitionKey}/{RowKey}",
                partitionKey, rowKey);
            // Don't throw - failure to record failure shouldn't break the flow
        }
    }

    private (string PartitionKey, string RowKey) GetKeys(string playerId, string functionName, string idempotencyKey)
    {
        return ($"{_titleId}:{functionName}", $"{playerId}:{idempotencyKey}");
    }
}

/// <summary>
/// Table entity for idempotency records.
/// </summary>
public sealed class IdempotencyEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? ResponsePayload { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
