#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Backend;
using Serhat.Backend.Monetization.Domain;
using Serhat.Backend.Monetization.Persistence;
using Serhat.Backend.Monetization.Services;

namespace Serhat.Forge.Tests.EditMode
{
    [TestFixture]
    public sealed class MonetizationPurchaseServiceTests
    {
        private const string ConsumableProductId = "com.serhat.forge.coins_100";
        private const string CurrentSubscriptionProductId = "com.serhat.forge.plus_monthly";
        private const string TargetSubscriptionProductId = "com.serhat.forge.pro_monthly";

        [Test]
        public async Task BuyAsync_VerificationSucceedsButConfirmationFails_RetainsPendingPurchase()
        {
            var product = new ProductDefinition(ConsumableProductId, ProductType.Consumable);
            using var context = TestContext.Create(product);
            context.Store.NextPurchaseResult = StorePurchaseResult.Success(
                CreateReceipt(ConsumableProductId, "transaction-confirmation-fails"));
            context.Store.NextConfirmationResult = StoreOperationResult.Failure(
                PurchaseError.StoreUnavailable("Confirmation callback timed out"));

            await context.InitializeAsync();
            var result = await context.Service.BuyAsync(ConsumableProductId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(PurchaseErrorCode.StoreUnavailable));
            Assert.That(context.Store.PurchaseCallCount, Is.EqualTo(1));
            Assert.That(context.Store.ConfirmationCallCount, Is.EqualTo(1));
            Assert.That(context.Backend.VerifyCallCount, Is.EqualTo(1));
            Assert.That(context.PendingStore.Count, Is.EqualTo(1));

            var pending = context.PendingStore.GetAll();
            Assert.That(pending[0].ProductId, Is.EqualTo(ConsumableProductId));
            Assert.That(pending[0].TransactionId, Is.EqualTo("transaction-confirmation-fails"));
            Assert.That(pending[0].RetryCount, Is.EqualTo(1));
        }

        [Test]
        public async Task BuyAsync_VerificationAndConfirmationSucceed_RemovesPendingPurchase()
        {
            var product = new ProductDefinition(ConsumableProductId, ProductType.Consumable);
            using var context = TestContext.Create(product);
            context.Store.NextPurchaseResult = StorePurchaseResult.Success(
                CreateReceipt(ConsumableProductId, "transaction-confirmed"));
            context.Store.NextConfirmationResult = StoreOperationResult.Success();

            await context.InitializeAsync();
            var result = await context.Service.BuyAsync(ConsumableProductId);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.TransactionId, Is.EqualTo("transaction-confirmed"));
            Assert.That(context.Store.ConfirmationCallCount, Is.EqualTo(1));
            Assert.That(context.Backend.VerifyCallCount, Is.EqualTo(1));
            Assert.That(context.PendingStore.Count, Is.Zero);
        }

        [Test]
        public async Task InitializeAsync_DuePersistedPurchase_IsRecoveredAutomatically()
        {
            var product = new ProductDefinition(ConsumableProductId, ProductType.Consumable);
            var clock = new FixedClock();
            var pendingStore = new PendingPurchaseStore(new InMemoryStorage(), clock);
            pendingStore.Add(
                CreateReceipt(ConsumableProductId, "transaction-restart-recovery"),
                product);
            clock.UtcNow = clock.UtcNow.AddSeconds(5);

            var store = new FakeStoreClient(new[] { product });
            var backend = new FakeBackendClient(TestContext.CreateEmptyEntitlements());
            using var service = new PurchaseService(
                store,
                backend,
                new FakeProductCatalog(new[] { product }),
                new AllowAllTierPolicy(),
                pendingStore,
                clock,
                new NullLogger());

            var initialization = await service.InitializeAsync();
            Assert.That(initialization.IsSuccess, Is.True, initialization.Error?.ToString());
            await WaitUntilAsync(() => pendingStore.Count == 0, TimeSpan.FromSeconds(1));

            Assert.That(backend.VerifyCallCount, Is.EqualTo(1));
            Assert.That(store.ConfirmationCallCount, Is.EqualTo(1));
            Assert.That(pendingStore.Count, Is.Zero);
        }

