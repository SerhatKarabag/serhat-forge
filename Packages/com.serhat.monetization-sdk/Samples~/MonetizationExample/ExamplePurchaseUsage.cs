#nullable enable

using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Backend;
using Serhat.Backend.Monetization.Domain;
using Serhat.Backend.Monetization.Persistence;
using Serhat.Backend.Monetization.Services;
using Serhat.Backend.Monetization.Store;
using UnityEngine;

namespace Serhat.Backend.Monetization.Samples
{
    /// <summary>
    /// Example usage of the monetization system.
    /// Shows how to set up and use IPurchaseService in a game.
    /// </summary>
    public class ExamplePurchaseUsage : MonoBehaviour
    {
        private IPurchaseService? _purchaseService;

        // UI references (assign in inspector)
        public UnityEngine.UI.Button? buyCoinsButton;
        public UnityEngine.UI.Button? buyPremiumButton;
        public UnityEngine.UI.Button? buyProButton;
        public UnityEngine.UI.Button? restoreButton;
        public UnityEngine.UI.Text? statusText;

        [Tooltip("Local sample identity only. Production must use the authenticated PlayFab title-player ID.")]
        public string localDevelopmentPlayerId = "local-development-player";

        private async void Start()
        {
            // Initialize the purchase service
            _purchaseService = CreatePurchaseService();

            // Subscribe to events
            _purchaseService.OnInitialized += OnStoreInitialized;
            _purchaseService.OnPurchaseCompleted += OnPurchaseCompleted;
            _purchaseService.OnEntitlementsUpdated += OnEntitlementsUpdated;

            // Initialize
            UpdateStatus("Initializing store...");
            await _purchaseService.InitializeAsync();
        }

        private void OnDestroy()
        {
            if (_purchaseService is System.IDisposable disposable)
            {
                disposable.Dispose();
            }

            _purchaseService = null;
        }

        private IPurchaseService CreatePurchaseService()
        {
            // 1. Create the store client (Unity IAP wrapper)
            var storeClient = new UnityIapStoreClient();
            storeClient.SetGoogleObfuscatedAccountId(
                StoreAccountIdentity.CreateGoogleObfuscatedAccountId(localDevelopmentPlayerId));
            storeClient.SetAppleAppAccountToken(
                StoreAccountIdentity.CreateAppleAppAccountToken(localDevelopmentPlayerId));

            // 2. Create the backend client
            // In a real app, you'd get the invoker from your existing backend SDK setup
            // For example:
            // var invoker = existingBackendSdk.GetInvoker();
            // var backendClient = new MonetizationBackendClientBuilder()
            //     .WithInvoker(invoker)
            //     .Build();

            // For this example, we'll create a mock
            IMonetizationBackendClient backendClient = CreateMockBackendClient();

            // 3. Create game-specific mappings
            var catalogMapping = new ExampleProductCatalog();
            var tierPolicy = new ExampleTierPolicy();

            // 4. Create local-development persistence. FileStorage is plaintext;
            // production games should inject protected/encrypted IStorage instead.
            var storage = new FileStorage(System.IO.Path.Combine(
                Application.persistentDataPath,
                "serhat_forge",
                "monetization"));
            var clock = SystemClock.Instance;
            var pendingStore = new PendingPurchaseStore(storage, clock);

            // 5. Create the purchase service
            var logger = new UnityBackendLogger();
            return new PurchaseService(
                storeClient,
                backendClient,
                catalogMapping,
                tierPolicy,
                pendingStore,
                clock,
                logger);
        }

        private void OnStoreInitialized(InitializationResult result)
        {
            if (result.IsSuccess)
            {
                UpdateStatus($"Store ready! {result.AvailableProducts.Count} products available");

                // Update UI with prices
                foreach (var product in result.AvailableProducts)
                {
                    Debug.Log($"Product: {product.ProductId} - {product.PriceString}");
                }

                // Enable purchase buttons
                SetButtonsEnabled(true);
            }
            else
            {
                UpdateStatus($"Store init failed: {result.Error}");
                SetButtonsEnabled(false);
            }
        }

        private void OnPurchaseCompleted(PurchaseResult result)
        {
            if (result.IsSuccess)
            {
                UpdateStatus($"Purchase complete: {result.ProductId}");

                // Handle granted items
                foreach (var itemId in result.GrantedItemIds)
                {
                    Debug.Log($"Granted: {itemId}");
                    // Enable features, add currency, etc.
                }

                // Handle subscription tier
                if (!string.IsNullOrEmpty(result.TierKey))
                {
                    Debug.Log($"Subscription tier: {result.TierKey}");
                    EnableSubscriptionFeatures(result.TierKey);
                }
            }
            else
            {
                UpdateStatus($"Purchase failed: {result.Error}");
            }
        }

        private void OnEntitlementsUpdated(EntitlementsResponse entitlements)
        {
            Debug.Log($"Entitlements updated: {entitlements.Entitlements.Count} items");

            // Check for specific entitlements
            foreach (var entitlement in entitlements.Entitlements)
            {
                Debug.Log($"Entitlement: {entitlement.ItemId} x{entitlement.Quantity}");
            }

            // Check subscription status
            if (entitlements.ActiveSubscription != null)
            {
                var sub = entitlements.ActiveSubscription;
                Debug.Log($"Active subscription: {sub.TierKey}, status: {sub.Status}");
                EnableSubscriptionFeatures(sub.TierKey);
            }
        }

