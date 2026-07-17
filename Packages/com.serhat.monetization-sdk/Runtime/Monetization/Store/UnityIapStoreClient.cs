#if UNITY_PURCHASING
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using SdkProductDefinition = Serhat.Backend.Monetization.Domain.ProductDefinition;
using SdkProductType = Serhat.Backend.Monetization.Domain.ProductType;

namespace Serhat.Backend.Monetization.Store
{
    /// <summary>
    /// Unity IAP implementation of IStoreClient.
    /// </summary>
    public sealed class UnityIapStoreClient : IStoreClient, IDetailedStoreListener
    {
        private IStoreController? _storeController;
        private IExtensionProvider? _extensionProvider;
        private IAppleExtensions? _appleExtensions;
        private IGooglePlayStoreExtensions? _googleExtensions;

        private TaskCompletionSource<InitializationResult>? _initTcs;
        private TaskCompletionSource<StorePurchaseResult>? _purchaseTcs;
        private TaskCompletionSource<IReadOnlyList<StoreReceipt>>? _restoreTcs;

        private readonly Dictionary<string, SdkProductDefinition> _productDefinitions = new();
        private readonly List<StoreReceipt> _restoredReceipts = new();
        private string? _currentPurchaseProductId;

        public bool IsInitialized => _storeController != null;

        public event Action<StorePurchaseResult>? OnPendingPurchaseCompleted;

        public async Task<InitializationResult> InitializeAsync(IReadOnlyList<SdkProductDefinition> products)
        {
            if (IsInitialized)
            {
                return InitializationResult.Success(GetProducts());
            }

            _initTcs = new TaskCompletionSource<InitializationResult>();

            // ── Diagnostic info ──
            Debug.Log($"[UnityIapStoreClient] ═══════════════ IAP INIT START ═══════════════");
            Debug.Log($"[UnityIapStoreClient] Bundle ID: {Application.identifier}");
            Debug.Log($"[UnityIapStoreClient] Platform: {Application.platform}");
            Debug.Log($"[UnityIapStoreClient] Unity Version: {Application.unityVersion}");
            Debug.Log($"[UnityIapStoreClient] Is Editor: {Application.isEditor}");
            Debug.Log($"[UnityIapStoreClient] Environment: {(Debug.isDebugBuild ? "DEBUG" : "RELEASE")}");

            // Build Unity IAP configuration
            var module = StandardPurchasingModule.Instance();
            module.useFakeStoreUIMode = FakeStoreUIMode.Default;
            Debug.Log($"[UnityIapStoreClient] Store module: {module.appStore}");
            var builder = ConfigurationBuilder.Instance(module);

            _productDefinitions.Clear();
            Debug.Log($"[UnityIapStoreClient] Registering {products.Count} products:");
            foreach (var product in products)
            {
                _productDefinitions[product.ProductId] = product;

                var unityType = product.Type switch
                {
                    SdkProductType.Consumable => UnityEngine.Purchasing.ProductType.Consumable,
                    SdkProductType.NonConsumable => UnityEngine.Purchasing.ProductType.NonConsumable,
                    SdkProductType.Subscription => UnityEngine.Purchasing.ProductType.Subscription,
                    _ => UnityEngine.Purchasing.ProductType.Consumable
                };

                Debug.Log($"[UnityIapStoreClient]   → ID: '{product.ProductId}' | Type: {unityType}");
                builder.AddProduct(product.ProductId, unityType);
            }

            Debug.Log($"[UnityIapStoreClient] Calling UnityPurchasing.Initialize with {_productDefinitions.Count} products...");
            UnityPurchasing.Initialize(this, builder);

            return await _initTcs.Task;
        }

