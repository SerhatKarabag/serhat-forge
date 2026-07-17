using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Persistence;

/// <summary>
/// Azure Table Storage implementation of purchase repository.
/// </summary>
public sealed class TableStoragePurchaseRepository : IPurchaseRepository
{
    private readonly TableClient _purchasesTable;
    private readonly TableClient _subscriptionsTable;
    private readonly TableClient _webhooksTable;
    private readonly string _titleId;
    private readonly ILogger<TableStoragePurchaseRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TableStoragePurchaseRepository(
        string connectionString,
        string titleId,
        ILogger<TableStoragePurchaseRepository> logger)
    {
        _titleId = titleId;
        _logger = logger;

        var serviceClient = new TableServiceClient(connectionString);

        _purchasesTable = serviceClient.GetTableClient($"{titleId}Purchases");
        _purchasesTable.CreateIfNotExists();

        _subscriptionsTable = serviceClient.GetTableClient($"{titleId}Subscriptions");
        _subscriptionsTable.CreateIfNotExists();

        _webhooksTable = serviceClient.GetTableClient($"{titleId}WebhookEvents");
        _webhooksTable.CreateIfNotExists();
    }

    #region Purchase Records

    public async Task<PurchaseRecord?> GetPurchaseAsync(string transactionKey, CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetPurchaseKeys(transactionKey);

        try
        {
            var response = await _purchasesTable.GetEntityAsync<PurchaseEntity>(
                partitionKey, rowKey, cancellationToken: ct);

            return response.Value.ToRecord();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> CreatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default)
    {
        var entity = PurchaseEntity.FromRecord(record, _titleId);
        (entity.PartitionKey, entity.RowKey) = GetPurchaseKeys(record.TransactionKey);

        try
        {
            await _purchasesTable.AddEntityAsync(entity, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogDebug("Purchase record already exists");
            return false;
        }
    }

    public async Task<bool> UpdatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default)
    {
        var entity = PurchaseEntity.FromRecord(record, _titleId);
        (entity.PartitionKey, entity.RowKey) = GetPurchaseKeys(record.TransactionKey);

        try
        {
            await _purchasesTable.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Purchase record was not found for update");
            return false;
        }
    }

    public async Task<IReadOnlyList<PurchaseRecord>> GetPurchasesByPlayerAsync(
        string playerId,
        CancellationToken ct = default)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {_titleId} and PlayerId eq {playerId}");
        var records = new List<PurchaseRecord>();

        await foreach (var entity in _purchasesTable.QueryAsync<PurchaseEntity>(filter, cancellationToken: ct))
        {
            records.Add(entity.ToRecord());
        }

        return records.OrderByDescending(r => r.CreatedAtUtc).ToList();
    }

    #endregion

    #region Subscription Records

    public async Task<SubscriptionRecord?> GetSubscriptionAsync(string subscriptionKey, CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetSubscriptionKeys(subscriptionKey);

        try
        {
            var response = await _subscriptionsTable.GetEntityAsync<SubscriptionEntity>(
                partitionKey, rowKey, cancellationToken: ct);

            return response.Value.ToRecord();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<SubscriptionRecord?> GetActiveSubscriptionAsync(string playerId, CancellationToken ct = default)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {_titleId} and PlayerId eq {playerId}");
        SubscriptionRecord? best = null;

        await foreach (var entity in _subscriptionsTable.QueryAsync<SubscriptionEntity>(filter, cancellationToken: ct))
        {
            var record = entity.ToRecord();
            if (record.IsActive && (best == null || record.TierPrecedence > best.TierPrecedence))
            {
                best = record;
            }
        }

        return best;
    }

    public async Task<bool> CreateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        var entity = SubscriptionEntity.FromRecord(record, _titleId);
        (entity.PartitionKey, entity.RowKey) = GetSubscriptionKeys(record.SubscriptionKey);

        try
        {
            await _subscriptionsTable.AddEntityAsync(entity, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogDebug("Subscription record already exists");
            return false;
        }
    }

    public async Task<bool> UpdateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        var entity = SubscriptionEntity.FromRecord(record, _titleId);
        (entity.PartitionKey, entity.RowKey) = GetSubscriptionKeys(record.SubscriptionKey);

        try
        {
            await _subscriptionsTable.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Subscription record was not found for update");
            return false;
        }
    }

    public async Task<IReadOnlyList<SubscriptionRecord>> GetSubscriptionsByPlayerAsync(
        string playerId,
        CancellationToken ct = default)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {_titleId} and PlayerId eq {playerId}");
        var records = new List<SubscriptionRecord>();

