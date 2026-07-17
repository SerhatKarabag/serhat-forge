using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;
using Serhat.Backend.Monetization.Persistence;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class PendingPurchaseStoreContractTests
{
    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        var storage = new InMemoryStorage();
        var clock = new FixedClock();

        Assert.Throws<ArgumentNullException>(() => new PendingPurchaseStore(null!, clock));
        Assert.Throws<ArgumentNullException>(() => new PendingPurchaseStore(storage, null!));
    }

    [Fact]
    public void AddAndComplete_PersistThroughInjectedStorage()
    {
        var storage = new InMemoryStorage();
        var clock = new FixedClock { TimestampMs = 10_000 };
        var receipt = new StoreReceipt
        {
            Platform = "google",
            ProductId = "coins_100",
            TransactionId = "transaction-1",
            ReceiptPayload = "receipt-token",
            Metadata = new Dictionary<string, string>
            {
                ["packageName"] = "com.example.game"
            }
        };
        var product = new ProductDefinition("coins_100", ProductType.Consumable);

        var firstInstance = new PendingPurchaseStore(storage, clock);
        firstInstance.Add(receipt, product);

        Assert.Equal(1, storage.WriteCount);
        var reloaded = new PendingPurchaseStore(storage, clock);
        var pending = Assert.Single(reloaded.GetAll());
        Assert.Equal("transaction-1", pending.TransactionId);
        Assert.Equal("com.example.game", pending.GetMetadata()["packageName"]);

        reloaded.Complete("transaction-1", "google");

        Assert.Equal(2, storage.WriteCount);
        Assert.Empty(new PendingPurchaseStore(storage, clock).GetAll());
    }

    [Fact]
    public void GetReadyForRetry_UsesPersistedTimestampAndInjectedClock()
    {
        var storage = new InMemoryStorage();
        var clock = new FixedClock { TimestampMs = 100_000 };
        var store = new PendingPurchaseStore(storage, clock);
        store.Add(
            new StoreReceipt
            {
                Platform = "apple",
                ProductId = "remove_ads",
                TransactionId = "transaction-2",
                ReceiptPayload = "receipt"
            },
            new ProductDefinition("remove_ads", ProductType.NonConsumable));

        Assert.Empty(store.GetReadyForRetry());

        clock.TimestampMs += 5_000;

        Assert.Single(new PendingPurchaseStore(storage, clock).GetReadyForRetry());
    }

    [Fact]
    public void RetryablePurchase_IsNotAbandonedAfterAFixedAttemptCount()
    {
        var storage = new InMemoryStorage();
        var clock = new FixedClock { TimestampMs = 100_000 };
        var store = new PendingPurchaseStore(storage, clock);
        var receipt = new StoreReceipt
        {
            Platform = "google",
            ProductId = "coins_100",
            TransactionId = "transaction-long-recovery",
            ReceiptPayload = "purchase-token"
        };

        store.Add(receipt, new ProductDefinition("coins_100", ProductType.Consumable));
        for (var attempt = 0; attempt < 8; attempt++)
        {
            store.MarkRetryFailed(
                receipt.TransactionId,
                receipt.Platform,
                "Temporary backend outage");
            clock.TimestampMs += (long)TimeSpan.FromMinutes(30).TotalMilliseconds;
        }

        var pending = Assert.Single(store.GetReadyForRetry());
        Assert.Equal(8, pending.RetryCount);
    }

    [Fact]
    public void PersistenceFailure_RollsBackInMemoryMutationAndFailsClosed()
    {
        var storage = new InMemoryStorage { FailWrites = true };
        var clock = new FixedClock { TimestampMs = 100_000 };
        var store = new PendingPurchaseStore(storage, clock);
        var receipt = new StoreReceipt
        {
            Platform = "google",
            ProductId = "coins_100",
            TransactionId = "transaction-fail-closed",
            ReceiptPayload = "receipt-token"
        };

        Assert.Throws<InvalidOperationException>(() =>
            store.Add(receipt, new ProductDefinition("coins_100", ProductType.Consumable)));
        Assert.Empty(store.GetAll());

        storage.FailWrites = false;
        store.Add(receipt, new ProductDefinition("coins_100", ProductType.Consumable));
        storage.FailWrites = true;

        Assert.Throws<InvalidOperationException>(() =>
            store.Complete(receipt.TransactionId, receipt.Platform));
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void Add_AppleReceiptPayload_IsNeitherRetainedNorPersisted()
    {
        const string receiptMarker = "RAW-APPLE-APP-RECEIPT-SECRET";
        var rawReceipt = new string('R', 256 * 1024) + receiptMarker;
        var storage = new InMemoryStorage();
        var store = new PendingPurchaseStore(
            storage,
            new FixedClock { TimestampMs = 100_000 });

        store.Add(
            new StoreReceipt
            {
                Platform = "apple",
                ProductId = "remove_ads",
                TransactionId = "apple-transaction-1",
                ReceiptPayload = rawReceipt
            },
            new ProductDefinition("remove_ads", ProductType.NonConsumable));

        Assert.Empty(Assert.Single(store.GetAll()).ReceiptPayload);
        Assert.NotNull(storage.LastWrittenData);
        Assert.DoesNotContain(receiptMarker, storage.LastWrittenData, StringComparison.Ordinal);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).UtcDateTime;
        public long TimestampMs { get; set; }
    }

    private sealed class InMemoryStorage : IStorage
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public int WriteCount { get; private set; }
        public bool FailWrites { get; set; }
        public string? LastWrittenData { get; private set; }

        public Task<string?> ReadAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task WriteAsync(string key, string data, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailWrites)
            {
                return Task.FromException(
                    new IOException("Simulated durable-storage failure"));
            }

            _values[key] = data;
            LastWrittenData = data;
            WriteCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_values.ContainsKey(key));
        }
    }
}
