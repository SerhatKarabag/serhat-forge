#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Backend;
using Serhat.Backend.Monetization.Domain;
using Serhat.Backend.Monetization.Persistence;
using UnityEngine;

namespace Serhat.Backend.Monetization.Services
{
    /// <summary>
    /// Main purchase service implementation.
    /// Orchestrates store interactions, server verification, and entitlement management.
    /// </summary>
    public sealed class PurchaseService : IPurchaseService, IDisposable
    {
        private static readonly TimeSpan SubscriptionStateRefreshTimeout = TimeSpan.FromSeconds(15);

        private readonly IStoreClient _storeClient;
        private readonly IMonetizationBackendClient _backendClient;
        private readonly IProductCatalogMapping _catalogMapping;
        private readonly ITierPolicy _tierPolicy;
        private readonly PendingPurchaseStore _pendingStore;
        private readonly IClock _clock;
        private readonly IBackendLogger _logger;

        private EntitlementsResponse? _cachedEntitlements;
        private readonly Dictionary<string, ProductInfo> _productInfoCache = new();
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private readonly SemaphoreSlim _purchaseLock = new(1, 1);
        private readonly SemaphoreSlim _verificationLock = new(1, 1);
        private readonly SemaphoreSlim _entitlementsLock = new(1, 1);
        private readonly SemaphoreSlim _pendingProcessingLock = new(1, 1);
        private readonly SemaphoreSlim _pendingWakeSignal = new(0, 1);
        private readonly Dictionary<string, VerifiedPurchase> _verifiedPurchases = new();
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private bool _disposed;

        public bool IsInitialized { get; private set; }
        public SubscriptionDto? ActiveSubscription => _cachedEntitlements?.ActiveSubscription;

        public event Action<InitializationResult>? OnInitialized;
        public event Action<PurchaseResult>? OnPurchaseCompleted;
        public event Action<EntitlementsResponse>? OnEntitlementsUpdated;

        public PurchaseService(
            IStoreClient storeClient,
            IMonetizationBackendClient backendClient,
            IProductCatalogMapping catalogMapping,
            ITierPolicy tierPolicy,
            PendingPurchaseStore pendingStore,
            IClock clock,
            IBackendLogger logger)
        {
            _storeClient = storeClient ?? throw new ArgumentNullException(nameof(storeClient));
            _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
            _catalogMapping = catalogMapping ?? throw new ArgumentNullException(nameof(catalogMapping));
            _tierPolicy = tierPolicy ?? throw new ArgumentNullException(nameof(tierPolicy));
            _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to pending purchase completion
            _storeClient.OnPendingPurchaseCompleted += HandlePendingPurchaseCompleted;
        }