        public async Task<StorePurchaseResult> PurchaseAsync(string productId)
        {
            if (!IsInitialized || _storeController == null)
            {
                return StorePurchaseResult.Failure(PurchaseError.StoreNotInitialized());
            }

            var product = _storeController.products.WithID(productId);
            if (product == null || !product.availableToPurchase)
            {
                return StorePurchaseResult.Failure(PurchaseError.ProductNotFound(productId));
            }

            _purchaseTcs = new TaskCompletionSource<StorePurchaseResult>();
            _currentPurchaseProductId = productId;

            _storeController.InitiatePurchase(product);

            return await _purchaseTcs.Task;
        }

        public IReadOnlyList<ProductInfo> GetProducts()
        {
            if (!IsInitialized || _storeController == null)
            {
                return Array.Empty<ProductInfo>();
            }

            var result = new List<ProductInfo>();
            foreach (var product in _storeController.products.all)
            {
                if (product.availableToPurchase)
                {
                    _productDefinitions.TryGetValue(product.definition.id, out var def);

                    result.Add(new ProductInfo(
                        productId: product.definition.id,
                        type: MapProductType(product.definition.type),
                        title: product.metadata.localizedTitle,
                        description: product.metadata.localizedDescription,
                        priceString: product.metadata.localizedPriceString,
                        priceDecimal: product.metadata.localizedPrice,
                        currencyCode: product.metadata.isoCurrencyCode,
                        tierKey: def?.TierKey
                    ));
                }
            }

            return result;
        }

        public ProductInfo? GetProduct(string productId)
        {
            if (!IsInitialized || _storeController == null)
            {
                return null;
            }

            var product = _storeController.products.WithID(productId);
            if (product == null || !product.availableToPurchase)
            {
                return null;
            }

            _productDefinitions.TryGetValue(productId, out var def);

            return new ProductInfo(
                productId: product.definition.id,
                type: MapProductType(product.definition.type),
                title: product.metadata.localizedTitle,
                description: product.metadata.localizedDescription,
                priceString: product.metadata.localizedPriceString,
                priceDecimal: product.metadata.localizedPrice,
                currencyCode: product.metadata.isoCurrencyCode,
                tierKey: def?.TierKey
            );
        }

        public void ConfirmPendingPurchase(string productId, string transactionId)
        {
            if (_storeController == null) return;

            var product = _storeController.products.WithID(productId);
            if (product != null)
            {
                _storeController.ConfirmPendingPurchase(product);
            }
        }

        public async Task<IReadOnlyList<StoreReceipt>> RestoreTransactionsAsync()
        {
            if (!IsInitialized)
            {
                return Array.Empty<StoreReceipt>();
            }

            _restoredReceipts.Clear();
            _restoreTcs = new TaskCompletionSource<IReadOnlyList<StoreReceipt>>();

#if UNITY_IOS
            if (_appleExtensions != null)
            {
                _appleExtensions.RestoreTransactions(OnRestoreComplete);
                return await _restoreTcs.Task;
            }
#endif

#if UNITY_ANDROID
            if (_googleExtensions != null)
            {
                _googleExtensions.RestoreTransactions(OnRestoreComplete);
                return await _restoreTcs.Task;
            }
#endif

            // For editor/other platforms, return empty
            _restoreTcs.TrySetResult(_restoredReceipts);
            return await _restoreTcs.Task;
        }

        private void OnRestoreComplete(bool success, string? error)
        {
            if (success)
            {
                _restoreTcs?.TrySetResult(_restoredReceipts);
            }
            else
            {
                Debug.LogWarning($"[UnityIapStoreClient] Restore failed: {error}");
                _restoreTcs?.TrySetResult(Array.Empty<StoreReceipt>());
            }
        }

        #region IDetailedStoreListener

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _storeController = controller;
            _extensionProvider = extensions;

#if UNITY_IOS
            _appleExtensions = extensions.GetExtension<IAppleExtensions>();
            _appleExtensions?.RegisterPurchaseDeferredListener(OnPurchaseDeferred);
#endif

#if UNITY_ANDROID
            _googleExtensions = extensions.GetExtension<IGooglePlayStoreExtensions>();
#endif