        await foreach (var entity in _subscriptionsTable.QueryAsync<SubscriptionEntity>(filter, cancellationToken: ct))
        {
            records.Add(entity.ToRecord());
        }

        return records.OrderByDescending(r => r.CreatedAtUtc).ToList();
    }

    #endregion

    #region Webhook Dedup

    public async Task<bool> TryBeginWebhookProcessingAsync(
        string eventId,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetWebhookKeys(eventId);
        var now = DateTime.UtcNow;
        var entity = new WebhookEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            Status = WebhookProcessingStatus.Processing,
            ClaimedAtUtc = now
        };

        try
        {
            await _webhooksTable.AddEntityAsync(entity, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            try
            {
                var existingResponse = await _webhooksTable.GetEntityAsync<WebhookEntity>(
                    partitionKey,
                    rowKey,
                    cancellationToken: ct);
                var existing = existingResponse.Value;
                if (existing.Status == WebhookProcessingStatus.Completed ||
                    existing.ProcessedAtUtc != default ||
                    existing.ClaimedAtUtc > now.AddMinutes(-5))
                {
                    return false;
                }

                existing.Status = WebhookProcessingStatus.Processing;
                existing.ClaimedAtUtc = now;
                await _webhooksTable.UpdateEntityAsync(
                    existing,
                    existing.ETag,
                    TableUpdateMode.Replace,
                    ct);
                return true;
            }
            catch (RequestFailedException race) when (race.Status is 404 or 412)
            {
                return false;
            }
        }
    }

    public async Task CompleteWebhookProcessingAsync(
        string eventId,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetWebhookKeys(eventId);
        var entity = new WebhookEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            Status = WebhookProcessingStatus.Completed,
            ProcessedAtUtc = DateTime.UtcNow
        };

        await _webhooksTable.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
    }

    public async Task AbandonWebhookProcessingAsync(
        string eventId,
        CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetWebhookKeys(eventId);
        try
        {
            var response = await _webhooksTable.GetEntityAsync<WebhookEntity>(
                partitionKey,
                rowKey,
                cancellationToken: ct);
            if (response.Value.Status != WebhookProcessingStatus.Completed &&
                response.Value.ProcessedAtUtc == default)
            {
                await _webhooksTable.DeleteEntityAsync(
                    partitionKey,
                    rowKey,
                    response.Value.ETag,
                    ct);
            }
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
            // Another worker completed or released the claim.
        }
    }
    public async Task<bool> HasProcessedWebhookAsync(string eventId, CancellationToken ct = default)
    {
        var (partitionKey, rowKey) = GetWebhookKeys(eventId);

        try
        {
            var response = await _webhooksTable.GetEntityAsync<WebhookEntity>(partitionKey, rowKey, cancellationToken: ct);
            return response.Value.Status == WebhookProcessingStatus.Completed ||
                   response.Value.ProcessedAtUtc != default;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public Task MarkWebhookProcessedAsync(string eventId, CancellationToken ct = default) =>
        CompleteWebhookProcessingAsync(eventId, ct);
    #endregion

    #region Key Generation

    private (string PartitionKey, string RowKey) GetPurchaseKeys(string transactionKey) =>
        (_titleId, CreateStorageSafeKey(transactionKey));

    private (string PartitionKey, string RowKey) GetSubscriptionKeys(string subscriptionKey) =>
        (_titleId, CreateStorageSafeKey(subscriptionKey));

    private static string CreateStorageSafeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private (string PartitionKey, string RowKey) GetWebhookKeys(string eventId)
    {
        // Deterministic across dates so delayed replays resolve to the same entity.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventId)));
        return ($"{_titleId}:webhooks:{hash[..2]}", hash);
    }

    #endregion
}

#region Table Entities

/// <summary>
/// Table entity for purchase records.
/// </summary>
public sealed class PurchaseEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string TransactionKey { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? GrantedEconomyItemIdsJson { get; set; }
    public int QuantityGranted { get; set; }
    public string? TierKey { get; set; }
    public string? CachedResponseJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string StoreTransactionId { get; set; } = string.Empty;
    public string? OriginalTransactionId { get; set; }

    public static PurchaseEntity FromRecord(PurchaseRecord record, string titleId)
    {
        return new PurchaseEntity
        {
            PartitionKey = titleId,
            RowKey = record.TransactionKey,
            TransactionKey = record.TransactionKey,
            Platform = record.Platform,
            ProductId = record.ProductId,
            ProductType = record.ProductType.ToString(),
            PlayerId = record.PlayerId,
            Status = record.Status.ToString(),
            GrantedEconomyItemIdsJson = record.GrantedEconomyItemIds.Count > 0
                ? JsonSerializer.Serialize(record.GrantedEconomyItemIds)
                : null,
            QuantityGranted = record.QuantityGranted,
            TierKey = record.TierKey,
            CachedResponseJson = record.CachedResponseJson,
            ErrorCode = record.ErrorCode,
            ErrorMessage = record.ErrorMessage,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            StoreTransactionId = record.StoreTransactionId,
            OriginalTransactionId = record.OriginalTransactionId
        };
    }

    public PurchaseRecord ToRecord()
    {
        return new PurchaseRecord
        {
            TransactionKey = TransactionKey,
            Platform = Platform,
            ProductId = ProductId,
            ProductType = Enum.Parse<Domain.ProductType>(ProductType),
            PlayerId = PlayerId,
            Status = Enum.Parse<PurchaseStatus>(Status),
            GrantedEconomyItemIds = !string.IsNullOrEmpty(GrantedEconomyItemIdsJson)
                ? JsonSerializer.Deserialize<List<string>>(GrantedEconomyItemIdsJson) ?? new List<string>()
                : new List<string>(),
            QuantityGranted = QuantityGranted,
            TierKey = TierKey,
            CachedResponseJson = CachedResponseJson,
            ErrorCode = ErrorCode,
            ErrorMessage = ErrorMessage,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            StoreTransactionId = StoreTransactionId,
            OriginalTransactionId = OriginalTransactionId
        };
    }
}

/// <summary>
/// Table entity for subscription records.
/// </summary>
public sealed class SubscriptionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string SubscriptionKey { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string TierKey { get; set; } = string.Empty;
    public int TierPrecedence { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ActiveEconomyItemId { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime OriginalPurchaseDateUtc { get; set; }
    public DateTime LastEventAtUtc { get; set; }
    public string? PendingTierKey { get; set; }
    public string? PendingProductId { get; set; }
    public DateTime? GracePeriodEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public static SubscriptionEntity FromRecord(SubscriptionRecord record, string titleId)
    {
        return new SubscriptionEntity
        {
            // All subscription point lookups and player queries use the title partition.
            // Keeping entity creation on the same partition is critical; a per-player
            // partition makes GetSubscriptionAsync(subscriptionKey) unable to locate rows.
            PartitionKey = titleId,
            RowKey = record.SubscriptionKey,
            SubscriptionKey = record.SubscriptionKey,
            Platform = record.Platform,
            PlayerId = record.PlayerId,
            ProductId = record.ProductId,
            TierKey = record.TierKey,
            TierPrecedence = record.TierPrecedence,
            Status = record.Status.ToString(),
            ActiveEconomyItemId = record.ActiveEconomyItemId,
            AutoRenew = record.AutoRenew,
            PeriodStartUtc = record.PeriodStartUtc,
            PeriodEndUtc = record.PeriodEndUtc,
            OriginalPurchaseDateUtc = record.OriginalPurchaseDateUtc,
            LastEventAtUtc = record.LastEventAtUtc,
            PendingTierKey = record.PendingTierKey,
            PendingProductId = record.PendingProductId,
            GracePeriodEndUtc = record.GracePeriodEndUtc,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    public SubscriptionRecord ToRecord()
    {
        return new SubscriptionRecord
        {
            SubscriptionKey = SubscriptionKey,
            Platform = Platform,
            PlayerId = PlayerId,
            ProductId = ProductId,
            TierKey = TierKey,
            TierPrecedence = TierPrecedence,
            Status = Enum.Parse<SubscriptionStatus>(Status),
            ActiveEconomyItemId = ActiveEconomyItemId,
            AutoRenew = AutoRenew,
            PeriodStartUtc = PeriodStartUtc,
            PeriodEndUtc = PeriodEndUtc,
            OriginalPurchaseDateUtc = OriginalPurchaseDateUtc,
            LastEventAtUtc = LastEventAtUtc,
            PendingTierKey = PendingTierKey,
            PendingProductId = PendingProductId,
            GracePeriodEndUtc = GracePeriodEndUtc,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}

/// <summary>
/// Table entity for webhook deduplication.
/// </summary>
internal static class WebhookProcessingStatus
{
    public const string Processing = "Processing";
    public const string Completed = "Completed";
}
public sealed class WebhookEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

#endregion