        public async Task<InitializationResult> InitializeAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await _initializationLock.WaitAsync(ct);
            try
            {
                if (IsInitialized)
                {
                    return InitializationResult.Success(
                        new List<ProductInfo>(_productInfoCache.Values));
                }

                return await InitializeInternalAsync(ct);
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task<InitializationResult> InitializeInternalAsync(CancellationToken ct)
        {
            _logger.Info("[PurchaseService] Initializing store...");

            try
            {
                // Get products from catalog mapping
                var products = _catalogMapping.GetAllProducts();

                // Initialize store
                var result = _storeClient is IResilientStoreClient resilientStore
                    ? await resilientStore.InitializeAsync(products, ct)
                    : await _storeClient.InitializeAsync(products);

                if (!result.IsSuccess)
                {
                    _logger.Error(
                        "[PurchaseService] Store initialization failed: {0}",
                        null,
                        result.Error?.ToString() ?? "Unknown store error");
                    RaiseSafely(OnInitialized, result, nameof(OnInitialized));
                    return result;
                }

                // Cache product info with tier keys
                _productInfoCache.Clear();
                foreach (var product in result.AvailableProducts)
                {
                    _productInfoCache[product.ProductId] = product;
                }

                IsInitialized = true;

                _logger.Info("[PurchaseService] Store initialized with {0} products",
                    result.AvailableProducts.Count);

                // Keep durable pending purchases moving for the lifetime of the service. The
                // loop sleeps until work exists and uses the persisted retry schedule.
                RunInBackground(RunPendingPurchaseRecoveryLoopAsync(
                    _lifetimeCancellation.Token));

                // Fetch initial entitlements
                RunInBackground(GetEntitlementsAsync(
                    forceRefresh: true,
                    _lifetimeCancellation.Token));

                RaiseSafely(OnInitialized, result, nameof(OnInitialized));
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Initialization exception: {0}", ex, ex.Message);

                var error = PurchaseError.Unknown(ex.Message);
                var result = InitializationResult.Failure(error);
                RaiseSafely(OnInitialized, result, nameof(OnInitialized));
                return result;
            }
        }

        public async Task<PurchaseResult> BuyAsync(string productId, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized)
            {
                return PurchaseResult.Failure(productId, PurchaseError.StoreNotInitialized());
            }

            // Validate product is allowed
            if (!_catalogMapping.IsProductAllowed(productId))
            {
                return PurchaseResult.Failure(productId, PurchaseError.ProductNotAllowed(productId));
            }

            var productDef = _catalogMapping.GetProduct(productId);

            if (productDef?.IsSubscription == true &&
                string.IsNullOrWhiteSpace(productDef.TierKey))
            {
                return PurchaseResult.Failure(
                    productId,
                    PurchaseError.ProductNotAllowed(productId));
            }

            // Prevent concurrent purchases
            if (!await _purchaseLock.WaitAsync(0, ct))
            {
                return PurchaseResult.Failure(productId,
                    new PurchaseError(PurchaseErrorCode.Pending, "Another purchase is in progress"));
            }

            try
            {
                // Subscription decisions must be made from a fresh server snapshot while the
                // purchase lock is held. Otherwise two callers (or a stale startup fetch) can
                // both pass the guard and start an unsupported replacement purchase.
                if (productDef?.IsSubscription == true)
                {
                    if (!await RefreshSubscriptionStateForPurchaseAsync(ct))
                    {
                        return PurchaseResult.Failure(
                            productId,
                            PurchaseError.ServerError(
                                "Current subscription state could not be refreshed safely"));
                    }

                    var activeSubscription = ActiveSubscription;
                    if (activeSubscription?.IsActive == true)
                    {
                        return PurchaseResult.Failure(
                            productId,
                            string.Equals(
                                activeSubscription.ProductId,
                                productId,
                                StringComparison.Ordinal)
                                ? PurchaseError.AlreadyOwned(productId)
                                : PurchaseError.SubscriptionChangeNotSupported(
                                    activeSubscription.ProductId,
                                    productId));
                    }

                    if (!_tierPolicy.IsTransitionAllowed(null, productDef.TierKey!))
                    {
                        return PurchaseResult.Failure(productId,
                            new PurchaseError(
                                PurchaseErrorCode.ProductNotAllowed,
                                $"Starting subscription tier '{productDef.TierKey}' is not allowed"));
                    }
                }

                _logger.Info("[PurchaseService] Starting purchase: {0}", productId);

                // Initiate store purchase
                var storeResult = _storeClient is IResilientStoreClient resilientStore
                    ? await resilientStore.PurchaseAsync(productId, ct)
                    : await _storeClient.PurchaseAsync(productId);

                if (!storeResult.IsSuccess)
                {
                    if (storeResult.IsPending && storeResult.Receipt != null)
                    {
                        // Save pending purchase for deferred completion
                        _pendingStore.Add(storeResult.Receipt, productDef);
                        SignalPendingRecovery();
                    }

                    var result = PurchaseResult.Failure(productId, storeResult.Error!);
                    RaiseSafely(OnPurchaseCompleted, result, nameof(OnPurchaseCompleted));
                    return result;
                }

                // Store purchase succeeded - verify with server
                var verifyResult = await VerifyAndGrantAsync(storeResult.Receipt!, productDef, false, ct);
                RaiseSafely(OnPurchaseCompleted, verifyResult, nameof(OnPurchaseCompleted));

                return verifyResult;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Purchase exception: {0}", ex, ex.Message);
                var result = PurchaseResult.Failure(productId, PurchaseError.Unknown(ex.Message));
                RaiseSafely(OnPurchaseCompleted, result, nameof(OnPurchaseCompleted));
                return result;
            }
            finally
            {
                _purchaseLock.Release();
            }
        }