        [Test]
        public async Task BuyAsync_ApplePurchase_DoesNotSendRawReceiptPayload()
        {
            const string productId = "com.serhat.forge.remove_ads";
            const string receiptMarker = "RAW-APPLE-APP-RECEIPT-SECRET";
            var rawReceipt = new string('R', 256 * 1024) + receiptMarker;
            var product = new ProductDefinition(productId, ProductType.NonConsumable);
            using var context = TestContext.Create(product);
            context.Store.NextPurchaseResult = StorePurchaseResult.Success(
                new StoreReceipt
                {
                    ProductId = productId,
                    Platform = "apple",
                    TransactionId = "apple-transaction-1",
                    ReceiptPayload = rawReceipt
                });

            await context.InitializeAsync();
            var result = await context.Service.BuyAsync(productId);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(context.Backend.LastVerifyRequest, Is.Not.Null);
            Assert.That(context.Backend.LastVerifyRequest!.ReceiptPayload, Is.Empty);
            Assert.That(
                context.Backend.LastVerifyRequest.ReceiptPayload,
                Does.Not.Contain(receiptMarker));
            Assert.That(context.PendingStore.Count, Is.Zero);
        }

        [Test]
        public async Task RestoreAsync_AllVerificationsFail_ReturnsFailedInsteadOfEmptySuccess()
        {
            var firstProduct = new ProductDefinition(
                "com.serhat.forge.remove_ads",
                ProductType.NonConsumable);
            var secondProduct = new ProductDefinition(
                "com.serhat.forge.level_pack",
                ProductType.NonConsumable);
            using var context = TestContext.Create(firstProduct, secondProduct);
            context.Store.NextRestoreResult = StoreRestoreResult.Success(
                new[]
                {
                    CreateReceipt(firstProduct.ProductId, "restore-transaction-1"),
                    CreateReceipt(secondProduct.ProductId, "restore-transaction-2")
                });
            context.Backend.NextVerificationResult = CloudResult<VerifyPurchaseResponse>.Success(
                new VerifyPurchaseResponse
                {
                    Success = false,
                    ErrorCode = "VERIFICATION_FAILED",
                    ErrorMessage = "Receipt rejected"
                });

            await context.InitializeAsync();
            var result = await context.Service.RestoreAsync();

            Assert.That(result.Status, Is.EqualTo(RestoreResultStatus.Failed));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.RestoredPurchases, Is.Empty);
            Assert.That(result.FailedPurchases, Has.Count.EqualTo(2));
            Assert.That(result.Error?.Code, Is.EqualTo(PurchaseErrorCode.VerificationFailed));
            Assert.That(context.Backend.VerifyCallCount, Is.EqualTo(2));
            Assert.That(context.Store.ConfirmationCallCount, Is.Zero);
        }

        [Test]
        public async Task BuyAsync_DifferentActiveSubscription_ReturnsUnsupportedWithoutStorePurchase()
        {
            var targetProduct = new ProductDefinition(
                TargetSubscriptionProductId,
                ProductType.Subscription,
                tierKey: "pro",
                tierPrecedence: 20);
            var entitlements = new GetEntitlementsResponse
            {
                ServerTimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ActiveSubscription = new SubscriptionDto
                {
                    ProductId = CurrentSubscriptionProductId,
                    TierKey = "plus",
                    Platform = "google",
                    Status = SubscriptionStatus.Active,
                    AutoRenew = true
                }
            };
            using var context = TestContext.Create(entitlements, targetProduct);

            await context.InitializeAsync();
            var result = await context.Service.BuyAsync(TargetSubscriptionProductId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Error?.Code,
                Is.EqualTo(PurchaseErrorCode.SubscriptionChangeNotSupported));
            Assert.That(context.Store.PurchaseCallCount, Is.Zero);
            Assert.That(context.Backend.VerifyCallCount, Is.Zero);
        }

        [Test]
        public async Task BuyAsync_StaleEmptyCacheButFreshActiveSubscription_BlocksStorePurchase()
        {
            var targetProduct = new ProductDefinition(
                TargetSubscriptionProductId,
                ProductType.Subscription,
                tierKey: "pro",
                tierPrecedence: 20);
            using var context = TestContext.Create(targetProduct);

            await context.InitializeAsync();
            context.Backend.NextEntitlementsResult =
                CloudResult<GetEntitlementsResponse>.Success(
                    new GetEntitlementsResponse
                    {
                        ServerTimestampUtc = new DateTime(
                            2026,
                            1,
                            2,
                            0,
                            0,
                            0,
                            DateTimeKind.Utc),
                        ActiveSubscription = new SubscriptionDto
                        {
                            ProductId = CurrentSubscriptionProductId,
                            TierKey = "plus",
                            Platform = "google",
                            Status = SubscriptionStatus.Active,
                            AutoRenew = true
                        }
                    });

            var result = await context.Service.BuyAsync(TargetSubscriptionProductId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Error?.Code,
                Is.EqualTo(PurchaseErrorCode.SubscriptionChangeNotSupported));
            Assert.That(context.Backend.EntitlementsCallCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(context.Store.PurchaseCallCount, Is.Zero);
        }

        [Test]
        public async Task BuyAsync_FreshSubscriptionRefreshFails_FailsClosedWithoutStorePurchase()
        {
            var targetProduct = new ProductDefinition(
                TargetSubscriptionProductId,
                ProductType.Subscription,
                tierKey: "pro",
                tierPrecedence: 20);
            using var context = TestContext.Create(targetProduct);

            await context.InitializeAsync();
            context.Backend.NextEntitlementsResult =
                CloudResult<GetEntitlementsResponse>.Failure(
                    new BackendError(
                        ErrorCodes.ServiceUnavailable,
                        "Entitlements are temporarily unavailable",
                        retryable: true));

            var result = await context.Service.BuyAsync(TargetSubscriptionProductId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(PurchaseErrorCode.ServerError));
            Assert.That(context.Store.PurchaseCallCount, Is.Zero);
        }

        private static StoreReceipt CreateReceipt(string productId, string transactionId) =>
            new()
            {
                ProductId = productId,
                Platform = "google",
                TransactionId = transactionId,
                ReceiptPayload = $"purchase-token-{transactionId}"
            };

        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.That(condition(), Is.True, "Condition did not become true before timeout.");
        }

        private sealed class TestContext : IDisposable
        {
            private bool _disposed;

            private TestContext(
                ProductDefinition[] products,
                GetEntitlementsResponse entitlements)
            {
                Store = new FakeStoreClient(products);
                Backend = new FakeBackendClient(entitlements);
                PendingStore = new PendingPurchaseStore(
                    new InMemoryStorage(),
                    new FixedClock());
                Service = new PurchaseService(
                    Store,
                    Backend,
                    new FakeProductCatalog(products),
                    new AllowAllTierPolicy(),
                    PendingStore,
                    new FixedClock(),
                    new NullLogger());
            }

            public PurchaseService Service { get; }
            public FakeStoreClient Store { get; }
            public FakeBackendClient Backend { get; }
            public PendingPurchaseStore PendingStore { get; }

            public static TestContext Create(params ProductDefinition[] products) =>
                new(products, CreateEmptyEntitlements());

            public static TestContext Create(
                GetEntitlementsResponse entitlements,
                params ProductDefinition[] products) =>
                new(products, entitlements);

            public async Task InitializeAsync()
            {
                var result = await Service.InitializeAsync();
                Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Service.Dispose();
            }

            internal static GetEntitlementsResponse CreateEmptyEntitlements() =>
                new()
                {
                    ServerTimestampUtc = new DateTime(
                        2026,
                        1,
                        1,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc)
                };
        }

        private sealed class FakeStoreClient : IResilientStoreClient, IDisposable
        {
            private readonly IReadOnlyList<ProductInfo> _products;
            private Action<StorePurchaseResult>? _pendingPurchaseCompleted;

            public FakeStoreClient(IReadOnlyList<ProductDefinition> products)
            {
                _products = products
                    .Select(product => new ProductInfo(
                        product.ProductId,
                        product.Type,
                        product.ProductId,
                        string.Empty,
                        "$0.99",
                        0.99m,
                        "USD",
                        product.TierKey))
                    .ToArray();
            }

            public bool IsInitialized { get; private set; }
            public StorePurchaseResult NextPurchaseResult { get; set; } =
                StorePurchaseResult.Failure(PurchaseError.StoreError("NOT_CONFIGURED", "No result"));
            public StoreOperationResult NextConfirmationResult { get; set; } =
                StoreOperationResult.Success();
            public StoreRestoreResult NextRestoreResult { get; set; } =
                StoreRestoreResult.NoPurchases();
            public int PurchaseCallCount { get; private set; }
            public int ConfirmationCallCount { get; private set; }

            public event Action<StorePurchaseResult>? OnPendingPurchaseCompleted
            {
                add => _pendingPurchaseCompleted += value;
                remove => _pendingPurchaseCompleted -= value;
            }

            public Task<InitializationResult> InitializeAsync(
                IReadOnlyList<ProductDefinition> products) =>
                InitializeAsync(products, CancellationToken.None);

            public Task<InitializationResult> InitializeAsync(
                IReadOnlyList<ProductDefinition> products,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IsInitialized = true;
                return Task.FromResult(InitializationResult.Success(_products));
            }

            public Task<StorePurchaseResult> PurchaseAsync(string productId) =>
                PurchaseAsync(productId, CancellationToken.None);

            public Task<StorePurchaseResult> PurchaseAsync(
                string productId,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PurchaseCallCount++;
                return Task.FromResult(NextPurchaseResult);
            }

            public IReadOnlyList<ProductInfo> GetProducts() => _products;

            public ProductInfo? GetProduct(string productId) =>
                _products.FirstOrDefault(product =>
                    string.Equals(product.ProductId, productId, StringComparison.Ordinal));

            public void ConfirmPendingPurchase(string productId, string transactionId)
            {
                ConfirmationCallCount++;
            }

            public Task<StoreOperationResult> ConfirmPendingPurchaseAsync(
                string productId,
                string transactionId,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConfirmationCallCount++;
                return Task.FromResult(NextConfirmationResult);
            }

            public Task<IReadOnlyList<StoreReceipt>> RestoreTransactionsAsync() =>
                Task.FromResult(NextRestoreResult.Receipts);

            public Task<StoreRestoreResult> RestoreTransactionsAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(NextRestoreResult);
            }

            public void Dispose()
            {
                IsInitialized = false;
                _pendingPurchaseCompleted = null;
            }
        }

        private sealed class FakeBackendClient : IMonetizationBackendClient
        {
            public FakeBackendClient(GetEntitlementsResponse entitlements)
            {
                NextEntitlementsResult =
                    CloudResult<GetEntitlementsResponse>.Success(entitlements);
            }

            public CloudResult<VerifyPurchaseResponse> NextVerificationResult { get; set; } =
                CloudResult<VerifyPurchaseResponse>.Success(
                    new VerifyPurchaseResponse
                    {
                        Success = true,
                        TransactionKey = "verified-transaction",
                        GrantedItemIds = new List<string> { "reward" }
                    });
            public int VerifyCallCount { get; private set; }
            public int EntitlementsCallCount { get; private set; }
            public CloudResult<GetEntitlementsResponse> NextEntitlementsResult { get; set; }
            public VerifyPurchaseRequest? LastVerifyRequest { get; private set; }

            public Task<CloudResult<VerifyPurchaseResponse>> VerifyPurchaseAsync(
                VerifyPurchaseRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                VerifyCallCount++;
                LastVerifyRequest = request;
                return Task.FromResult(NextVerificationResult);
            }

            public Task<CloudResult<GetEntitlementsResponse>> GetEntitlementsAsync(
                GetEntitlementsRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                EntitlementsCallCount++;
                return Task.FromResult(NextEntitlementsResult);
            }
        }

        private sealed class FakeProductCatalog : IProductCatalogMapping
        {
            private readonly ProductDefinition[] _products;

            public FakeProductCatalog(ProductDefinition[] products)
            {
                _products = products;
            }

            public IReadOnlyList<ProductDefinition> GetAllProducts() => _products;

            public ProductDefinition? GetProduct(string productId) =>
                _products.FirstOrDefault(product =>
                    string.Equals(product.ProductId, productId, StringComparison.Ordinal));

            public IReadOnlyList<ProductDefinition> GetSubscriptionProducts() =>
                _products.Where(product => product.IsSubscription).ToArray();

            public IReadOnlyList<ProductDefinition> GetProductsByTier(string tierKey) =>
                _products.Where(product =>
                    string.Equals(product.TierKey, tierKey, StringComparison.Ordinal)).ToArray();

            public bool IsProductAllowed(string productId) => GetProduct(productId) != null;
        }

        private sealed class AllowAllTierPolicy : ITierPolicy
        {
            public TierChangePolicy UpgradePolicy => TierChangePolicy.Immediate;
            public TierChangePolicy DowngradePolicy => TierChangePolicy.NextRenewal;

            public int GetTierPrecedence(string tierKey) => 0;

            public TierChangeResult CompareTiers(string? fromTierKey, string toTierKey) =>
                new(
                    TierChangeType.NoChange,
                    TierChangePolicy.Immediate,
                    fromTierKey ?? string.Empty,
                    toTierKey);

            public bool IsTransitionAllowed(string? fromTierKey, string toTierKey) => true;

            public string GetTierDisplayName(string tierKey) => tierKey;
        }

        private sealed class InMemoryStorage : IStorage
        {
            private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

            public Task<string?> ReadAsync(string key, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                _values.TryGetValue(key, out var value);
                return Task.FromResult<string?>(value);
            }

            public Task WriteAsync(string key, string data, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                _values[key] = data;
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

        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; } =
                new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public long TimestampMs => new DateTimeOffset(UtcNow).ToUnixTimeMilliseconds();
        }

        private sealed class NullLogger : IBackendLogger
        {
            public void Debug(string message, params object[] args) { }
            public void Info(string message, params object[] args) { }
            public void Warning(string message, params object[] args) { }
            public void Error(string message, Exception? exception = null, params object[] args) { }
        }
    }
}
