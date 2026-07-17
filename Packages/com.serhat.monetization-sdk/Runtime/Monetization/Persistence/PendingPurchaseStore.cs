#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;
using UnityEngine;

namespace Serhat.Backend.Monetization.Persistence
{
    /// <summary>
    /// Represents a pending purchase awaiting server verification.
    /// </summary>
    [Serializable]
    public sealed class PendingPurchase
    {
        public string ProductId = string.Empty;
        public string Platform = string.Empty;
        public string TransactionId = string.Empty;
        public string ReceiptPayload = string.Empty;
        public string ProductType = string.Empty;
        public string? TierKey;
        public long CreatedAtMs;
        public int RetryCount;
        public string? LastError;

        // Metadata serialized as JSON
        public string? MetadataJson;

        public PendingPurchase() { }

        public PendingPurchase(StoreReceipt receipt, ProductDefinition? productDef, long createdAtMs)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            EnsureRequiredReceiptData(receipt);

            ProductId = receipt.ProductId;
            Platform = receipt.Platform;
            TransactionId = receipt.TransactionId;
            // Apple server verification uses only the transaction ID. Never persist a raw App
            // Store receipt/JWS even if a custom store adapter accidentally supplies one.
            ReceiptPayload = IsApple(receipt.Platform) ? string.Empty : receipt.ReceiptPayload;
            ProductType = productDef?.Type.ToString() ?? "Unknown";
            TierKey = productDef?.TierKey;
            CreatedAtMs = createdAtMs;
            RetryCount = 0;
            LastError = null;

            if (receipt.Metadata.Count > 0)
            {
                MetadataJson = JsonUtility.ToJson(new SerializableDictionary(receipt.Metadata));
            }
        }

        internal PendingPurchase Copy() =>
            new()
            {
                ProductId = ProductId ?? string.Empty,
                Platform = Platform ?? string.Empty,
                TransactionId = TransactionId ?? string.Empty,
                ReceiptPayload = IsApple(Platform) ? string.Empty : ReceiptPayload ?? string.Empty,
                ProductType = ProductType ?? string.Empty,
                TierKey = TierKey,
                CreatedAtMs = CreatedAtMs,
                RetryCount = RetryCount,
                LastError = LastError,
                MetadataJson = MetadataJson
            };