            Debug.Log($"[UnityIapStoreClient] ═══════════════ IAP INIT SUCCESS ═══════════════");

            // Log ALL products from store (including unavailable ones)
            foreach (var product in controller.products.all)
            {
                Debug.Log($"[UnityIapStoreClient] Store product: '{product.definition.id}' | Available: {product.availableToPurchase} | Price: '{product.metadata?.localizedPriceString}' | Title: '{product.metadata?.localizedTitle}'");
            }

            var products = GetProducts();
            _initTcs?.TrySetResult(InitializationResult.Success(products));

            Debug.Log($"[UnityIapStoreClient] Available products: {products.Count}/{controller.products.all.Length}");
            Debug.Log($"[UnityIapStoreClient] ═══════════════════════════════════════════════");
        }

        public void OnInitializeFailed(InitializationFailureReason error)
        {
            LogInitFailureDetails(error, null);
            var purchaseError = MapInitFailure(error);
            _initTcs?.TrySetResult(InitializationResult.Failure(purchaseError));
        }

        public void OnInitializeFailed(InitializationFailureReason error, string? message)
        {
            LogInitFailureDetails(error, message);
            var purchaseError = MapInitFailure(error, message);
            _initTcs?.TrySetResult(InitializationResult.Failure(purchaseError));
        }

        private void LogInitFailureDetails(InitializationFailureReason error, string? message)
        {
            Debug.LogError($"[UnityIapStoreClient] ═══════════════ IAP INIT FAILED ═══════════════");
            Debug.LogError($"[UnityIapStoreClient] Reason: {error}");
            Debug.LogError($"[UnityIapStoreClient] Message: {message ?? "(no message)"}");
            Debug.LogError($"[UnityIapStoreClient] Bundle ID: {Application.identifier}");
            Debug.LogError($"[UnityIapStoreClient] Platform: {Application.platform}");
            Debug.LogError($"[UnityIapStoreClient] Registered products ({_productDefinitions.Count}):");
            foreach (var kvp in _productDefinitions)
            {
                Debug.LogError($"[UnityIapStoreClient]   → '{kvp.Key}' (Type: {kvp.Value.Type})");
            }
            Debug.LogError($"[UnityIapStoreClient] ═══════════════════════════════════════════════");
            Debug.LogError($"[UnityIapStoreClient] Possible causes:");
            Debug.LogError($"[UnityIapStoreClient]   1. Product IDs don't match store (App Store Connect / Google Play)");
            Debug.LogError($"[UnityIapStoreClient]   2. IAPs not submitted with an app version yet");
            Debug.LogError($"[UnityIapStoreClient]   3. Paid Apps Agreement not active");
            Debug.LogError($"[UnityIapStoreClient]   4. Bundle ID mismatch between build and store");
            Debug.LogError($"[UnityIapStoreClient]   5. Products have 'Missing Metadata' status in store");
            Debug.LogError($"[UnityIapStoreClient]   6. Network connectivity issue");
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            var receipt = ExtractReceipt(args.purchasedProduct);

            // Check if this is for the current purchase or a restored/pending purchase
            if (_currentPurchaseProductId == args.purchasedProduct.definition.id)
            {
                _purchaseTcs?.TrySetResult(StorePurchaseResult.Success(receipt));
                _currentPurchaseProductId = null;
            }
            else
            {
                // This is a restored or pending purchase completing
                _restoredReceipts.Add(receipt);
                OnPendingPurchaseCompleted?.Invoke(StorePurchaseResult.Success(receipt));
            }

            // Return Pending - we'll confirm after server verification
            return PurchaseProcessingResult.Pending;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            var error = MapPurchaseFailure(failureReason, product.definition.id);

            if (_currentPurchaseProductId == product.definition.id)
            {
                _purchaseTcs?.TrySetResult(StorePurchaseResult.Failure(error));
                _currentPurchaseProductId = null;
            }

            Debug.LogWarning($"[UnityIapStoreClient] Purchase failed: {product.definition.id} - {failureReason}");
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            var error = MapPurchaseFailure(
                failureDescription.reason,
                product.definition.id,
                failureDescription.message);

            if (_currentPurchaseProductId == product.definition.id)
            {
                _purchaseTcs?.TrySetResult(StorePurchaseResult.Failure(error));
                _currentPurchaseProductId = null;
            }

            Debug.LogWarning($"[UnityIapStoreClient] Purchase failed: {product.definition.id} - {failureDescription.reason}: {failureDescription.message}");
        }

