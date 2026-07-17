using System;
using System.Collections.Generic;
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
        public string ProductId;
        public string Platform;
        public string TransactionId;
        public string ReceiptPayload;
        public string ProductType;
        public string? TierKey;
        public long CreatedAtMs;
        public int RetryCount;
        public string? LastError;

        // Metadata serialized as JSON
        public string? MetadataJson;

        public PendingPurchase() { }

        public PendingPurchase(StoreReceipt receipt, ProductDefinition? productDef, long createdAtMs)
        {
            ProductId = receipt.ProductId;
            Platform = receipt.Platform;
            TransactionId = receipt.TransactionId;
            ReceiptPayload = receipt.ReceiptPayload;
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
            for (int i = 0; i < Keys.Count && i < Values.Count; i++)
            {
                result[Keys[i]] = Values[i];
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
    /// Uses PlayerPrefs for synchronous persistence (pending purchases must survive crashes).
    /// </summary>
    public sealed class PendingPurchaseStore
    {
        private const string StorageKey = "SerhatBackendSdk_PendingPurchases";
        private const int MaxRetries = 5;
        private const long RetryBackoffBaseMs = 5000; // 5 seconds

        private readonly IClock _clock;
        private readonly object _lock = new();
        private PendingPurchaseContainer _container;

        public PendingPurchaseStore(IStorage storage, IClock clock)
        {
            _clock = clock;
            _container = Load();
        }

        /// <summary>
        /// Gets all pending purchases.
        /// </summary>
        public IReadOnlyList<PendingPurchase> GetAll()
        {
            lock (_lock)
            {
                return _container.Purchases.ToArray();
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
                    if (purchase.RetryCount >= MaxRetries)
                    {
                        continue; // Max retries exceeded
                    }

                    var backoffMs = CalculateBackoff(purchase.RetryCount);
                    var nextRetryAt = purchase.CreatedAtMs + backoffMs;

                    if (now >= nextRetryAt)
                    {
                        ready.Add(purchase);
                    }
                }

                return ready;
            }
        }

        /// <summary>
        /// Adds a pending purchase.
        /// </summary>
        public void Add(StoreReceipt receipt, ProductDefinition? productDef)
        {
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
                Save();
            }
        }

        /// <summary>
        /// Marks a pending purchase as successfully verified and removes it.
        /// </summary>
        public void Complete(string transactionId, string platform)
        {
            lock (_lock)
            {
                _container.Purchases.RemoveAll(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);
                Save();
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
                    purchase.RetryCount++;
                    purchase.LastError = error;
                    purchase.CreatedAtMs = _clock.TimestampMs; // Reset timer for backoff
                    Save();
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
                _container.Purchases.RemoveAll(p =>
                    p.TransactionId == transactionId &&
                    p.Platform == platform);
                Save();
            }
        }

        /// <summary>
        /// Clears all pending purchases (for testing/reset).
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _container.Purchases.Clear();
                Save();
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
                var json = PlayerPrefs.GetString(StorageKey, null);
                if (!string.IsNullOrEmpty(json))
                {
                    return JsonUtility.FromJson<PendingPurchaseContainer>(json)
                           ?? new PendingPurchaseContainer();
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
                PlayerPrefs.SetString(StorageKey, json);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PendingPurchaseStore] Failed to save: {ex.Message}");
            }
        }

        private static long CalculateBackoff(int retryCount)
        {
            // Exponential backoff: 5s, 10s, 20s, 40s, 80s
            return RetryBackoffBaseMs * (1L << retryCount);
        }
    }
}
