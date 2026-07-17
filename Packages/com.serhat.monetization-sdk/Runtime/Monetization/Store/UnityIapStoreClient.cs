#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;
using UnityEngine;
using UnityEngine.Purchasing;
using SdkProductDefinition = Serhat.Backend.Monetization.Domain.ProductDefinition;
using SdkProductType = Serhat.Backend.Monetization.Domain.ProductType;
using UnityProduct = UnityEngine.Purchasing.Product;
using UnityProductDefinition = UnityEngine.Purchasing.ProductDefinition;
using UnityProductType = UnityEngine.Purchasing.ProductType;

namespace Serhat.Backend.Monetization.Store
{
    /// <summary>
    /// Unity IAP 5 implementation of <see cref="IStoreClient"/>.
    /// Store orders remain pending until the trusted backend verifies them.
    /// </summary>
    public sealed class UnityIapStoreClient :
        IResilientStoreClient,
        IStoreAccountBinding,
        IDisposable
    {
        private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PurchaseTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(45);

        private readonly Dictionary<string, SdkProductDefinition> _productDefinitions = new();
        private readonly Dictionary<string, PendingOrder> _pendingOrdersByTransaction = new();
        private readonly List<PendingOrder> _pendingOrdersWithoutTransaction = new();
        private readonly HashSet<string> _deferredProductIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _quarantinedProductIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _confirmationsInFlight = new(StringComparer.Ordinal);
        private readonly HashSet<string> _confirmedTransactions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<StoreOperationResult>>
            _confirmationCompletions = new(StringComparer.Ordinal);
        private readonly object _purchaseStateSync = new();
        private readonly SemaphoreSlim _storeOperationGate = new(1, 1);
        private readonly Dictionary<string, ProductInfo> _availableProductsById = new();
        private readonly CancellationTokenSource _lifetimeCancellation = new();

        private StoreController? _storeController;
        private Task<InitializationResult>? _initializationTask;
        private Task<StoreRestoreResult>? _restoreTask;
        private TaskCompletionSource<InitializationResult>? _productFetchCompletion;
        private TaskCompletionSource<bool>? _initialPurchasesFetchCompletion;
        private TaskCompletionSource<StorePurchaseResult>? _purchaseCompletion;
        private TaskCompletionSource<StoreRestoreResult>? _restoreCompletion;
        private StoreConnectionFailureDescription? _lastConnectionFailure;
        private PurchasesFetchFailureDescription? _lastPurchasesFetchFailure;
        private string? _currentPurchaseProductId;
        private string? _googleObfuscatedAccountId;
        private Guid? _appleAppAccountToken;
        private bool _isStoreConnected;
        private bool _productsFetched;
        private bool _initialPurchasesFetched;
        private bool _disposed;
        private IReadOnlyList<ProductInfo> _availableProducts = Array.Empty<ProductInfo>();

        public bool IsInitialized =>
            _isStoreConnected && _productsFetched && _initialPurchasesFetched;

        public event Action<StorePurchaseResult>? OnPendingPurchaseCompleted;

        public void SetGoogleObfuscatedAccountId(string accountId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 64)
            {
                throw new ArgumentException(
                    "Google Play obfuscated account ID must contain 1-64 characters.",
                    nameof(accountId));
            }

            lock (_purchaseStateSync)
            {
                if (_purchaseCompletion != null)
                {
                    throw new InvalidOperationException(
                        "Store account binding cannot change while a purchase is in progress.");
                }

                _googleObfuscatedAccountId = accountId;
            }

            _storeController?.GooglePlayStoreExtendedService?.SetObfuscatedAccountId(accountId);
        }

        public void SetAppleAppAccountToken(Guid appAccountToken)
        {
            ThrowIfDisposed();
            if (appAccountToken == Guid.Empty)
            {
                throw new ArgumentException(
                    "Apple app account token cannot be empty.",
                    nameof(appAccountToken));
            }

            lock (_purchaseStateSync)
            {
                if (_purchaseCompletion != null)
                {
                    throw new InvalidOperationException(
                        "Store account binding cannot change while a purchase is in progress.");
                }

                _appleAppAccountToken = appAccountToken;
            }

            _storeController?.AppleStoreExtendedService?.SetAppAccountToken(appAccountToken);
        }

        public async Task<InitializationResult> InitializeAsync(
            IReadOnlyList<SdkProductDefinition> products)
            => await InitializeAsync(products, CancellationToken.None);

