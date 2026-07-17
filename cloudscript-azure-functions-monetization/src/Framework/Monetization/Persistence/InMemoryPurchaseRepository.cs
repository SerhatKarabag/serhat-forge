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
    private readonly object _purchaseLock = new();
    private readonly object _subscriptionLock = new();

    #region Purchase Records

    public Task<PurchaseRecord?> GetPurchaseAsync(string transactionKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_purchaseLock)
        {
            _purchases.TryGetValue(transactionKey, out var record);
            return Task.FromResult(record?.Copy());
        }
    }

    public Task<bool> CreatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        lock (_purchaseLock)
        {
            var success = _purchases.TryAdd(record.TransactionKey, record.Copy());
            return Task.FromResult(success);
        }
    }

    public Task<bool> UpdatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        lock (_purchaseLock)
        {
            if (!_purchases.ContainsKey(record.TransactionKey))
            {
                return Task.FromResult(false);
            }

            _purchases[record.TransactionKey] = record.Copy();
            return Task.FromResult(true);
        }
    }

    public Task<PurchaseClaimResult> TryClaimPurchaseAsync(
        PurchaseRecord candidate,
        string leaseId,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ct.ThrowIfCancellationRequested();

        lock (_purchaseLock)
        {
            if (!_purchases.TryGetValue(candidate.TransactionKey, out var existing))
            {
                var created = candidate.Copy();
                created.AcquireProcessingLease(leaseId, nowUtc, leaseDuration);
                _purchases.AddOrUpdate(created.TransactionKey, created, (_, _) => created);
                return Task.FromResult(new PurchaseClaimResult(true, created.Copy()));
            }

            if (!existing.HasSameImmutableIdentity(candidate) ||
                !existing.CanAcquireProcessingLease(nowUtc))
            {
                return Task.FromResult(new PurchaseClaimResult(false, existing.Copy()));
            }

            var claimed = existing.Copy();
            claimed.AcquireProcessingLease(leaseId, nowUtc, leaseDuration);
            _purchases[claimed.TransactionKey] = claimed;
            return Task.FromResult(new PurchaseClaimResult(true, claimed.Copy()));
        }
    }

    public Task<bool> TryUpdatePurchaseAsync(
        PurchaseRecord record,
        string expectedLeaseId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLeaseId);
        ct.ThrowIfCancellationRequested();

        lock (_purchaseLock)
        {
            if (!_purchases.TryGetValue(record.TransactionKey, out var current) ||
                !string.Equals(
                    current.ProcessingLeaseId,
                    expectedLeaseId,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _purchases[record.TransactionKey] = record.Copy();
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryRenewPurchaseLeaseAsync(
        string transactionKey,
        string expectedLeaseId,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLeaseId);
        ct.ThrowIfCancellationRequested();

        lock (_purchaseLock)
        {
            if (!_purchases.TryGetValue(transactionKey, out var current))
            {
                return Task.FromResult(false);
            }

            var renewed = current.Copy();
            if (!renewed.TryRenewProcessingLease(
                    expectedLeaseId,
                    nowUtc,
                    leaseDuration))
            {
                return Task.FromResult(false);
            }

            _purchases[transactionKey] = renewed;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<PurchaseRecord>> GetPurchasesByPlayerAsync(
        string playerId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_purchaseLock)
        {
            var records = _purchases.Values
                .Where(p => p.PlayerId == playerId)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => p.Copy())
                .ToList();

            return Task.FromResult<IReadOnlyList<PurchaseRecord>>(records);
        }
    }

    #endregion

    #region Subscription Records

    public Task<SubscriptionRecord?> GetSubscriptionAsync(string subscriptionKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_subscriptionLock)
        {
            _subscriptions.TryGetValue(subscriptionKey, out var record);
            return Task.FromResult(record?.Copy());
        }
    }

    public Task<SubscriptionRecord?> GetActiveSubscriptionAsync(string playerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_subscriptionLock)
        {
            var record = _subscriptions.Values
                .Where(s => s.PlayerId == playerId && s.IsActive)
                .OrderByDescending(s => s.TierPrecedence)
                .FirstOrDefault();

            return Task.FromResult(record?.Copy());
        }
    }

    public Task<bool> CreateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        lock (_subscriptionLock)
        {
            var success = _subscriptions.TryAdd(record.SubscriptionKey, record.Copy());
            return Task.FromResult(success);
        }
    }

    public Task<bool> UpdateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        lock (_subscriptionLock)
        {
            if (!_subscriptions.ContainsKey(record.SubscriptionKey))
            {
                return Task.FromResult(false);
            }

            _subscriptions[record.SubscriptionKey] = record.Copy();
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateSubscriptionIfNotNewerAsync(
        SubscriptionRecord record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        lock (_subscriptionLock)
        {
            if (!_subscriptions.TryGetValue(record.SubscriptionKey, out var durable))
            {
                return Task.FromResult(false);
            }

            if (IsDurableSubscriptionNewer(durable, record))
            {
                return Task.FromResult(true);
            }

            _subscriptions[record.SubscriptionKey] = record.Copy();
            return Task.FromResult(true);
        }
    }

    private static bool IsDurableSubscriptionNewer(
        SubscriptionRecord durable,
        SubscriptionRecord candidate) =>
        durable.LastEventAtUtc > candidate.LastEventAtUtc ||
        durable.LastEventAtUtc == candidate.LastEventAtUtc &&
        durable.PeriodEndUtc > candidate.PeriodEndUtc;

    public Task<IReadOnlyList<SubscriptionRecord>> GetSubscriptionsByPlayerAsync(
        string playerId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_subscriptionLock)
        {
            var records = _subscriptions.Values
                .Where(s => s.PlayerId == playerId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .Select(s => s.Copy())
                .ToList();

            return Task.FromResult<IReadOnlyList<SubscriptionRecord>>(records);
        }
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
        lock (_purchaseLock)
        {
            _purchases.Clear();
        }

        lock (_subscriptionLock)
        {
            _subscriptions.Clear();
        }
        _processedWebhooks.Clear();
    }
}