        public async void OnBuyCoinsClicked()
        {
            if (_purchaseService == null) return;

            SetButtonsEnabled(false);
            UpdateStatus("Purchasing coins...");

            var result = await _purchaseService.BuyAsync(ExampleProductCatalog.CoinsPack100);

            SetButtonsEnabled(true);
        }

        public async void OnBuyPremiumClicked()
        {
            if (_purchaseService == null) return;

            // Check current subscription
            var currentSub = _purchaseService.ActiveSubscription;
            if (currentSub != null && currentSub.TierKey == ExampleProductCatalog.TierPremium)
            {
                UpdateStatus("Already subscribed to Premium!");
                return;
            }

            SetButtonsEnabled(false);
            UpdateStatus("Purchasing Premium subscription...");

            var result = await _purchaseService.BuyAsync(ExampleProductCatalog.PremiumMonthly);

            SetButtonsEnabled(true);
        }

        public async void OnBuyProClicked()
        {
            if (_purchaseService == null) return;

            SetButtonsEnabled(false);
            UpdateStatus("Purchasing Pro subscription...");

            var result = await _purchaseService.BuyAsync(ExampleProductCatalog.ProMonthly);

            SetButtonsEnabled(true);
        }

        public async void OnRestoreClicked()
        {
            if (_purchaseService == null) return;

            SetButtonsEnabled(false);
            UpdateStatus("Restoring purchases...");

            var result = await _purchaseService.RestoreAsync();

            if (result.IsSuccess)
            {
                UpdateStatus($"Restored {result.RestoredPurchases.Count} purchases");
            }
            else
            {
                UpdateStatus($"Restore failed: {result.Error}");
            }

            SetButtonsEnabled(true);
        }

        private void EnableSubscriptionFeatures(string tierKey)
        {
            // Enable tier-specific features
            switch (tierKey)
            {
                case ExampleProductCatalog.TierPremium:
                    // Enable premium features
                    Debug.Log("Premium features enabled!");
                    break;

                case ExampleProductCatalog.TierPro:
                    // Enable pro features (includes premium)
                    Debug.Log("Pro features enabled!");
                    break;
            }
        }

        private void UpdateStatus(string message)
        {
            Debug.Log($"[PurchaseExample] {message}");
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            if (buyCoinsButton != null) buyCoinsButton.interactable = enabled;
            if (buyPremiumButton != null) buyPremiumButton.interactable = enabled;
            if (buyProButton != null) buyProButton.interactable = enabled;
            if (restoreButton != null) restoreButton.interactable = enabled;
        }

        // Mock backend client for testing without actual server
        private static IMonetizationBackendClient CreateMockBackendClient()
        {
            return new MockMonetizationBackendClient();
        }
    }

    /// <summary>
    /// Mock backend client for local editor/development testing.
    /// Release players fail closed even if this sample is imported accidentally.
    /// </summary>
    internal class MockMonetizationBackendClient : IMonetizationBackendClient
    {
        public Task<CloudResult<VerifyPurchaseResponse>> VerifyPurchaseAsync(
            VerifyPurchaseRequest request,
            System.Threading.CancellationToken ct = default)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            return Task.FromResult(CloudResult<VerifyPurchaseResponse>.Failure(
                CreateReleaseDisabledError()));
#else
            // Simulate successful verification
            var response = new VerifyPurchaseResponse
            {
                Success = true,
                TransactionKey = request.TransactionId,
                GrantedItemIds = new System.Collections.Generic.List<string> { $"economy_{request.ProductId}" }
            };

            if (request.ProductType == "Subscription")
            {
                response.Subscription = new SubscriptionDto
                {
                    ProductId = request.ProductId,
                    TierKey = request.TierKey ?? "unknown",
                    Status = SubscriptionStatus.Active,
                    AutoRenew = true,
                    PeriodStartUtc = System.DateTime.UtcNow,
                    PeriodEndUtc = System.DateTime.UtcNow.AddMonths(1)
                };
            }

            return Task.FromResult(CloudResult<VerifyPurchaseResponse>.Success(response));
#endif
        }

        public Task<CloudResult<GetEntitlementsResponse>> GetEntitlementsAsync(
            GetEntitlementsRequest request,
            System.Threading.CancellationToken ct = default)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            return Task.FromResult(CloudResult<GetEntitlementsResponse>.Failure(
                CreateReleaseDisabledError()));
#else
            var response = new GetEntitlementsResponse
            {
                Entitlements = new System.Collections.Generic.List<EntitlementDto>(),
                ServerTimestampUtc = System.DateTime.UtcNow
            };

            return Task.FromResult(CloudResult<GetEntitlementsResponse>.Success(response));
#endif
        }

        private static BackendError CreateReleaseDisabledError()
        {
            return new BackendError(
                ErrorCodes.Forbidden,
                "The sample monetization backend is disabled in release players. Configure a verified cloud backend.");
        }
    }
}