        public async Task<InitializationResult> InitializeAsync(
            IReadOnlyList<SdkProductDefinition> products,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (products == null)
            {
                throw new ArgumentNullException(nameof(products));
            }

            if (IsInitialized)
            {
                return InitializationResult.Success(GetProducts());
            }

            var initializationTask = _initializationTask;
            if (initializationTask == null)
            {
                initializationTask = InitializeInternalAsync(products);
                _initializationTask = initializationTask;
            }

            try
            {
                return await AwaitOperationAsync(
                    initializationTask,
                    InitializationTimeout,
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                return InitializationResult.Failure(
                    PurchaseError.StoreUnavailable(exception.Message));
            }
            finally
            {
                if (initializationTask.IsCompleted &&
                    ReferenceEquals(_initializationTask, initializationTask))
                {
                    _initializationTask = null;
                }
            }
        }

        public Task<StorePurchaseResult> PurchaseAsync(string productId) =>
            PurchaseAsync(productId, CancellationToken.None);

        public async Task<StorePurchaseResult> PurchaseAsync(
            string productId,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsInitialized || _storeController == null)
            {
                return StorePurchaseResult.Failure(PurchaseError.StoreNotInitialized());
            }

            if (string.IsNullOrWhiteSpace(productId))
            {
                return StorePurchaseResult.Failure(PurchaseError.ProductNotFound(productId ?? string.Empty));
            }

            var product = _storeController.GetProductById(productId);
            if (product == null || !product.availableToPurchase)
            {
                return StorePurchaseResult.Failure(PurchaseError.ProductNotFound(productId));
            }

            if (HasPendingOrderForProduct(productId) ||
                _deferredProductIds.Contains(productId) ||
                _quarantinedProductIds.Contains(productId))
            {
                return StorePurchaseResult.Failure(PurchaseError.Pending());
            }

            if (!await _storeOperationGate.WaitAsync(0, cancellationToken))
            {
                return StorePurchaseResult.Failure(PurchaseError.Pending());
            }

            var completion = CreateCompletion<StorePurchaseResult>();
            lock (_purchaseStateSync)
            {
                _purchaseCompletion = completion;
                _currentPurchaseProductId = productId;
            }

            try
            {
                _storeController.PurchaseProduct(product);
                return await AwaitPurchaseCompletionAsync(
                    completion,
                    PurchaseTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _quarantinedProductIds.Add(productId);
                throw;
            }
            catch (TimeoutException)
            {
                _quarantinedProductIds.Add(productId);
                return StorePurchaseResult.Pending();
            }
            catch (Exception exception)
            {
                return StorePurchaseResult.Failure(
                    PurchaseError.StoreError(exception.GetType().Name, exception.Message));
            }
            finally
            {
                ClearCurrentPurchase(completion);

                _storeOperationGate.Release();
            }
        }

        public IReadOnlyList<ProductInfo> GetProducts()
        {
            if (!IsInitialized || _storeController == null)
            {
                return Array.Empty<ProductInfo>();
            }

            return _availableProducts;
        }

        public ProductInfo? GetProduct(string productId)
        {
            if (!IsInitialized || _storeController == null || string.IsNullOrWhiteSpace(productId))
            {
                return null;
            }

            return _availableProductsById.TryGetValue(productId, out var product)
                ? product
                : null;
        }

        public void ConfirmPendingPurchase(string productId, string transactionId)
        {
            _ = ObserveLegacyConfirmationAsync(productId, transactionId);
        }

        public async Task<StoreOperationResult> ConfirmPendingPurchaseAsync(
            string productId,
            string transactionId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (_storeController == null || !IsInitialized)
            {
                return StoreOperationResult.Failure(PurchaseError.StoreNotInitialized());
            }

            if (string.IsNullOrWhiteSpace(productId) ||
                string.IsNullOrWhiteSpace(transactionId))
            {
                return StoreOperationResult.Failure(
                    PurchaseError.VerificationFailed(
                        "Store confirmation requires an exact product and transaction identifier"));
            }

            if (_confirmedTransactions.Contains(transactionId))
            {
                return StoreOperationResult.Success();
            }

            var order = FindPendingOrder(productId, transactionId);
            if (order == null)
            {
                return StoreOperationResult.Failure(
                    PurchaseError.StoreUnavailable(
                        $"The pending order for '{productId}' is not available for confirmation"));
            }

            var confirmationKey = GetOrderKey(order, productId);
            if (!_confirmationCompletions.TryGetValue(confirmationKey, out var completion))
            {
                completion = CreateCompletion<StoreOperationResult>();
                _confirmationCompletions.Add(confirmationKey, completion);
                _confirmationsInFlight.Add(confirmationKey);

                try
                {
                    _storeController.ConfirmPurchase(order);
                }
                catch (Exception exception)
                {
                    CompleteConfirmation(
                        confirmationKey,
                        StoreOperationResult.Failure(
                            PurchaseError.StoreUnavailable(exception.Message)));
                }
            }

            try
            {
                return await AwaitOperationAsync(
                    completion.Task,
                    ConfirmationTimeout,
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                CompleteConfirmation(
                    confirmationKey,
                    StoreOperationResult.Failure(
                        PurchaseError.StoreUnavailable(exception.Message)));
                return StoreOperationResult.Failure(
                    PurchaseError.StoreUnavailable(exception.Message));
            }
        }

        public async Task<IReadOnlyList<StoreReceipt>> RestoreTransactionsAsync()
        {
            var result = await RestoreTransactionsAsync(CancellationToken.None);
            if (result.Status == StoreRestoreStatus.Failed)
            {
                throw new InvalidOperationException(
                    result.Error?.Message ?? "Store restore failed");
            }

            return result.Receipts;
        }

        public async Task<StoreRestoreResult> RestoreTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsInitialized || _storeController == null)
            {
                throw new InvalidOperationException("Store is not initialized");
            }

            var restoreTask = _restoreTask;
            if (restoreTask == null)
            {
                restoreTask = RestoreInternalAsync();
                _restoreTask = restoreTask;
            }

            try
            {
                return await AwaitOperationAsync(
                    restoreTask,
                    RestoreTimeout,
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                return StoreRestoreResult.Failure(
                    PurchaseError.StoreUnavailable(exception.Message));
            }
            finally
            {
                if (restoreTask.IsCompleted && ReferenceEquals(_restoreTask, restoreTask))
                {
                    _restoreTask = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetimeCancellation.Cancel();
            _isStoreConnected = false;
            _productsFetched = false;
            _initialPurchasesFetched = false;
            _availableProducts = Array.Empty<ProductInfo>();
            _availableProductsById.Clear();

            if (_storeController != null)
            {
                _storeController.OnStoreConnected -= HandleStoreConnected;
                _storeController.OnStoreDisconnected -= HandleStoreDisconnected;
                _storeController.OnProductsFetched -= HandleProductsFetched;
                _storeController.OnProductsFetchFailed -= HandleProductsFetchFailed;
                _storeController.OnPurchasePending -= HandlePurchasePending;
                _storeController.OnPurchaseDeferred -= HandlePurchaseDeferred;
                _storeController.OnPurchaseFailed -= HandlePurchaseFailed;
                _storeController.OnPurchaseConfirmed -= HandlePurchaseConfirmed;
                _storeController.OnPurchasesFetched -= HandlePurchasesFetched;
                _storeController.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;
            }

            _productFetchCompletion?.TrySetResult(
                InitializationResult.Failure(PurchaseError.StoreUnavailable("Store client disposed")));
            _initialPurchasesFetchCompletion?.TrySetResult(false);
            CompleteCurrentPurchase(
                StorePurchaseResult.Failure(PurchaseError.StoreUnavailable("Store client disposed")));
            _restoreCompletion?.TrySetResult(
                StoreRestoreResult.Failure(
                    PurchaseError.StoreUnavailable("Store client disposed")));

            foreach (var confirmation in _confirmationCompletions.Values)
            {
                confirmation.TrySetResult(
                    StoreOperationResult.Failure(
                        PurchaseError.StoreUnavailable("Store client disposed")));
            }

            _confirmationCompletions.Clear();
            _confirmationsInFlight.Clear();
            _lifetimeCancellation.Dispose();
        }

        private async Task<InitializationResult> InitializeInternalAsync(
            IReadOnlyList<SdkProductDefinition> products)
        {
            try
            {
                var unityProducts = BuildProductDefinitions(products, out var validationError);
                if (validationError != null)
                {
                    return InitializationResult.Failure(validationError);
                }

                EnsureStoreController();
                _lastConnectionFailure = null;
                _isStoreConnected = false;
                _productsFetched = false;
                _initialPurchasesFetched = false;
                _availableProducts = Array.Empty<ProductInfo>();
                _availableProductsById.Clear();
                _lastPurchasesFetchFailure = null;

                await AwaitOperationAsync(
                    _storeController!.Connect(),
                    InitializationTimeout,
                    _lifetimeCancellation.Token);
                if (!_isStoreConnected)
                {
                    var details = _lastConnectionFailure?.Message ?? "Could not connect to the store";
                    return InitializationResult.Failure(PurchaseError.StoreUnavailable(details));
                }

                var completion = CreateCompletion<InitializationResult>();
                _productFetchCompletion = completion;
                _storeController.FetchProducts(
                    unityProducts,
                    new MaximumNumberOfAttemptsRetryPolicy(5));

                var result = await AwaitOperationAsync(
                    completion.Task,
                    InitializationTimeout,
                    _lifetimeCancellation.Token);
                if (result.IsSuccess)
                {
                    var purchasesCompletion = CreateCompletion<bool>();
                    _initialPurchasesFetchCompletion = purchasesCompletion;
                    _storeController.FetchPurchases();

                    if (!await AwaitOperationAsync(
                            purchasesCompletion.Task,
                            InitializationTimeout,
                            _lifetimeCancellation.Token))
                    {
                        var details = _lastPurchasesFetchFailure?.Message ??
                                      "Could not fetch existing store purchases";
                        return InitializationResult.Failure(PurchaseError.StoreUnavailable(details));
                    }
                }

                return result;
            }
            catch (Exception exception)
            {
                return InitializationResult.Failure(PurchaseError.StoreUnavailable(exception.Message));
            }
            finally
            {
                _productFetchCompletion = null;
                _initialPurchasesFetchCompletion = null;
            }
        }

        private async Task<StoreRestoreResult> RestoreInternalAsync()
        {
            await _storeOperationGate.WaitAsync(_lifetimeCancellation.Token);
            if (_disposed)
            {
                _storeOperationGate.Release();
                throw new ObjectDisposedException(nameof(UnityIapStoreClient));
            }

            var completion = CreateCompletion<StoreRestoreResult>();
            _restoreCompletion = completion;

            try
            {
                _storeController!.RestoreTransactions((success, error) =>
                {
                    if (!success)
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            Debug.LogWarning($"[UnityIapStoreClient] Restore failed: {error}");
                        }

                        completion.TrySetResult(
                            StoreRestoreResult.Failure(
                                PurchaseError.StoreUnavailable(error ?? "Store restore failed")));
                    }

                    // On success, Unity IAP 5 calls FetchPurchases. OnPurchasesFetched
                    // completes this operation after the purchase cache is refreshed.
                });

                return await AwaitOperationAsync(
                    completion.Task,
                    RestoreTimeout,
                    _lifetimeCancellation.Token);
            }
            catch (TimeoutException exception)
            {
                return StoreRestoreResult.Failure(
                    PurchaseError.StoreUnavailable(exception.Message));
            }
            catch (OperationCanceledException) when (_disposed)
            {
                throw new ObjectDisposedException(nameof(UnityIapStoreClient));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UnityIapStoreClient] Restore failed: {exception.Message}");
                throw;
            }
            finally
            {
                if (ReferenceEquals(_restoreCompletion, completion))
                {
                    _restoreCompletion = null;
                }

                _storeOperationGate.Release();
            }
        }