        public Dictionary<string, string> GetMetadata()
        {
            if (string.IsNullOrEmpty(MetadataJson))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                var dict = JsonUtility.FromJson<SerializableDictionary>(MetadataJson);
                return dict?.ToDictionary() ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        internal bool HasRequiredData() =>
            !string.IsNullOrWhiteSpace(ProductId) &&
            !string.IsNullOrWhiteSpace(Platform) &&
            !string.IsNullOrWhiteSpace(TransactionId) &&
            (IsApple(Platform) || !string.IsNullOrWhiteSpace(ReceiptPayload));

        private static void EnsureRequiredReceiptData(StoreReceipt receipt)
        {
            if (string.IsNullOrWhiteSpace(receipt.ProductId) ||
                string.IsNullOrWhiteSpace(receipt.Platform) ||
                string.IsNullOrWhiteSpace(receipt.TransactionId) ||
                (!IsApple(receipt.Platform) &&
                 string.IsNullOrWhiteSpace(receipt.ReceiptPayload)))
            {
                throw new ArgumentException(
                    "Receipt must include product, platform, transaction, and the platform-required payload.",
                    nameof(receipt));
            }
        }

        private static bool IsApple(string? platform) =>
            string.Equals(platform, "apple", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Helper for serializing dictionaries with JsonUtility.
    /// </summary>
    [Serializable]
    internal sealed class SerializableDictionary
    {
        public List<string> Keys = new();
        public List<string> Values = new();

        public SerializableDictionary() { }

        public SerializableDictionary(Dictionary<string, string> dict)
        {
            foreach (var kvp in dict)
            {
                Keys.Add(kvp.Key);
                Values.Add(kvp.Value);
            }
        }

        public Dictionary<string, string> ToDictionary()
        {
            var result = new Dictionary<string, string>();
            if (Keys == null || Values == null)
            {
                return result;
            }

            for (int i = 0; i < Keys.Count && i < Values.Count; i++)
            {
                var key = Keys[i];
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = Values[i] ?? string.Empty;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Container for persisted pending purchases.
    /// </summary>
    [Serializable]
    internal sealed class PendingPurchaseContainer
    {
        public List<PendingPurchase> Purchases = new();
    }

    /// <summary>
    /// Persistent storage for pending purchases.
    /// Ensures crash-safe handling of purchases awaiting verification.
    /// Uses the injected local storage for synchronous persistence (pending purchases must survive crashes).
    /// </summary>
    public sealed class PendingPurchaseStore
    {
        private const string StorageKey = "SerhatBackendSdk_PendingPurchases";
        private const long RetryBackoffBaseMs = 5000; // 5 seconds
        private const long MaxRetryBackoffMs = 30 * 60 * 1000; // 30 minutes

        private readonly IStorage _storage;
        private readonly IClock _clock;
        private readonly object _lock = new();
        private PendingPurchaseContainer _container;

        public PendingPurchaseStore(IStorage storage, IClock clock)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _container = Load();
        }

        /// <summary>
        /// Gets all pending purchases.
        /// </summary>
        public IReadOnlyList<PendingPurchase> GetAll()
        {
            lock (_lock)
            {
                var snapshot = new PendingPurchase[_container.Purchases.Count];
                for (var i = 0; i < snapshot.Length; i++)
                {
                    snapshot[i] = _container.Purchases[i].Copy();
                }

                return snapshot;
            }
        }

        /// <summary>
        /// Gets pending purchases ready for retry.
        /// </summary>
        public IReadOnlyList<PendingPurchase> GetReadyForRetry()
        {
            lock (_lock)
            {
                var now = _clock.TimestampMs;
                var ready = new List<PendingPurchase>();

                foreach (var purchase in _container.Purchases)
                {
                    if (!purchase.HasRequiredData())
                    {
                        continue;
                    }

                    var backoffMs = CalculateBackoff(purchase.RetryCount);
                    var nextRetryAt = AddWithoutOverflow(purchase.CreatedAtMs, backoffMs);

                    if (now >= nextRetryAt)
                    {
                        ready.Add(purchase.Copy());
                    }
                }

                return ready;
            }
        }

        /// <summary>
        /// Gets the bounded delay until the next valid pending purchase should be retried,
        /// or null when no valid pending purchase exists.
        /// </summary>
        internal TimeSpan? GetTimeUntilNextRetry()
        {
            lock (_lock)
            {
                var now = _clock.TimestampMs;
                long? shortestDelayMs = null;

                foreach (var purchase in _container.Purchases)
                {
                    if (!purchase.HasRequiredData())
                    {
                        continue;
                    }

                    var nextRetryAt = AddWithoutOverflow(
                        purchase.CreatedAtMs,
                        CalculateBackoff(purchase.RetryCount));
                    var retryWindowEnd = AddWithoutOverflow(now, MaxRetryBackoffMs);
                    var remainingMs = nextRetryAt <= now
                        ? 0
                        : nextRetryAt >= retryWindowEnd
                            ? MaxRetryBackoffMs
                            : nextRetryAt - now;

                    if (!shortestDelayMs.HasValue || remainingMs < shortestDelayMs.Value)
                    {
                        shortestDelayMs = remainingMs;
                    }
                }

                return shortestDelayMs.HasValue
                    ? TimeSpan.FromMilliseconds(shortestDelayMs.Value)
                    : null;
            }
        }

        /// <summary>
        /// Adds a pending purchase.
        /// </summary>
        public void Add(StoreReceipt receipt, ProductDefinition? productDef)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            lock (_lock)
            {
                // Check for duplicates
                var existing = _container.Purchases.Find(p =>
                    p.TransactionId == receipt.TransactionId &&
                    p.Platform == receipt.Platform);

                if (existing != null)
                {
                    return; // Already pending
                }

                var pending = new PendingPurchase(receipt, productDef, _clock.TimestampMs);
                _container.Purchases.Add(pending);
                try
                {
                    Save();
                }
                catch
                {
                    _container.Purchases.Remove(pending);
                    throw;
                }
            }
        }

        /// <summary>
        /// Marks a pending purchase as successfully verified and removes it.
        /// </summary>
        public void Complete(string transactionId, string platform)
        {
            lock (_lock)
            {
                var removed = _container.Purchases.FindAll(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);
                if (removed.Count == 0)
                {
                    return;
                }

                _container.Purchases.RemoveAll(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);
                try
                {
                    Save();
                }
                catch
                {
                    _container.Purchases.AddRange(removed);
                    throw;
                }
            }
        }

        /// <summary>
        /// Marks a retry attempt with error.
        /// </summary>
        public void MarkRetryFailed(string transactionId, string platform, string? error)
        {
            lock (_lock)
            {
                var purchase = _container.Purchases.Find(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);

                if (purchase != null)
                {
                    var previousRetryCount = purchase.RetryCount;
                    var previousLastError = purchase.LastError;
                    var previousCreatedAtMs = purchase.CreatedAtMs;
                    if (purchase.RetryCount < int.MaxValue)
                    {
                        purchase.RetryCount++;
                    }
                    purchase.LastError = error;
                    purchase.CreatedAtMs = _clock.TimestampMs; // Reset timer for backoff
                    try
                    {
                        Save();
                    }
                    catch
                    {
                        purchase.RetryCount = previousRetryCount;
                        purchase.LastError = previousLastError;
                        purchase.CreatedAtMs = previousCreatedAtMs;
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Removes a pending purchase that permanently failed.
        /// </summary>
        public void Remove(string transactionId, string platform)
        {
            lock (_lock)
            {
                var removed = _container.Purchases.FindAll(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);
                if (removed.Count == 0)
                {
                    return;
                }

                _container.Purchases.RemoveAll(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);
                try
                {
                    Save();
                }
                catch
                {
                    _container.Purchases.AddRange(removed);
                    throw;
                }
            }
        }

        /// <summary>
        /// Clears all pending purchases (for testing/reset).
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                var previous = new List<PendingPurchase>(_container.Purchases);
                _container.Purchases.Clear();
                try
                {
                    Save();
                }
                catch
                {
                    _container.Purchases.AddRange(previous);
                    throw;
                }
            }
        }

        /// <summary>
        /// Gets the count of pending purchases.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _container.Purchases.Count;
                }
            }
        }

        private PendingPurchaseContainer Load()
        {
            try
            {
                // Invoke async storage on a context-free worker before synchronously joining.
                // A durable receipt must be persisted before verification continues, and calling
                // an arbitrary async IStorage directly from Unity's synchronization context can
                // deadlock when that implementation captures the caller context.
                var json = Task
                    .Run(() => _storage.ReadAsync(StorageKey))
                    .GetAwaiter()
                    .GetResult();
                if (!string.IsNullOrEmpty(json))
                {
                    var container = JsonUtility.FromJson<PendingPurchaseContainer>(json)
                                    ?? new PendingPurchaseContainer();
                    container.Purchases ??= new List<PendingPurchase>();

                    var invalidCount = container.Purchases.RemoveAll(purchase =>
                        purchase == null || !purchase.HasRequiredData());
                    if (invalidCount > 0)
                    {
                        Debug.LogWarning(
                            $"[PendingPurchaseStore] Ignored {invalidCount} invalid persisted record(s).");
                    }

                    return container;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PendingPurchaseStore] Failed to load: {ex.Message}");
            }

            return new PendingPurchaseContainer();
        }

        private void Save()
        {
            try
            {
                var json = JsonUtility.ToJson(_container);
                Task
                    .Run(() => _storage.WriteAsync(StorageKey, json))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PendingPurchaseStore] Failed to save: {ex.Message}");
                throw new InvalidOperationException(
                    "Pending purchase state could not be persisted.",
                    ex);
            }
        }

        private static long CalculateBackoff(int retryCount)
        {
            var boundedExponent = Math.Min(Math.Max(retryCount, 0), 20);
            var exponentialDelay = RetryBackoffBaseMs * (1L << boundedExponent);
            return Math.Min(exponentialDelay, MaxRetryBackoffMs);
        }

        private static long AddWithoutOverflow(long value, long increment) =>
            value > long.MaxValue - increment
                ? long.MaxValue
                : value + increment;
    }
}
