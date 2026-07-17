using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Persistence;

/// <summary>
/// In-memory implementation of purchase repository.
/// For testing and development only - not for production use.
/// </summary>
public sealed class InMemoryPurchaseRepository : IPurchaseRepository
{
    private readonly ConcurrentDictionary<string, PurchaseRecord> _purchases = new();
    private readonly ConcurrentDictionary<string, SubscriptionRecord> _subscriptions = new();
    private readonly ConcurrentDictionary<string, WebhookProcessingState> _processedWebhooks = new();

    #region Purchase Records

    public Task<PurchaseRecord?> GetPurchaseAsync(string transactionKey, CancellationToken ct = default)
    {
        _purchases.TryGetValue(transactionKey, out var record);
        return Task.FromResult(record);
    }

    public Task<bool> CreatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default)
    {
        var success = _purchases.TryAdd(record.TransactionKey, record);
        return Task.FromResult(success);
    }

    public Task<bool> UpdatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default)
    {
        if (!_purchases.ContainsKey(record.TransactionKey))
        {
            return Task.FromResult(false);
        }

        _purchases[record.TransactionKey] = record;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<PurchaseRecord>> GetPurchasesByPlayerAsync(
        string playerId,
        CancellationToken ct = default)
    {
        var records = _purchases.Values
            .Where(p => p.PlayerId == playerId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<PurchaseRecord>>(records);
    }

    #endregion

    #region Subscription Records

    public Task<SubscriptionRecord?> GetSubscriptionAsync(string subscriptionKey, CancellationToken ct = default)
    {
        _subscriptions.TryGetValue(subscriptionKey, out var record);
        return Task.FromResult(record);
    }

    public Task<SubscriptionRecord?> GetActiveSubscriptionAsync(string playerId, CancellationToken ct = default)
    {
        var record = _subscriptions.Values
            .Where(s => s.PlayerId == playerId && s.IsActive)
            .OrderByDescending(s => s.TierPrecedence)
            .FirstOrDefault();

        return Task.FromResult(record);
    }

    public Task<bool> CreateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        var success = _subscriptions.TryAdd(record.SubscriptionKey, record);
        return Task.FromResult(success);
    }

    public Task<bool> UpdateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        if (!_subscriptions.ContainsKey(record.SubscriptionKey))
        {
            return Task.FromResult(false);
        }

        _subscriptions[record.SubscriptionKey] = record;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SubscriptionRecord>> GetSubscriptionsByPlayerAsync(
        string playerId,
        CancellationToken ct = default)
    {
        var records = _subscriptions.Values
            .Where(s => s.PlayerId == playerId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<SubscriptionRecord>>(records);
    }

    #endregion

    #region Webhook Dedup

    public Task<bool> TryBeginWebhookProcessingAsync(string eventId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var claimed = _processedWebhooks.TryAdd(eventId, WebhookProcessingState.Processing);
        return Task.FromResult(claimed);
    }

    public Task CompleteWebhookProcessingAsync(string eventId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _processedWebhooks[eventId] = WebhookProcessingState.Completed;
        return Task.CompletedTask;
    }

    public Task AbandonWebhookProcessingAsync(string eventId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _processedWebhooks.TryRemove(eventId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> HasProcessedWebhookAsync(string eventId, CancellationToken ct = default)
    {
        return Task.FromResult(_processedWebhooks.TryGetValue(eventId, out var state) && state == WebhookProcessingState.Completed);
    }

    public Task MarkWebhookProcessedAsync(string eventId, CancellationToken ct = default)
    {
        _processedWebhooks[eventId] = WebhookProcessingState.Completed;
        return Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// Clears all data. For testing only.
    /// </summary>
    private enum WebhookProcessingState
    {
        Processing,
        Completed
    }

    public void Clear()
    {
        _purchases.Clear();
        _subscriptions.Clear();
        _processedWebhooks.Clear();
    }
}