        private void EnsureStoreController()
        {
            if (_storeController != null)
            {
                return;
            }

            _storeController = UnityIAPServices.StoreController();
            if (!string.IsNullOrWhiteSpace(_googleObfuscatedAccountId))
            {
                _storeController.GooglePlayStoreExtendedService?.SetObfuscatedAccountId(
                    _googleObfuscatedAccountId);
            }

            if (_appleAppAccountToken.HasValue)
            {
                _storeController.AppleStoreExtendedService?.SetAppAccountToken(
                    _appleAppAccountToken.Value);
            }

            _storeController.ProcessPendingOrdersOnPurchasesFetched(true);
            _storeController.OnStoreConnected += HandleStoreConnected;
            _storeController.OnStoreDisconnected += HandleStoreDisconnected;
            _storeController.OnProductsFetched += HandleProductsFetched;
            _storeController.OnProductsFetchFailed += HandleProductsFetchFailed;
            _storeController.OnPurchasePending += HandlePurchasePending;
            _storeController.OnPurchaseDeferred += HandlePurchaseDeferred;
            _storeController.OnPurchaseFailed += HandlePurchaseFailed;
            _storeController.OnPurchaseConfirmed += HandlePurchaseConfirmed;
            _storeController.OnPurchasesFetched += HandlePurchasesFetched;
            _storeController.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;
        }