        #endregion

        #region Helpers

        private void OnPurchaseDeferred(Product product)
        {
            Debug.Log($"[UnityIapStoreClient] Purchase deferred (pending): {product.definition.id}");

            var receipt = ExtractReceipt(product);
            if (_currentPurchaseProductId == product.definition.id)
            {
                _purchaseTcs?.TrySetResult(StorePurchaseResult.Pending(receipt));
                _currentPurchaseProductId = null;
            }
        }

        private StoreReceipt ExtractReceipt(Product product)
        {
            var receipt = new StoreReceipt
            {
                ProductId = product.definition.id,
                TransactionId = product.transactionID,
                ReceiptPayload = product.receipt
            };

#if UNITY_IOS
            receipt.Platform = "apple";
#elif UNITY_ANDROID
            receipt.Platform = "google";
            receipt.Metadata["packageName"] = Application.identifier;
            receipt.Metadata["isSubscription"] = (product.definition.type == UnityEngine.Purchasing.ProductType.Subscription).ToString();
#else
            receipt.Platform = "unknown";
#endif

            return receipt;
        }

        private static SdkProductType MapProductType(UnityEngine.Purchasing.ProductType type)
        {
            return type switch
            {
                UnityEngine.Purchasing.ProductType.Consumable => SdkProductType.Consumable,
                UnityEngine.Purchasing.ProductType.NonConsumable => SdkProductType.NonConsumable,
                UnityEngine.Purchasing.ProductType.Subscription => SdkProductType.Subscription,
                _ => SdkProductType.Consumable
            };
        }

        private static PurchaseError MapInitFailure(InitializationFailureReason reason, string? message = null)
        {
            return reason switch
            {
                InitializationFailureReason.PurchasingUnavailable =>
                    PurchaseError.StoreUnavailable(message ?? "Purchasing unavailable"),
                InitializationFailureReason.NoProductsAvailable =>
                    PurchaseError.StoreUnavailable(message ?? "No products available"),
                InitializationFailureReason.AppNotKnown =>
                    PurchaseError.StoreUnavailable(message ?? "App not known to store"),
                _ =>
                    PurchaseError.Unknown(message ?? reason.ToString())
            };
        }

        private static PurchaseError MapPurchaseFailure(PurchaseFailureReason reason, string productId, string? message = null)
        {
            return reason switch
            {
                PurchaseFailureReason.UserCancelled => PurchaseError.UserCancelled(),
                PurchaseFailureReason.PurchasingUnavailable => PurchaseError.StoreUnavailable(message),
                PurchaseFailureReason.ExistingPurchasePending => PurchaseError.Pending(),
                PurchaseFailureReason.ProductUnavailable => PurchaseError.ProductNotFound(productId),
                PurchaseFailureReason.SignatureInvalid => PurchaseError.VerificationFailed("Signature invalid"),
                PurchaseFailureReason.DuplicateTransaction => PurchaseError.AlreadyOwned(productId),
                PurchaseFailureReason.Unknown => PurchaseError.Unknown(message),
                _ => PurchaseError.StoreError(reason.ToString(), message ?? reason.ToString())
            };
        }

        #endregion
    }
}
#endif