        public async Task<RestoreResult> RestoreAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized)
            {
                return RestoreResult.Failure(PurchaseError.StoreNotInitialized());
            }

            var lockTaken = false;

            try
            {
                lockTaken = await _purchaseLock.WaitAsync(0, ct);
                if (!lockTaken)
                {
                    return RestoreResult.Failure(
                        new PurchaseError(
                            PurchaseErrorCode.Pending,
                            "Another purchase or restore operation is in progress"));
                }

                _logger.Info("[PurchaseService] Restoring purchases...");
                StoreRestoreResult storeRestore;
                if (_storeClient is IResilientStoreClient resilientStore)
                {
                    storeRestore = await resilientStore.RestoreTransactionsAsync(ct);
                }
                else
                {
                    var legacyReceipts = await _storeClient.RestoreTransactionsAsync();
                    storeRestore = StoreRestoreResult.Success(legacyReceipts);
                }

                if (storeRestore.Status == StoreRestoreStatus.Failed)
                {
                    return RestoreResult.Failure(
                        storeRestore.Error ?? PurchaseError.StoreUnavailable("Store restore failed"));
                }

                if (storeRestore.Status == StoreRestoreStatus.NoPurchases)
                {
                    _logger.Info("[PurchaseService] No purchases to restore");
                    return RestoreResult.NoRestorations();
                }

                var purchaseResults = new List<PurchaseResult>(
                    storeRestore.Receipts.Count + storeRestore.Errors.Count);

                foreach (var receipt in storeRestore.Receipts)
                {
                    var productDef = _catalogMapping.GetProduct(receipt.ProductId);
                    var result = await VerifyAndGrantAsync(receipt, productDef, true, ct);
                    purchaseResults.Add(result);
                    RaiseSafely(OnPurchaseCompleted, result, nameof(OnPurchaseCompleted));
                }

                for (var index = 0; index < storeRestore.Errors.Count; index++)
                {
                    purchaseResults.Add(PurchaseResult.Failure(string.Empty, storeRestore.Errors[index]));
                }

                var restoreResult = RestoreResult.FromPurchases(purchaseResults);
                _logger.Info(
                    "[PurchaseService] Restore completed: {0} restored, {1} failed",
                    restoreResult.RestoredPurchases.Count,
                    restoreResult.FailedPurchases.Count);

                return restoreResult;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Restore exception: {0}", ex, ex.Message);
                return RestoreResult.Failure(PurchaseError.StoreUnavailable(ex.Message));
            }
            finally
            {
                if (lockTaken)
                {
                    _purchaseLock.Release();
                }
            }
        }

        public async Task<EntitlementsResponse> GetEntitlementsAsync(
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            var fetchResult = await FetchEntitlementsAsync(forceRefresh, ct);
            return fetchResult.Response;
        }

        private async Task<EntitlementsFetchResult> FetchEntitlementsAsync(
            bool forceRefresh,
            CancellationToken ct)
        {
            ThrowIfDisposed();

            if (!forceRefresh && _cachedEntitlements != null)
            {
                return new EntitlementsFetchResult(_cachedEntitlements, false);
            }

            await _entitlementsLock.WaitAsync(ct);
            try
            {
                if (!forceRefresh && _cachedEntitlements != null)
                {
                    return new EntitlementsFetchResult(_cachedEntitlements, false);
                }

                var request = new GetEntitlementsRequest { ForceRefresh = forceRefresh };
                var result = await _backendClient.GetEntitlementsAsync(request, ct);

                if (result.IsSuccess && result.Data != null)
                {
                    var incoming = new EntitlementsResponse
                    {
                        Entitlements = result.Data.Entitlements,
                        ActiveSubscription = result.Data.ActiveSubscription,
                        ServerTimestampUtc = result.Data.ServerTimestampUtc
                    };

                    if (_cachedEntitlements == null ||
                        incoming.ServerTimestampUtc == default ||
                        _cachedEntitlements.ServerTimestampUtc == default ||
                        incoming.ServerTimestampUtc >= _cachedEntitlements.ServerTimestampUtc)
                    {
                        _cachedEntitlements = incoming;
                        RaiseSafely(
                            OnEntitlementsUpdated,
                            _cachedEntitlements,
                            nameof(OnEntitlementsUpdated));
                    }

                    return new EntitlementsFetchResult(
                        _cachedEntitlements ?? incoming,
                        true);
                }

                _logger.Warning(
                    "[PurchaseService] Failed to get entitlements: {0}",
                    result.Error?.ToString() ?? "Unknown backend error");
                return new EntitlementsFetchResult(
                    _cachedEntitlements ?? new EntitlementsResponse(),
                    false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] GetEntitlements exception: {0}", ex, ex.Message);
                return new EntitlementsFetchResult(
                    _cachedEntitlements ?? new EntitlementsResponse(),
                    false);
            }
            finally
            {
                _entitlementsLock.Release();
            }
        }

        public bool HasEntitlement(string itemId)
        {
            ThrowIfDisposed();

            if (_cachedEntitlements == null)
            {
                return false;
            }

            return _cachedEntitlements.Entitlements.Exists(e => e.ItemId == itemId);
        }

        public ProductInfo? GetProductInfo(string productId)
        {
            ThrowIfDisposed();

            _productInfoCache.TryGetValue(productId, out var info);
            return info;
        }

        public async Task ProcessPendingPurchasesAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await ProcessPendingPurchasesSerializedAsync(ct);
        }

        private async Task ProcessPendingPurchasesSerializedAsync(CancellationToken ct)
        {
            await _pendingProcessingLock.WaitAsync(ct);
            try
            {
                await ProcessPendingPurchasesInternalAsync(ct);
            }
            finally
            {
                _pendingProcessingLock.Release();
            }
        }

        private async Task ProcessPendingPurchasesInternalAsync(CancellationToken ct)
        {
            var pending = _pendingStore.GetReadyForRetry();

            if (pending.Count == 0)
            {
                return;
            }

            _logger.Info("[PurchaseService] Processing {0} pending purchases", pending.Count);

            foreach (var purchase in pending)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var receipt = new StoreReceipt
                    {
                        ProductId = purchase.ProductId,
                        Platform = purchase.Platform,
                        TransactionId = purchase.TransactionId,
                        ReceiptPayload = purchase.ReceiptPayload,
                        Metadata = purchase.GetMetadata()
                    };

                    var productDef = _catalogMapping.GetProduct(purchase.ProductId);
                    var result = await VerifyAndGrantAsync(receipt, productDef, false, ct);

                    if (result.IsSuccess)
                    {
                        RaiseSafely(OnPurchaseCompleted, result, nameof(OnPurchaseCompleted));
                    }
                    else if (!result.Error!.IsRetryable)
                    {
                        // Permanent failure - remove from pending
                        _pendingStore.Remove(purchase.TransactionId, purchase.Platform);
                    }
                    // VerifyAndGrantAsync retains and schedules retryable failures.
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("[PurchaseService] Pending purchase processing failed: {0}", ex.Message);

                    _pendingStore.MarkRetryFailed(
                        purchase.TransactionId,
                        purchase.Platform,
                        ex.Message);
                }
            }
        }

        private async Task RunPendingPurchaseRecoveryLoopAsync(CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var retryDelay = _pendingStore.GetTimeUntilNextRetry();
                if (!retryDelay.HasValue)
                {
                    await _pendingWakeSignal.WaitAsync(ct);
                    continue;
                }

                if (retryDelay.Value > TimeSpan.Zero)
                {
                    await WaitForPendingSignalOrDelayAsync(retryDelay.Value, ct);
                }

                await ProcessPendingPurchasesSerializedAsync(ct);
            }
        }

        private async Task WaitForPendingSignalOrDelayAsync(
            TimeSpan delay,
            CancellationToken ct)
        {
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var signalTask = _pendingWakeSignal.WaitAsync(waitCancellation.Token);
            var delayTask = Task.Delay(delay, waitCancellation.Token);
            var completedTask = await Task.WhenAny(signalTask, delayTask);
            waitCancellation.Cancel();
            await completedTask;
        }

        private async Task<PurchaseResult> VerifyAndGrantAsync(
            StoreReceipt receipt,
            ProductDefinition? productDef,
            bool isRestore,
            CancellationToken ct)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            if (string.IsNullOrWhiteSpace(receipt.ProductId) ||
                string.IsNullOrWhiteSpace(receipt.Platform) ||
                string.IsNullOrWhiteSpace(receipt.TransactionId) ||
                (!IsApple(receipt.Platform) &&
                 string.IsNullOrWhiteSpace(receipt.ReceiptPayload)))
            {
                return PurchaseResult.Failure(
                    receipt.ProductId,
                    PurchaseError.VerificationFailed("The store receipt is incomplete"));
            }

            var verificationKey = $"{receipt.Platform}\n{receipt.TransactionId}";
            await _verificationLock.WaitAsync(ct);

            try
            {
                if (_verifiedPurchases.TryGetValue(verificationKey, out var verifiedPurchase))
                {
                    return verifiedPurchase.ToResult(isRestore);
                }

                // Persist before the network call so a crash cannot lose a paid order.
                _pendingStore.Add(receipt, productDef);
                SignalPendingRecovery();

                var metadata = new Dictionary<string, string>(receipt.Metadata);

                metadata["restored"] = isRestore ? "true" : "false";

                var request = new VerifyPurchaseRequest
                {
                    Platform = receipt.Platform,
                    ProductId = receipt.ProductId,
                    TransactionId = receipt.TransactionId,
                    // Apple App Store Server API verification is transaction-ID based. Strip any
                    // accidentally supplied AppReceipt/JWS before crossing the network boundary.
                    ReceiptPayload = IsApple(receipt.Platform)
                        ? string.Empty
                        : receipt.ReceiptPayload,
                    ProductType = productDef?.Type.ToString() ?? "Unknown",
                    TierKey = productDef?.TierKey,
                    Metadata = metadata
                };

                // Google exposes its purchase token as TransactionId. Never log either value.
                _logger.Debug("[PurchaseService] Verifying purchase for product: {0}",
                    receipt.ProductId);

                var verifyResult = await _backendClient.VerifyPurchaseAsync(request, ct);

                if (!verifyResult.IsSuccess || verifyResult.Data == null)
                {
                    var error = MapBackendError(verifyResult.Error);
                    _logger.Warning("[PurchaseService] Verification failed: {0}", error);

                    if (!error.IsRetryable)
                    {
                        _pendingStore.Remove(receipt.TransactionId, receipt.Platform);
                    }
                    else
                    {
                        _pendingStore.MarkRetryFailed(
                            receipt.TransactionId,
                            receipt.Platform,
                            error.Message);
                    }

                    return PurchaseResult.Failure(receipt.ProductId, error);
                }

                var response = verifyResult.Data;

                if (!response.Success)
                {
                    var error = new PurchaseError(
                        PurchaseErrorCode.VerificationFailed,
                        response.ErrorMessage ?? "Verification failed");

                    _pendingStore.Remove(receipt.TransactionId, receipt.Platform);
                    return PurchaseResult.Failure(receipt.ProductId, error);
                }

                var grantedItemIds = response.GrantedItemIds ?? new List<string>();

                StoreOperationResult confirmationResult;
                if (_storeClient is IResilientStoreClient resilientStore)
                {
                    confirmationResult = await resilientStore.ConfirmPendingPurchaseAsync(
                        receipt.ProductId,
                        receipt.TransactionId,
                        ct);
                }
                else
                {
                    _storeClient.ConfirmPendingPurchase(
                        receipt.ProductId,
                        receipt.TransactionId);
                    confirmationResult = StoreOperationResult.Success();
                }

                if (!confirmationResult.IsSuccess)
                {
                    var confirmationError = confirmationResult.Error ??
                                            PurchaseError.StoreUnavailable(
                                                "Store confirmation did not complete");
                    _pendingStore.MarkRetryFailed(
                        receipt.TransactionId,
                        receipt.Platform,
                        confirmationError.Message);
                    return PurchaseResult.Failure(receipt.ProductId, confirmationError);
                }

                // Remove durable recovery state only after the store confirmation callback.
                _pendingStore.Complete(receipt.TransactionId, receipt.Platform);

                // Update cached subscription if applicable
                if (response.Subscription != null)
                {
                    if (_cachedEntitlements == null)
                    {
                        _cachedEntitlements = new EntitlementsResponse();
                    }
                    _cachedEntitlements.ActiveSubscription = response.Subscription;
                }

                _logger.Info("[PurchaseService] Purchase verified: {0}, granted: {1}",
                    receipt.ProductId, string.Join(", ", grantedItemIds));

                verifiedPurchase = new VerifiedPurchase(
                    receipt.ProductId,
                    receipt.TransactionId,
                    grantedItemIds,
                    response.Subscription?.TierKey);
                _verifiedPurchases[verificationKey] = verifiedPurchase;

                // Refresh entitlements
                RunInBackground(GetEntitlementsAsync(
                    forceRefresh: true,
                    _lifetimeCancellation.Token));

                return verifiedPurchase.ToResult(isRestore);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Verification exception: {0}", ex, ex.Message);

                _pendingStore.MarkRetryFailed(
                    receipt.TransactionId,
                    receipt.Platform,
                    ex.Message);

                return PurchaseResult.Failure(receipt.ProductId,
                    PurchaseError.NetworkError(ex.Message));
            }
            finally
            {
                _verificationLock.Release();
            }
        }

        private void HandlePendingPurchaseCompleted(StorePurchaseResult storeResult)
        {
            if (_disposed)
            {
                return;
            }

            RunInBackground(HandlePendingPurchaseCompletedAsync(storeResult));
        }

        private async Task HandlePendingPurchaseCompletedAsync(StorePurchaseResult storeResult)
        {
            try
            {
                if (!storeResult.IsSuccess || storeResult.Receipt == null)
                {
                    return;
                }

                var productDef = _catalogMapping.GetProduct(storeResult.Receipt.ProductId);
                _pendingStore.Add(storeResult.Receipt, productDef);
                SignalPendingRecovery();
                await WaitForStoreInitializationAsync(_lifetimeCancellation.Token);
                var result = await VerifyAndGrantAsync(
                    storeResult.Receipt,
                    productDef,
                    false,
                    _lifetimeCancellation.Token);
                RaiseSafely(OnPurchaseCompleted, result, nameof(OnPurchaseCompleted));
            }
            catch (OperationCanceledException) when (_disposed)
            {
                // Expected during application shutdown.
            }
            catch (Exception exception)
            {
                _logger.Error(
                    "[PurchaseService] Pending purchase callback failed: {0}",
                    exception,
                    exception.Message);
            }
        }

        private async Task WaitForStoreInitializationAsync(CancellationToken ct)
        {
            var attemptsRemaining = 200;
            while (!_storeClient.IsInitialized && attemptsRemaining-- > 0)
            {
                await Task.Delay(50, ct);
            }

            if (!_storeClient.IsInitialized)
            {
                throw new TimeoutException(
                    "The store did not finish initialization before pending-purchase recovery.");
            }
        }

        private async Task<bool> RefreshSubscriptionStateForPurchaseAsync(CancellationToken ct)
        {
            using var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            refreshCancellation.CancelAfter(SubscriptionStateRefreshTimeout);

            try
            {
                var result = await FetchEntitlementsAsync(
                    forceRefresh: true,
                    refreshCancellation.Token);
                return result.WasFetchedSuccessfully;
            }
            catch (OperationCanceledException) when (
                !ct.IsCancellationRequested &&
                refreshCancellation.IsCancellationRequested)
            {
                _logger.Warning(
                    "[PurchaseService] Subscription state refresh timed out after {0} seconds",
                    SubscriptionStateRefreshTimeout.TotalSeconds);
                return false;
            }
        }

        private readonly struct EntitlementsFetchResult
        {
            public EntitlementsFetchResult(
                EntitlementsResponse response,
                bool wasFetchedSuccessfully)
            {
                Response = response;
                WasFetchedSuccessfully = wasFetchedSuccessfully;
            }

            public EntitlementsResponse Response { get; }
            public bool WasFetchedSuccessfully { get; }
        }

        private void RunInBackground(Task task)
        {
            _ = ObserveBackgroundTaskAsync(task);
        }

        private void SignalPendingRecovery()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _pendingWakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Signals are coalesced; the loop re-reads durable state after waking.
            }
        }

        private async Task ObserveBackgroundTaskAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) when (_disposed)
            {
                // Expected during application shutdown.
            }
            catch (OperationCanceledException)
            {
                // A caller-scoped background operation was cancelled; no retry state is changed.
            }
            catch (Exception exception)
            {
                _logger.Error(
                    "[PurchaseService] Background operation failed: {0}",
                    exception,
                    exception.Message);
            }
        }

        private void RaiseSafely<T>(Action<T>? observers, T value, string eventName)
        {
            if (observers == null)
            {
                return;
            }

            foreach (var observer in observers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)observer).Invoke(value);
                }
                catch (Exception exception)
                {
                    _logger.Error(
                        $"[PurchaseService] Observer for {eventName} failed: {{0}}",
                        exception,
                        exception.Message);
                }
            }
        }

        private sealed class VerifiedPurchase
        {
            private readonly string _productId;
            private readonly string _transactionId;
            private readonly IReadOnlyList<string> _grantedItemIds;
            private readonly string? _tierKey;

            public VerifiedPurchase(
                string productId,
                string transactionId,
                IReadOnlyList<string> grantedItemIds,
                string? tierKey)
            {
                _productId = productId;
                _transactionId = transactionId;
                _grantedItemIds = new List<string>(grantedItemIds).AsReadOnly();
                _tierKey = tierKey;
            }

            public PurchaseResult ToResult(bool isRestore) =>
                isRestore
                    ? PurchaseResult.Restored(
                        _productId,
                        _transactionId,
                        _grantedItemIds,
                        _tierKey)
                    : PurchaseResult.Success(
                        _productId,
                        _transactionId,
                        _grantedItemIds,
                        _tierKey);
        }

        private static PurchaseError MapBackendError(BackendError? error)
        {
            if (error == null)
            {
                return PurchaseError.Unknown("No error details");
            }

            return error.Code switch
            {
                "RATE_LIMITED" => PurchaseError.RateLimited(),
                "NETWORK_ERROR" => PurchaseError.NetworkError(error.Message),
                "IN_PROGRESS" => PurchaseError.ServerError(
                    error.Message ?? "Purchase verification is already in progress"),
                "PRODUCT_NOT_ALLOWED" => PurchaseError.ProductNotAllowed(error.Message ?? ""),
                "VERIFICATION_FAILED" => PurchaseError.VerificationFailed(error.Message),
                "ALREADY_GRANTED" => PurchaseError.AlreadyOwned(error.Message ?? ""),
                _ => new PurchaseError(
                    PurchaseErrorCode.ServerError,
                    error.Message ?? "Server error",
                    error.Retryable)
            };
        }

        private static bool IsApple(string? platform) =>
            string.Equals(platform, "apple", StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsInitialized = false;
            _lifetimeCancellation.Cancel();
            _storeClient.OnPendingPurchaseCompleted -= HandlePendingPurchaseCompleted;
            if (_storeClient is IDisposable disposableStoreClient)
            {
                disposableStoreClient.Dispose();
            }

            OnInitialized = null;
            OnPurchaseCompleted = null;
            OnEntitlementsUpdated = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PurchaseService));
            }
        }
    }
}