        private List<UnityProductDefinition> BuildProductDefinitions(
            IReadOnlyList<SdkProductDefinition> products,
            out PurchaseError? validationError)
        {
            validationError = null;
            _productDefinitions.Clear();

            if (products.Count == 0)
            {
                validationError = PurchaseError.StoreUnavailable("The product catalog is empty");
                return new List<UnityProductDefinition>();
            }

            var result = new List<UnityProductDefinition>(products.Count);
            for (var index = 0; index < products.Count; index++)
            {
                var product = products[index];
                if (product == null || string.IsNullOrWhiteSpace(product.ProductId))
                {
                    validationError = PurchaseError.StoreUnavailable(
                        $"Product catalog entry {index} has no product ID");
                    return result;
                }

                if (_productDefinitions.ContainsKey(product.ProductId))
                {
                    validationError = PurchaseError.StoreUnavailable(
                        $"Product catalog contains duplicate ID '{product.ProductId}'");
                    return result;
                }

                _productDefinitions.Add(product.ProductId, product);
                result.Add(new UnityProductDefinition(product.ProductId, MapProductType(product.Type)));
            }

            return result;
        }

        private void HandleStoreConnected()
        {
            _isStoreConnected = true;
            _lastConnectionFailure = null;
        }

        private void HandleStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            _isStoreConnected = false;
            _productsFetched = false;
            _initialPurchasesFetched = false;
            _lastConnectionFailure = failure;
            _availableProducts = Array.Empty<ProductInfo>();
            _availableProductsById.Clear();

