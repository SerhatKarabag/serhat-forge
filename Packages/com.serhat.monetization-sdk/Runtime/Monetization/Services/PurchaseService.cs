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
    public sealed class PurchaseService : IPurchaseService
    {
        private readonly IStoreClient _storeClient;
        private readonly IMonetizationBackendClient _backendClient;
        private readonly IProductCatalogMapping _catalogMapping;
        private readonly ITierPolicy _tierPolicy;
        private readonly PendingPurchaseStore _pendingStore;
        private readonly IClock _clock;
        private readonly IBackendLogger _logger;

        private EntitlementsResponse? _cachedEntitlements;
        private readonly Dictionary<string, ProductInfo> _productInfoCache = new();
        private readonly SemaphoreSlim _purchaseLock = new(1, 1);

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
            _storeClient = storeClient;
            _backendClient = backendClient;
            _catalogMapping = catalogMapping;
            _tierPolicy = tierPolicy;
            _pendingStore = pendingStore;
            _clock = clock;
            _logger = logger;

            // Subscribe to pending purchase completion
            _storeClient.OnPendingPurchaseCompleted += HandlePendingPurchaseCompleted;
        }

        public async Task<InitializationResult> InitializeAsync(CancellationToken ct = default)
        {
            if (IsInitialized)
            {
                return InitializationResult.Success(new List<ProductInfo>(_productInfoCache.Values));
            }

            _logger.Info("[PurchaseService] Initializing store...");

            try
            {
                // Get products from catalog mapping
                var products = _catalogMapping.GetAllProducts();

                // Initialize store
                var result = await _storeClient.InitializeAsync(products);

                if (!result.IsSuccess)
                {
                    _logger.Error("[PurchaseService] Store initialization failed: {0}", null, result.Error);
                    OnInitialized?.Invoke(result);
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

                // Process any pending purchases from previous sessions
                _ = ProcessPendingPurchasesAsync(ct);

                // Fetch initial entitlements
                _ = GetEntitlementsAsync(forceRefresh: true, ct);

                OnInitialized?.Invoke(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Initialization exception: {0}", ex, ex.Message);

                var error = PurchaseError.Unknown(ex.Message);
                var result = InitializationResult.Failure(error);
                OnInitialized?.Invoke(result);
                return result;
            }
        }

        public async Task<PurchaseResult> BuyAsync(string productId, CancellationToken ct = default)
        {
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

            // For subscriptions, check tier policy
            if (productDef?.IsSubscription == true && ActiveSubscription != null)
            {
                var tierChange = _tierPolicy.CompareTiers(
                    ActiveSubscription.TierKey,
                    productDef.TierKey!);

                if (!_tierPolicy.IsTransitionAllowed(ActiveSubscription.TierKey, productDef.TierKey!))
                {
                    return PurchaseResult.Failure(productId,
                        new PurchaseError(
                            PurchaseErrorCode.ProductNotAllowed,
                            $"Tier transition from {ActiveSubscription.TierKey} to {productDef.TierKey} is not allowed"));
                }
            }

            // Prevent concurrent purchases
            if (!await _purchaseLock.WaitAsync(0, ct))
            {
                return PurchaseResult.Failure(productId,
                    new PurchaseError(PurchaseErrorCode.Pending, "Another purchase is in progress"));
            }

            try
            {
                _logger.Info("[PurchaseService] Starting purchase: {0}", productId);

                // Initiate store purchase
                var storeResult = await _storeClient.PurchaseAsync(productId);

                if (!storeResult.IsSuccess)
                {
                    if (storeResult.IsPending && storeResult.Receipt != null)
                    {
                        // Save pending purchase for deferred completion
                        _pendingStore.Add(storeResult.Receipt, productDef);
                    }

                    var result = PurchaseResult.Failure(productId, storeResult.Error!);
                    OnPurchaseCompleted?.Invoke(result);
                    return result;
                }

                // Store purchase succeeded - verify with server
                var verifyResult = await VerifyAndGrantAsync(storeResult.Receipt!, productDef, false, ct);
                if (!verifyResult.IsSuccess)
                {
                    OnPurchaseCompleted?.Invoke(verifyResult);
                }

                return verifyResult;
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Purchase exception: {0}", ex, ex.Message);
                var result = PurchaseResult.Failure(productId, PurchaseError.Unknown(ex.Message));
                OnPurchaseCompleted?.Invoke(result);
                return result;
            }
            finally
            {
                _purchaseLock.Release();
            }
        }

        public async Task<RestoreResult> RestoreAsync(CancellationToken ct = default)
        {
            if (!IsInitialized)
            {
                return RestoreResult.Failure(PurchaseError.StoreNotInitialized());
            }

            _logger.Info("[PurchaseService] Restoring purchases...");

            try
            {
                var receipts = await _storeClient.RestoreTransactionsAsync();

                if (receipts.Count == 0)
                {
                    _logger.Info("[PurchaseService] No purchases to restore");
                    return RestoreResult.NoRestorations();
                }

                var restoredPurchases = new List<PurchaseResult>();

                foreach (var receipt in receipts)
                {
                    var productDef = _catalogMapping.GetProduct(receipt.ProductId);
                    var result = await VerifyAndGrantAsync(receipt, productDef, true, ct);
                    restoredPurchases.Add(result);
                }

                _logger.Info("[PurchaseService] Restored {0} purchases", restoredPurchases.Count);

                return RestoreResult.Success(restoredPurchases);
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] Restore exception: {0}", ex, ex.Message);
                return RestoreResult.Failure(PurchaseError.Unknown(ex.Message));
            }
        }

        public async Task<EntitlementsResponse> GetEntitlementsAsync(
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            if (!forceRefresh && _cachedEntitlements != null)
            {
                return _cachedEntitlements;
            }

            try
            {
                var request = new GetEntitlementsRequest { ForceRefresh = forceRefresh };
                var result = await _backendClient.GetEntitlementsAsync(request, ct);

                if (result.IsSuccess && result.Data != null)
                {
                    _cachedEntitlements = new EntitlementsResponse
                    {
                        Entitlements = result.Data.Entitlements,
                        ActiveSubscription = result.Data.ActiveSubscription,
                        ServerTimestampUtc = result.Data.ServerTimestampUtc
                    };

                    OnEntitlementsUpdated?.Invoke(_cachedEntitlements);
                }
                else
                {
                    _logger.Warning("[PurchaseService] Failed to get entitlements: {0}", result.Error);
                }

                return _cachedEntitlements ?? new EntitlementsResponse();
            }
            catch (Exception ex)
            {
                _logger.Error("[PurchaseService] GetEntitlements exception: {0}", ex, ex.Message);
                return _cachedEntitlements ?? new EntitlementsResponse();
            }
        }

        public bool HasEntitlement(string itemId)
        {
            if (_cachedEntitlements == null)
            {
                return false;
            }

            return _cachedEntitlements.Entitlements.Exists(e => e.ItemId == itemId);
        }

        public ProductInfo? GetProductInfo(string productId)
        {
            _productInfoCache.TryGetValue(productId, out var info);
            return info;
        }

        public async Task ProcessPendingPurchasesAsync(CancellationToken ct = default)
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
                        _pendingStore.Complete(purchase.TransactionId, purchase.Platform);
                    }
                    else if (!result.Error!.IsRetryable)
                    {
                        // Permanent failure - remove from pending
                        _pendingStore.Remove(purchase.TransactionId, purchase.Platform);
                    }
                    else
                    {
                        // Retryable failure
                        _pendingStore.MarkRetryFailed(
                            purchase.TransactionId,
                            purchase.Platform,
                            result.Error.Message);
                    }
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

        private async Task<PurchaseResult> VerifyAndGrantAsync(
            StoreReceipt receipt,
            ProductDefinition? productDef,
            bool isRestore,
            CancellationToken ct)
        {
            // Save to pending store for crash safety
            _pendingStore.Add(receipt, productDef);

            try
            {
                Dictionary<string, string>? metadata = null;
                if (receipt.Metadata != null && receipt.Metadata.Count > 0)
                {
                    metadata = new Dictionary<string, string>(receipt.Metadata);
                }
                else
                {
                    metadata = new Dictionary<string, string>();
                }

                metadata["restored"] = isRestore ? "true" : "false";

                var request = new VerifyPurchaseRequest
                {
                    Platform = receipt.Platform,
                    ProductId = receipt.ProductId,
                    TransactionId = receipt.TransactionId,
                    ReceiptPayload = receipt.ReceiptPayload,
                    ProductType = productDef?.Type.ToString() ?? "Unknown",
                    TierKey = productDef?.TierKey,
                    Metadata = metadata
                };

                _logger.Debug("[PurchaseService] Verifying purchase: {0} (tx: {1})",
                    receipt.ProductId, receipt.TransactionId);

                var verifyResult = await _backendClient.VerifyPurchaseAsync(request, ct);

                if (!verifyResult.IsSuccess || verifyResult.Data == null)
                {
                    var error = MapBackendError(verifyResult.Error);
                    _logger.Warning("[PurchaseService] Verification failed: {0}", error);

                    if (!error.IsRetryable)
                    {
                        _pendingStore.Remove(receipt.TransactionId, receipt.Platform);
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

                // Verification succeeded - confirm with store and remove from pending
                _storeClient.ConfirmPendingPurchase(receipt.ProductId, receipt.TransactionId);
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
                    receipt.ProductId, string.Join(", ", response.GrantedItemIds));

                var result = isRestore
                    ? PurchaseResult.Restored(
                        receipt.ProductId,
                        receipt.TransactionId,
                        response.GrantedItemIds,
                        response.Subscription?.TierKey)
                    : PurchaseResult.Success(
                        receipt.ProductId,
                        receipt.TransactionId,
                        response.GrantedItemIds,
                        response.Subscription?.TierKey);

                OnPurchaseCompleted?.Invoke(result);

                // Refresh entitlements
                _ = GetEntitlementsAsync(forceRefresh: true, ct);

                return result;
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
        }

        private async void HandlePendingPurchaseCompleted(StorePurchaseResult storeResult)
        {
            if (!storeResult.IsSuccess || storeResult.Receipt == null)
            {
                return;
            }

            var productDef = _catalogMapping.GetProduct(storeResult.Receipt.ProductId);
            var result = await VerifyAndGrantAsync(storeResult.Receipt, productDef, false, CancellationToken.None);

            // VerifyAndGrantAsync already emits success completion.
            // Emit only failures here to avoid duplicate success callbacks.
            if (!result.IsSuccess)
            {
                OnPurchaseCompleted?.Invoke(result);
            }
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
                "PRODUCT_NOT_ALLOWED" => PurchaseError.ProductNotAllowed(error.Message ?? ""),
                "VERIFICATION_FAILED" => PurchaseError.VerificationFailed(error.Message),
                "ALREADY_GRANTED" => PurchaseError.AlreadyOwned(error.Message ?? ""),
                _ => new PurchaseError(
                    PurchaseErrorCode.ServerError,
                    error.Message ?? "Server error",
                    error.Retryable)
            };
        }
    }
}