            _productFetchCompletion?.TrySetResult(
                InitializationResult.Failure(PurchaseError.StoreUnavailable(failure.Message)));
            _initialPurchasesFetchCompletion?.TrySetResult(false);
            CompleteCurrentPurchase(
                StorePurchaseResult.Failure(PurchaseError.StoreUnavailable(failure.Message)));
            _restoreCompletion?.TrySetResult(
                StoreRestoreResult.Failure(PurchaseError.StoreUnavailable(failure.Message)));

            var confirmationKeys = new List<string>(_confirmationCompletions.Keys);
            for (var index = 0; index < confirmationKeys.Count; index++)
            {
                CompleteConfirmation(
                    confirmationKeys[index],
                    StoreOperationResult.Failure(
                        PurchaseError.StoreUnavailable(failure.Message)));
            }
        }

        private void HandleProductsFetched(List<UnityProduct> products)
        {
            var availableProducts = new List<ProductInfo>(products.Count);
            _availableProductsById.Clear();
            for (var index = 0; index < products.Count; index++)
            {
                if (products[index].availableToPurchase)
                {
                    var product = MapProduct(products[index]);
                    availableProducts.Add(product);
                    _availableProductsById[product.ProductId] = product;
                }
            }

            if (availableProducts.Count == 0)
            {
                _productsFetched = false;
                _productFetchCompletion?.TrySetResult(
                    InitializationResult.Failure(
                        PurchaseError.StoreUnavailable("The store returned no purchasable products")));
                return;
            }

            _availableProducts = availableProducts.ToArray();
            _productsFetched = true;
            _productFetchCompletion?.TrySetResult(InitializationResult.Success(_availableProducts));
        }

        private void HandleProductsFetchFailed(ProductFetchFailed failure)
        {
            if (_productsFetched && _availableProducts.Count > 0)
            {
                Debug.LogWarning(
                    $"[UnityIapStoreClient] Some store products could not be fetched: " +
                    failure.FailureReason);
                return;
            }

            _productsFetched = false;
            _productFetchCompletion?.TrySetResult(
                InitializationResult.Failure(PurchaseError.StoreUnavailable(failure.FailureReason)));
        }

        private void HandlePurchasePending(PendingOrder order)
        {
            TrackPendingOrder(order);

            var receipt = ExtractReceipt(order);
            var productId = GetProductId(order);
            _quarantinedProductIds.Remove(productId);
            _deferredProductIds.Remove(productId);
            if (receipt == null || string.IsNullOrWhiteSpace(productId))
            {
                var failure = StorePurchaseResult.Failure(
                    PurchaseError.VerificationFailed("The store returned an incomplete purchase receipt"));

                if (!TryCompleteCurrentPurchase(productId, failure) &&
                    (_restoreCompletion == null || IsConsumable(order)))
                {
                    RaisePendingPurchaseCompleted(failure);
                }

                return;
            }

            var result = StorePurchaseResult.Success(receipt);
            if (!TryCompleteCurrentPurchase(productId, result) &&
                (_restoreCompletion == null || IsConsumable(order)))
            {
                RaisePendingPurchaseCompleted(result);
            }
        }

        private void HandlePurchaseDeferred(DeferredOrder order)
        {
            var productId = GetProductId(order);
            if (!string.IsNullOrWhiteSpace(productId))
            {
                _quarantinedProductIds.Remove(productId);
                _deferredProductIds.Add(productId);
            }

            TryCompleteCurrentPurchase(productId, StorePurchaseResult.Pending());

            Debug.Log($"[UnityIapStoreClient] Purchase deferred for '{productId}'.");
        }

        private void HandlePurchaseFailed(FailedOrder order)
        {
            var productId = GetProductId(order);
            _quarantinedProductIds.Remove(productId);
            var error = MapPurchaseFailure(order.FailureReason, productId, order.Details);
            TryCompleteCurrentPurchase(productId, StorePurchaseResult.Failure(error));

            Debug.LogWarning(
                $"[UnityIapStoreClient] Purchase failed for '{productId}': " +
                $"{order.FailureReason} ({order.Details})");
        }

        private void HandlePurchaseConfirmed(Order order)
        {
            var productId = GetProductId(order);
            var orderKey = GetOrderKey(order, productId);

            if (order is FailedOrder failedOrder)
            {
                Debug.LogWarning(
                    $"[UnityIapStoreClient] Confirmation failed for '{productId}': " +
                    $"{failedOrder.FailureReason} ({failedOrder.Details})");
                CompleteConfirmation(
                    orderKey,
                    StoreOperationResult.Failure(
                        MapPurchaseFailure(
                            failedOrder.FailureReason,
                            productId,
                            failedOrder.Details)));
                return;
            }

            if (!string.IsNullOrWhiteSpace(order.Info.TransactionID))
            {
                _confirmedTransactions.Add(order.Info.TransactionID);
            }

            RemovePendingOrder(order.Info.TransactionID, productId);
            CompleteConfirmation(orderKey, StoreOperationResult.Success());
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            TrackDeferredOrders(orders.DeferredOrders);
            TrackConfirmedOrders(orders.ConfirmedOrders);
            _initialPurchasesFetched = true;
            _initialPurchasesFetchCompletion?.TrySetResult(true);

            var completion = _restoreCompletion;
            if (completion == null)
            {
                return;
            }

            var receipts = new List<StoreReceipt>(
                orders.ConfirmedOrders.Count + orders.PendingOrders.Count);
            var receiptKeys = new HashSet<string>(StringComparer.Ordinal);

            var errors = new List<PurchaseError>();
            AddReceipts(orders.ConfirmedOrders, receipts, receiptKeys, errors);
            AddReceipts(orders.PendingOrders, receipts, receiptKeys, errors);

            completion.TrySetResult(
                errors.Count > 0 && receipts.Count > 0
                    ? StoreRestoreResult.Partial(receipts, errors)
                    : errors.Count > 0
                        ? StoreRestoreResult.Failure(errors[0])
                        : StoreRestoreResult.Success(receipts));
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            _lastPurchasesFetchFailure = failure;
            if (_initialPurchasesFetchCompletion != null)
            {
                _initialPurchasesFetched = false;
                _initialPurchasesFetchCompletion.TrySetResult(false);
            }
            Debug.LogWarning(
                $"[UnityIapStoreClient] Could not fetch store purchases: {failure.Message}");
            _restoreCompletion?.TrySetResult(
                StoreRestoreResult.Failure(PurchaseError.StoreUnavailable(failure.Message)));
        }

        private void AddReceipts<TOrder>(
            IReadOnlyList<TOrder> orders,
            ICollection<StoreReceipt> receipts,
            ISet<string> receiptKeys,
            ICollection<PurchaseError> errors)
            where TOrder : Order
        {
            for (var index = 0; index < orders.Count; index++)
            {
                if (IsConsumable(orders[index]))
                {
                    continue;
                }

                var receipt = ExtractReceipt(orders[index]);
                if (receipt == null)
                {
                    errors.Add(
                        PurchaseError.VerificationFailed(
                            $"The store returned an incomplete receipt for '{GetProductId(orders[index])}'"));
                    continue;
                }

                var key = $"{receipt.Platform}\n{receipt.TransactionId}\n{receipt.ProductId}";
                if (receiptKeys.Add(key))
                {
                    receipts.Add(receipt);
                }
            }
        }

        private StoreReceipt? ExtractReceipt(Order order)
        {
            var product = GetProduct(order);
            if (product == null)
            {
                return null;
            }

            var transactionId = order.Info.TransactionID ?? string.Empty;
            var platform = ResolvePlatform(order);
            // Apple verification queries App Store Server API by transaction ID. Do not extract
            // AppReceipt/JWS here: keeping the payload empty prevents raw signed store data from
            // entering pending storage or the backend request. Google still requires its token.
            var receiptPayload = string.Equals(platform, "apple", StringComparison.Ordinal)
                ? string.Empty
                : string.Equals(platform, "google", StringComparison.Ordinal)
                    ? transactionId
                    : order.Info.Receipt ?? string.Empty;

            if (string.IsNullOrWhiteSpace(transactionId) ||
                (!string.Equals(platform, "apple", StringComparison.Ordinal) &&
                 string.IsNullOrWhiteSpace(receiptPayload)))
            {
                return null;
            }

            var receipt = new StoreReceipt
            {
                Platform = platform,
                ProductId = product.definition.id,
                TransactionId = transactionId,
                ReceiptPayload = receiptPayload
            };

            if (string.Equals(platform, "google", StringComparison.Ordinal))
            {
                receipt.Metadata["packageName"] = Application.identifier;
                receipt.Metadata["isSubscription"] =
                    (product.definition.type == UnityProductType.Subscription).ToString();
            }

            return receipt;
        }

        private ProductInfo MapProduct(UnityProduct product)
        {
            _productDefinitions.TryGetValue(product.definition.id, out var definition);
            var metadata = product.metadata;

            return new ProductInfo(
                product.definition.id,
                MapProductType(product.definition.type),
                metadata?.localizedTitle ?? string.Empty,
                metadata?.localizedDescription ?? string.Empty,
                metadata?.localizedPriceString ?? string.Empty,
                metadata?.localizedPrice ?? 0m,
                metadata?.isoCurrencyCode ?? string.Empty,
                definition?.TierKey);
        }

        private void TrackPendingOrder(PendingOrder order)
        {
            var transactionId = order.Info.TransactionID;
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                _pendingOrdersByTransaction[transactionId] = order;
                return;
            }

            if (!_pendingOrdersWithoutTransaction.Contains(order))
            {
                _pendingOrdersWithoutTransaction.Add(order);
            }
        }

        private void TrackConfirmedOrders(IReadOnlyList<ConfirmedOrder> orders)
        {
            for (var index = 0; index < orders.Count; index++)
            {
                var transactionId = orders[index].Info.TransactionID;
                if (!string.IsNullOrWhiteSpace(transactionId))
                {
                    _confirmedTransactions.Add(transactionId);
                    _pendingOrdersByTransaction.Remove(transactionId);
                }
            }
        }

        private bool HasPendingOrderForProduct(string productId)
        {
            foreach (var pendingOrder in _pendingOrdersByTransaction.Values)
            {
                if (string.Equals(GetProductId(pendingOrder), productId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            for (var index = 0; index < _pendingOrdersWithoutTransaction.Count; index++)
            {
                if (string.Equals(
                        GetProductId(_pendingOrdersWithoutTransaction[index]),
                        productId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void TrackDeferredOrders(IReadOnlyList<DeferredOrder> orders)
        {
            _deferredProductIds.Clear();
            for (var index = 0; index < orders.Count; index++)
            {
                var productId = GetProductId(orders[index]);
                if (!string.IsNullOrWhiteSpace(productId))
                {
                    _deferredProductIds.Add(productId);
                }
            }
        }

        private PendingOrder? FindPendingOrder(string productId, string transactionId)
        {
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                return _pendingOrdersByTransaction.TryGetValue(transactionId, out var order) &&
                       string.Equals(GetProductId(order), productId, StringComparison.Ordinal)
                    ? order
                    : null;
            }

            foreach (var pendingOrder in _pendingOrdersByTransaction.Values)
            {
                if (string.Equals(GetProductId(pendingOrder), productId, StringComparison.Ordinal))
                {
                    return pendingOrder;
                }
            }

            for (var index = 0; index < _pendingOrdersWithoutTransaction.Count; index++)
            {
                if (string.Equals(
                        GetProductId(_pendingOrdersWithoutTransaction[index]),
                        productId,
                        StringComparison.Ordinal))
                {
                    return _pendingOrdersWithoutTransaction[index];
                }
            }

            return null;
        }

        private void RemovePendingOrder(string transactionId, string productId)
        {
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                _pendingOrdersByTransaction.Remove(transactionId);
            }

            for (var index = _pendingOrdersWithoutTransaction.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        GetProductId(_pendingOrdersWithoutTransaction[index]),
                        productId,
                        StringComparison.Ordinal))
                {
                    _pendingOrdersWithoutTransaction.RemoveAt(index);
                }
            }
        }

        private bool TryCompleteCurrentPurchase(
            string productId,
            StorePurchaseResult result)
        {
            TaskCompletionSource<StorePurchaseResult>? completion;
            lock (_purchaseStateSync)
            {
                if (_purchaseCompletion == null ||
                    string.IsNullOrWhiteSpace(_currentPurchaseProductId) ||
                    !string.Equals(
                        _currentPurchaseProductId,
                        productId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                completion = _purchaseCompletion;
                _purchaseCompletion = null;
                _currentPurchaseProductId = null;
            }

            // A timeout/cancellation atomically closes the completion first. In that case
            // the caller can no longer receive this receipt, so the event recovery path must
            // persist and verify it instead.
            return completion.TrySetResult(result);
        }

        private void CompleteCurrentPurchase(StorePurchaseResult result)
        {
            TaskCompletionSource<StorePurchaseResult>? completion;
            lock (_purchaseStateSync)
            {
                completion = _purchaseCompletion;
                _purchaseCompletion = null;
                _currentPurchaseProductId = null;
            }

            completion?.TrySetResult(result);
        }

        private void ClearCurrentPurchase(
            TaskCompletionSource<StorePurchaseResult> completion)
        {
            lock (_purchaseStateSync)
            {
                if (!ReferenceEquals(_purchaseCompletion, completion))
                {
                    return;
                }

                _purchaseCompletion = null;
                _currentPurchaseProductId = null;
            }
        }

        private void CompleteConfirmation(string confirmationKey, StoreOperationResult result)
        {
            _confirmationsInFlight.Remove(confirmationKey);
            if (_confirmationCompletions.Remove(confirmationKey, out var completion))
            {
                completion.TrySetResult(result);
            }
        }

        private async Task ObserveLegacyConfirmationAsync(
            string productId,
            string transactionId)
        {
            try
            {
                var result = await ConfirmPendingPurchaseAsync(
                    productId,
                    transactionId,
                    _lifetimeCancellation.Token);
                if (!result.IsSuccess)
                {
                    Debug.LogWarning(
                        $"[UnityIapStoreClient] Store confirmation was not completed for '{productId}': " +
                        result.Error?.Message);
                }
            }
            catch (OperationCanceledException) when (_disposed)
            {
                // Expected during application shutdown.
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[UnityIapStoreClient] Store confirmation failed for '{productId}': " +
                    exception.Message);
            }
        }

        private void RaisePendingPurchaseCompleted(StorePurchaseResult result)
        {
            try
            {
                OnPendingPurchaseCompleted?.Invoke(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static UnityProduct? GetProduct(Order order)
        {
            var items = order.CartOrdered.Items();
            return items.Count > 0 ? items[0].Product : null;
        }

        private static string GetProductId(Order order) =>
            GetProduct(order)?.definition.id ?? string.Empty;

        private static bool IsConsumable(Order order) =>
            GetProduct(order)?.definition.type == UnityProductType.Consumable;

        private static string GetOrderKey(Order order, string productId) =>
            !string.IsNullOrWhiteSpace(order.Info.TransactionID)
                ? order.Info.TransactionID
                : productId;

        private static string ResolvePlatform(Order order)
        {
            if (order.Info.Google != null)
            {
                return "google";
            }

            if (order.Info.Apple != null)
            {
                return "apple";
            }

#if UNITY_ANDROID
            return "google";
#elif UNITY_IOS || UNITY_TVOS || UNITY_STANDALONE_OSX
            return "apple";
#else
            return "unknown";
#endif
        }

        private static UnityProductType MapProductType(SdkProductType type)
        {
            return type switch
            {
                SdkProductType.Consumable => UnityProductType.Consumable,
                SdkProductType.NonConsumable => UnityProductType.NonConsumable,
                SdkProductType.Subscription => UnityProductType.Subscription,
                _ => UnityProductType.Unknown
            };
        }

        private static SdkProductType MapProductType(UnityProductType type)
        {
            return type switch
            {
                UnityProductType.NonConsumable => SdkProductType.NonConsumable,
                UnityProductType.Subscription => SdkProductType.Subscription,
                _ => SdkProductType.Consumable
            };
        }

        private static PurchaseError MapPurchaseFailure(
            PurchaseFailureReason reason,
            string productId,
            string? details)
        {
            return reason switch
            {
                PurchaseFailureReason.UserCancelled => PurchaseError.UserCancelled(),
                PurchaseFailureReason.PurchasingUnavailable => PurchaseError.StoreUnavailable(details),
                PurchaseFailureReason.StoreNotConnected => PurchaseError.StoreUnavailable(details),
                PurchaseFailureReason.ExistingPurchasePending => PurchaseError.Pending(),
                PurchaseFailureReason.ProductUnavailable => PurchaseError.ProductNotFound(productId),
                PurchaseFailureReason.SignatureInvalid =>
                    PurchaseError.VerificationFailed(details ?? "Store signature is invalid"),
                PurchaseFailureReason.ValidationFailure =>
                    PurchaseError.VerificationFailed(details ?? "Store validation failed"),
                PurchaseFailureReason.DuplicateTransaction => PurchaseError.AlreadyOwned(productId),
                PurchaseFailureReason.PaymentDeclined =>
                    PurchaseError.StoreError(nameof(PurchaseFailureReason.PaymentDeclined),
                        details ?? "Payment was declined"),
                PurchaseFailureReason.PurchaseMissing =>
                    PurchaseError.StoreError(nameof(PurchaseFailureReason.PurchaseMissing),
                        details ?? "The store returned no purchase"),
                _ => PurchaseError.Unknown(details ?? reason.ToString())
            };
        }

        private static TaskCompletionSource<T> CreateCompletion<T>() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task<StorePurchaseResult> AwaitPurchaseCompletionAsync(
            TaskCompletionSource<StorePurchaseResult> completion,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (completion.Task.IsCompleted)
            {
                return await completion.Task;
            }

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
            var completed = await Task.WhenAny(completion.Task, timeoutTask);
            if (ReferenceEquals(completed, completion.Task))
            {
                timeoutCancellation.Cancel();
                return await completion.Task;
            }

            // Close the operation before returning timeout/cancellation. If a store callback
            // wins this race, TrySetCanceled fails and its result is returned to the caller.
            // If cancellation wins, a later callback cannot be swallowed by this abandoned
            // task and is routed through OnPendingPurchaseCompleted.
            if (!completion.TrySetCanceled())
            {
                return await completion.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The store operation did not complete within {timeout.TotalSeconds:0} seconds.");
        }

        private static async Task<T> AwaitOperationAsync<T>(
            Task<T> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (operation.IsCompleted)
            {
                return await operation;
            }

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
            var completed = await Task.WhenAny(operation, timeoutTask);
            if (ReferenceEquals(completed, operation))
            {
                timeoutCancellation.Cancel();
                return await operation;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The store operation did not complete within {timeout.TotalSeconds:0} seconds.");
        }

        private static async Task AwaitOperationAsync(
            Task operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (operation.IsCompleted)
            {
                await operation;
                return;
            }

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
            var completed = await Task.WhenAny(operation, timeoutTask);
            if (ReferenceEquals(completed, operation))
            {
                timeoutCancellation.Cancel();
                await operation;
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The store operation did not complete within {timeout.TotalSeconds:0} seconds.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UnityIapStoreClient));
            }
        }
    }
}
