using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Services;

/// <summary>
/// Request to verify a purchase.
/// </summary>
public sealed class VerifyPurchaseServiceRequest
{
    public string PlayerId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string ReceiptPayload { get; set; } = string.Empty;
    /// <summary>
    /// Retained for wire compatibility only. Untrusted client metadata is intentionally ignored;
    /// grant metadata comes exclusively from ProductConfig.GrantMetadata.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response from purchase verification.
/// </summary>
public sealed class VerifyPurchaseServiceResponse
{
    public bool Success { get; set; }
    public string? TransactionKey { get; set; }
    public List<string> GrantedItemIds { get; set; } = new();
    public SubscriptionServiceDto? Subscription { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Retryable { get; set; }
    public bool IsDuplicate { get; set; }

    public static VerifyPurchaseServiceResponse Ok(
        string transactionKey,
        List<string> grantedItemIds,
        SubscriptionServiceDto? subscription = null,
        bool isDuplicate = false) => new()
    {
        Success = true,
        TransactionKey = transactionKey,
        GrantedItemIds = grantedItemIds,
        Subscription = subscription,
        IsDuplicate = isDuplicate
    };

    public static VerifyPurchaseServiceResponse Fail(
        string errorCode,
        string errorMessage,
        bool retryable = false) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        Retryable = retryable
    };
}

/// <summary>
/// Subscription info for service response.
/// </summary>
public sealed class SubscriptionServiceDto
{
    public string ProductId { get; set; } = string.Empty;
    public string TierKey { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime OriginalPurchaseDateUtc { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? GrantedItemId { get; set; }
    public int? GracePeriodDaysRemaining { get; set; }
}

/// <summary>
/// Main service for purchase verification and entitlement granting.
/// Orchestrates: Store verification -> Idempotency check -> Entitlement grant -> Record storage
/// </summary>
public sealed class PurchaseVerificationService
{
    private readonly IStoreVerifier _appleVerifier;
    private readonly IStoreVerifier _googleVerifier;
    private readonly IPurchaseRepository _repository;
    private readonly IEntitlementGranter _granter;
    private readonly ProductAllowlistConfig _productConfig;
    private readonly bool _enforceProductionSandboxPolicy;
    private readonly ILogger<PurchaseVerificationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _processingLeaseDuration;
    private readonly TimeSpan _outboundOperationTimeout;
    private readonly TimeSpan _baseRetryDelay;

    private static readonly TimeSpan DefaultProcessingLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultOutboundOperationTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);
    // PlayFab Economy v2 retains IdempotencyId records for 14 days. Stop a full day earlier so
    // clock skew and queue delay cannot turn an automatic retry into a duplicate economy grant.
    private static readonly TimeSpan AutomaticGrantRetryWindow = TimeSpan.FromDays(13);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PurchaseVerificationService(
        IStoreVerifier appleVerifier,
        IStoreVerifier googleVerifier,
        IPurchaseRepository repository,
        IEntitlementGranter granter,
        ProductAllowlistConfig productConfig,
        ILogger<PurchaseVerificationService> logger,
        bool enforceProductionSandboxPolicy,
        TimeProvider? timeProvider = null,
        TimeSpan? processingLeaseDuration = null,
        TimeSpan? baseRetryDelay = null,
        TimeSpan? outboundOperationTimeout = null)
    {
        _appleVerifier = appleVerifier ?? throw new ArgumentNullException(nameof(appleVerifier));
        _googleVerifier = googleVerifier ?? throw new ArgumentNullException(nameof(googleVerifier));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _granter = granter ?? throw new ArgumentNullException(nameof(granter));
        _productConfig = productConfig ?? throw new ArgumentNullException(nameof(productConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enforceProductionSandboxPolicy = enforceProductionSandboxPolicy;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _processingLeaseDuration = processingLeaseDuration ?? DefaultProcessingLeaseDuration;
        _outboundOperationTimeout = outboundOperationTimeout ?? DefaultOutboundOperationTimeout;
        _baseRetryDelay = baseRetryDelay ?? DefaultBaseRetryDelay;

        if (_processingLeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processingLeaseDuration),
                "The processing lease duration must be positive.");
        }

        if (_baseRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseRetryDelay),
                "The retry delay must be positive.");
        }

        if (_outboundOperationTimeout <= TimeSpan.Zero ||
            _outboundOperationTimeout >= _processingLeaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outboundOperationTimeout),
                "The outbound timeout must be positive and shorter than the processing lease.");
        }
    }

    public async Task<VerifyPurchaseServiceResponse> VerifyAndGrantAsync(
        VerifyPurchaseServiceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Platform, Platform.Apple, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Platform, Platform.Google, StringComparison.OrdinalIgnoreCase))
        {
            return VerifyPurchaseServiceResponse.Fail(
                "INVALID_PLATFORM",
                "Platform must be either 'apple' or 'google'.");
        }

        if (string.IsNullOrWhiteSpace(request.PlayerId) ||
            string.IsNullOrWhiteSpace(request.ProductId))
        {
            return VerifyPurchaseServiceResponse.Fail(
                "INVALID_REQUEST",
                "PlayerId and ProductId are required.");
        }

        var normalizedPlatform = request.Platform.ToLowerInvariant();
        string transactionKey;
        if (normalizedPlatform == Platform.Google)
        {
            if (string.IsNullOrWhiteSpace(request.ReceiptPayload))
            {
                return VerifyPurchaseServiceResponse.Fail(
                    "INVALID_RECEIPT",
                    "A Google Play purchase token is required.");
            }

            transactionKey = PurchaseRecord.CreateGoogleTransactionKey(request.ReceiptPayload);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.TransactionId))
            {
                return VerifyPurchaseServiceResponse.Fail(
                    "INVALID_TRANSACTION_ID",
                    "An Apple transaction ID is required.");
            }

            // AppleStoreVerifier cryptographically verifies that the returned transaction ID is
            // exactly this value before a grant can occur.
            transactionKey = PurchaseRecord.CreateTransactionKey(
                normalizedPlatform,
                request.TransactionId);
        }

        _logger.LogInformation(
            "Verifying purchase: Platform={Platform}, ProductId={ProductId}, TransactionFingerprint={TransactionFingerprint}",
            request.Platform,
            request.ProductId,
            SensitiveLogValue.Fingerprint(transactionKey));

        // Existing claims are inspected before the mutable allowlist. This lets terminal rows
        // replay and in-flight rows resume from their immutable grant snapshot after a catalog
        // deployment removes or changes the product.
        var existing = await _repository.GetPurchaseAsync(transactionKey, ct);
        if (existing != null && !MatchesRequestIdentity(existing, request))
        {
            return IdempotencyConflictResponse();
        }

        if (existing is { Status: PurchaseStatus.Granted or PurchaseStatus.Failed or PurchaseStatus.Refunded })
        {
            return HandleExistingRecord(existing, UtcNow);
        }

        ProductConfig? productConfig = null;
        if (existing?.HasGrantPayloadSnapshot != true)
        {
            productConfig = _productConfig.GetProduct(request.ProductId);
        }

        if (existing == null && productConfig == null)
        {
            _logger.LogWarning("Product not allowed: {ProductId}", request.ProductId);
            return VerifyPurchaseServiceResponse.Fail("PRODUCT_NOT_ALLOWED",
                $"Product {request.ProductId} is not in the allowlist");
        }

        // Atomically create or reclaim the purchase-processing lease.
        var now = UtcNow;
        var candidate = existing?.Copy() ?? new PurchaseRecord
        {
            TransactionKey = transactionKey,
            Platform = normalizedPlatform,
            ProductId = request.ProductId,
            ProductType = productConfig!.Type,
            PlayerId = request.PlayerId,
            Status = PurchaseStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            // A Google order ID is not trusted until it comes back from the verifier.
            StoreTransactionId = normalizedPlatform == Platform.Apple
                ? request.TransactionId
                : string.Empty
        };

        if (existing == null)
        {
            ApplyGrantPayloadSnapshot(candidate, productConfig!);
        }

        var leaseId = Guid.NewGuid().ToString("N");
        var claim = await _repository.TryClaimPurchaseAsync(
            candidate,
            leaseId,
            now,
            _processingLeaseDuration,
            ct);
        var record = claim.Record;

        if (!MatchesRequestIdentity(record, request))
        {
            return IdempotencyConflictResponse();
        }

        if (!claim.Acquired)
        {
            return HandleExistingRecord(record, now);
        }

        var ambiguousLegacyGrantState =
            record.Status == PurchaseStatus.Verified &&
            !record.HasGrantAttemptTracking &&
            !record.FirstGrantAttemptAtUtc.HasValue;

        if (!record.HasGrantPayloadSnapshot)
        {
            if (productConfig == null || productConfig.Type != record.ProductType)
            {
                const string errorCode = "PURCHASE_CONFIGURATION_MISSING";
                const string errorMessage =
                    "The original purchase configuration is unavailable; restore it to resume this claim.";
                ScheduleRetry(record, record.Status, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable: true);
            }

            ApplyGrantPayloadSnapshot(record, productConfig);
            if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }
        }

        if (!IsGrantPayloadSnapshotSafe(record))
        {
            const string errorCode = "PURCHASE_GRANT_SNAPSHOT_INVALID";
            const string errorMessage =
                "The durable server grant payload is invalid; no entitlement operation was attempted.";
            CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
            if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }

            return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
        }

        VerificationResult verificationResult;
        if (record.Status == PurchaseStatus.Verified &&
            record.HasStoreVerificationSnapshot &&
            record.ProductType != ProductType.Subscription)
        {
            verificationResult = RestoreVerificationSnapshot(record);
        }
        else
        {
            // Legacy Verified rows did not persist enough store data to resume safely. Re-verify
            // under the acquired lease rather than granting from incomplete state.
            record.Status = PurchaseStatus.Pending;

            var verifier = GetVerifier(normalizedPlatform);
            var verifyRequest = new VerifyRequest
            {
                ProductId = record.ProductId,
                TransactionId = normalizedPlatform == Platform.Apple
                    ? request.TransactionId
                    : string.Empty,
                ReceiptPayload = request.ReceiptPayload,
                ExpectedObfuscatedAccountId = normalizedPlatform == Platform.Google
                    ? CreateGoogleAccountBinding(record.PlayerId)
                    : null,
                ExpectedAppleAppAccountToken = normalizedPlatform == Platform.Apple
                    ? StoreAccountIdentity.CreateAppleAppAccountToken(record.PlayerId).ToString("D")
                    : null,
                ExpectedProductType = record.ProductType,
                IsSubscription = record.ProductType == ProductType.Subscription
            };

            try
            {
                verificationResult = record.ProductType == ProductType.Subscription
                    ? await ExecuteWithDeadlineAsync(
                        token => verifier.VerifySubscriptionAsync(verifyRequest, token),
                        ct)
                    : await ExecuteWithDeadlineAsync(
                        token => verifier.VerifyOneTimePurchaseAsync(verifyRequest, token),
                        ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                const string errorCode = "STORE_TIMEOUT";
                const string errorMessage = "Store verification exceeded its operation deadline.";
                ScheduleRetry(record, PurchaseStatus.Pending, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable: true);
            }

            if (!verificationResult.IsValid)
            {
                var errorCode = verificationResult.ErrorCode ?? "VERIFICATION_FAILED";
                var errorMessage = verificationResult.ErrorMessage ?? "Store verification failed";
                _logger.LogWarning(
                    "Verification failed: {ErrorCode} - {ErrorMessage}",
                    errorCode,
                    errorMessage);

                if (verificationResult.IsRetryable)
                {
                    ScheduleRetry(record, PurchaseStatus.Pending, errorCode, errorMessage, UtcNow);
                    if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                    {
                        return LeaseLostResponse();
                    }

                    return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable: true);
                }

                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            if (!string.IsNullOrWhiteSpace(verificationResult.ProductId) &&
                !string.Equals(
                    verificationResult.ProductId,
                    record.ProductId,
                    StringComparison.Ordinal))
            {
                const string errorCode = "PRODUCT_MISMATCH";
                const string errorMessage = "The verified store product does not match the request.";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            if (normalizedPlatform == Platform.Apple &&
                !string.Equals(
                    verificationResult.TransactionId,
                    request.TransactionId,
                    StringComparison.Ordinal))
            {
                const string errorCode = "TRANSACTION_MISMATCH";
                const string errorMessage =
                    "The verified Apple transaction ID does not match the requested transaction.";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            if (record.ProductType == ProductType.Subscription &&
                (!verificationResult.PurchaseDateUtc.HasValue ||
                 !verificationResult.ExpirationDateUtc.HasValue ||
                 verificationResult.ExpirationDateUtc.Value <= verificationResult.PurchaseDateUtc.Value ||
                 (normalizedPlatform == Platform.Apple &&
                  string.IsNullOrWhiteSpace(verificationResult.OriginalTransactionId))))
            {
                const string errorCode = "SUBSCRIPTION_SNAPSHOT_INCOMPLETE";
                const string errorMessage =
                    "Store verification did not return a complete authoritative subscription period.";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            if (verificationResult.IsSandbox &&
                _enforceProductionSandboxPolicy &&
                !_productConfig.AllowSandboxInProduction)
            {
                const string errorCode = "SANDBOX_NOT_ALLOWED";
                const string errorMessage = "Sandbox purchases not allowed in production";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            var verificationPersistedAtUtc = UtcNow;
            ApplyVerificationSnapshot(record, verificationResult, verificationPersistedAtUtc);
            if (!ambiguousLegacyGrantState)
            {
                // New rows explicitly distinguish "verified but no provider call yet" from
                // legacy rows whose outbound grant state is unknowable.
                record.HasGrantAttemptTracking = true;
            }
            if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }
        }

        if (ambiguousLegacyGrantState)
        {
            return await RequireGrantReconciliationAsync(record, leaseId, UtcNow, ct);
        }

        string? subscriptionKey = null;
        var skipEntitlementGrant = false;
        if (record.ProductType == ProductType.Subscription)
        {
            subscriptionKey = CreateSubscriptionKey(request, record, verificationResult);
            if (!await TryRenewLeaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }

            (SubscriptionRecord? ByKey, SubscriptionRecord? ActiveForPlayer) subscriptionState;
            try
            {
                subscriptionState = await ExecuteWithDeadlineAsync(
                    token => GetSubscriptionGrantStateAsync(
                        subscriptionKey,
                        record.PlayerId,
                        token),
                    ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                const string errorCode = "SUBSCRIPTION_STATE_TIMEOUT";
                const string errorMessage =
                    "Subscription state lookup exceeded its operation deadline.";
                ScheduleRetry(record, PurchaseStatus.Verified, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable: true);
            }

            var activeSubscription = subscriptionState.ActiveForPlayer;
            if (activeSubscription != null &&
                !string.Equals(
                    activeSubscription.SubscriptionKey,
                    subscriptionKey,
                    StringComparison.Ordinal))
            {
                const string errorCode = "SUBSCRIPTION_CHANGE_NOT_SUPPORTED";
                const string errorMessage =
                    "A different active subscription already exists; use an explicit subscription-change flow.";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            var keyedSubscription = subscriptionState.ByKey;
            if (keyedSubscription != null &&
                (!string.Equals(keyedSubscription.PlayerId, record.PlayerId, StringComparison.Ordinal) ||
                 !string.Equals(keyedSubscription.Platform, record.Platform, StringComparison.OrdinalIgnoreCase)))
            {
                const string errorCode = "SUBSCRIPTION_IDENTITY_CONFLICT";
                const string errorMessage =
                    "The verified store subscription is already bound to another identity.";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            if (keyedSubscription != null &&
                !string.Equals(keyedSubscription.ProductId, record.ProductId, StringComparison.Ordinal))
            {
                const string errorCode = "SUBSCRIPTION_CHANGE_NOT_SUPPORTED";
                const string errorMessage =
                    "The store subscription product changed; use an explicit subscription-change flow.";
                CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
            }

            if (activeSubscription != null)
            {
                if (!MatchesActiveSubscriptionGrant(activeSubscription, record))
                {
                    const string errorCode = "SUBSCRIPTION_GRANT_RECONCILIATION_REQUIRED";
                    const string errorMessage =
                        "The durable active subscription grant differs from the verified purchase snapshot.";
                    CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                    if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                    {
                        return LeaseLostResponse();
                    }

                    return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
                }

                // Apple renewals have a new transaction ID but retain the original subscription
                // key. The durable active grant proves that Economy Add must not run again.
                skipEntitlementGrant = true;
            }
        }

        GrantResult grantResult;
        if (skipEntitlementGrant)
        {
            grantResult = GrantResult.Success(
                new List<string>(record.GrantEconomyItemIds),
                wasDuplicate: true);
        }
        else
        {
            var grantWindowFailure = await PrepareGrantAttemptAsync(record, leaseId, ct);
            if (grantWindowFailure != null)
            {
                return grantWindowFailure;
            }

            // Assert and renew ownership immediately before the external grant. The provider
            // deadline is shorter than this lease, so a healthy worker cannot outlive its claim.
            if (!await TryRenewLeaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }

            var grantRequest = new GrantRequest
            {
                PlayerId = record.PlayerId,
                ItemIds = new List<string>(record.GrantEconomyItemIds),
                Quantities = record.GrantQuantities == null
                    ? null
                    : new List<int>(record.GrantQuantities),
                // Subscriptions use their original provider identity, so concurrent activation
                // and renewal transactions converge on one Economy v2 idempotency key.
                IdempotencyKey = CreateGrantIdempotencyKey(subscriptionKey ?? transactionKey),
                Metadata = record.GrantMetadata == null
                    ? null
                    : new Dictionary<string, string>(record.GrantMetadata, StringComparer.Ordinal)
            };

            try
            {
                grantResult = await ExecuteWithDeadlineAsync(
                    token => _granter.GrantItemsAsync(grantRequest, token),
                    ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                const string errorCode = "GRANT_TIMEOUT";
                const string errorMessage = "Entitlement grant exceeded its operation deadline.";
                ScheduleRetry(record, PurchaseStatus.Verified, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable: true);
            }

            if (!grantResult.IsSuccess)
            {
                var errorCode = grantResult.ErrorCode ?? "GRANT_FAILED";
                var errorMessage = grantResult.ErrorMessage ?? "Failed to grant entitlements";
                _logger.LogWarning(
                    "Grant failed: {ErrorCode} - {ErrorMessage}",
                    errorCode,
                    errorMessage);

                var retryable = IsRetryableGrantFailure(errorCode);
                if (retryable)
                {
                    ScheduleRetry(record, PurchaseStatus.Verified, errorCode, errorMessage, UtcNow);
                }
                else
                {
                    CompletePermanentFailure(record, errorCode, errorMessage, UtcNow);
                }

                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable);
            }
        }

        SubscriptionServiceDto? subscriptionDto = null;
        if (record.ProductType == ProductType.Subscription)
        {
            if (!await TryRenewLeaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }

            try
            {
                subscriptionDto = await ExecuteWithDeadlineAsync(
                    token => HandleSubscriptionAsync(
                        record,
                        verificationResult,
                        subscriptionKey!,
                        token),
                    ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                const string errorCode = "SUBSCRIPTION_PROJECTION_TIMEOUT";
                const string errorMessage =
                    "Subscription projection exceeded its operation deadline.";
                ScheduleRetry(record, PurchaseStatus.Verified, errorCode, errorMessage, UtcNow);
                if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
                {
                    return LeaseLostResponse();
                }

                return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage, retryable: true);
            }
        }

        record.Status = PurchaseStatus.Granted;
        record.GrantedEconomyItemIds = grantResult.GrantedItemIds;
        record.QuantityGranted = record.GrantQuantities is { Count: > 0 }
            ? record.GrantQuantities[0]
            : 1;
        record.UpdatedAtUtc = UtcNow;
        record.IsRetryable = false;
        record.NextRetryAtUtc = null;
        record.ErrorCode = null;
        record.ErrorMessage = null;
        record.ProcessingLeaseId = null;
        record.ProcessingLeaseExpiresAtUtc = null;

        var response = VerifyPurchaseServiceResponse.Ok(
            transactionKey,
            grantResult.GrantedItemIds,
            subscriptionDto);

        record.CachedResponseJson = JsonSerializer.Serialize(response, JsonOptions);
        if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
        {
            return LeaseLostResponse();
        }

        _logger.LogInformation(
            "Purchase completed: TransactionFingerprint={TransactionFingerprint}, GrantedItems={Items}",
            SensitiveLogValue.Fingerprint(transactionKey), string.Join(",", grantResult.GrantedItemIds));

        return response;
    }

    private VerifyPurchaseServiceResponse HandleExistingRecord(
        PurchaseRecord record,
        DateTime nowUtc)
    {
        _logger.LogInformation("Found existing record: Status={Status}, TransactionFingerprint={TransactionFingerprint}",
            record.Status, SensitiveLogValue.Fingerprint(record.TransactionKey));

        switch (record.Status)
        {
            case PurchaseStatus.Granted:
                // Return cached response
                if (!string.IsNullOrEmpty(record.CachedResponseJson))
                {
                    try
                    {
                        var cached = JsonSerializer.Deserialize<VerifyPurchaseServiceResponse>(
                            record.CachedResponseJson, JsonOptions);
                        if (cached?.Success == true)
                        {
                            cached.IsDuplicate = true;
                            return cached;
                        }
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning(
                            "Ignoring malformed cached purchase response: TransactionFingerprint={TransactionFingerprint}",
                            SensitiveLogValue.Fingerprint(record.TransactionKey));
                    }
                }

                return VerifyPurchaseServiceResponse.Ok(
                    record.TransactionKey,
                    record.GrantedEconomyItemIds,
                    isDuplicate: true);

            case PurchaseStatus.Pending:
            case PurchaseStatus.Verified:
                if (record.IsRetryable &&
                    record.NextRetryAtUtc.HasValue &&
                    record.NextRetryAtUtc.Value > nowUtc)
                {
                    return VerifyPurchaseServiceResponse.Fail(
                        record.ErrorCode ?? "RETRY_SCHEDULED",
                        record.ErrorMessage ?? "Purchase retry is scheduled",
                        retryable: true);
                }

                return VerifyPurchaseServiceResponse.Fail(
                    "IN_PROGRESS",
                    "Purchase is being processed",
                    retryable: true);

            case PurchaseStatus.Failed:
                return VerifyPurchaseServiceResponse.Fail(
                    record.ErrorCode ?? "PREVIOUS_FAILURE",
                    record.ErrorMessage ?? "Previous verification failed");

            case PurchaseStatus.Refunded:
                return VerifyPurchaseServiceResponse.Fail("REFUNDED",
                    "This purchase has been refunded");

            default:
                return VerifyPurchaseServiceResponse.Fail("UNKNOWN_STATUS",
                    $"Unknown purchase status: {record.Status}");
        }
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private static bool MatchesRequestIdentity(
        PurchaseRecord record,
        VerifyPurchaseServiceRequest request) =>
        string.Equals(record.PlayerId, request.PlayerId, StringComparison.Ordinal) &&
        string.Equals(record.Platform, request.Platform, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(record.ProductId, request.ProductId, StringComparison.Ordinal);

    private static VerifyPurchaseServiceResponse IdempotencyConflictResponse() =>
        VerifyPurchaseServiceResponse.Fail(
            "IDEMPOTENCY_CONFLICT",
            "The store purchase is already associated with a different purchase identity.");

    private static VerifyPurchaseServiceResponse LeaseLostResponse() =>
        VerifyPurchaseServiceResponse.Fail(
            "IN_PROGRESS",
            "Another worker owns this purchase operation",
            retryable: true);

    private static string CreateGrantIdempotencyKey(string transactionKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transactionKey)));

    private static string CreateGoogleAccountBinding(string playerId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"serhat-forge/google-account/v1:{playerId}")));

    private static string CreateSubscriptionKey(
        VerifyPurchaseServiceRequest request,
        PurchaseRecord purchase,
        VerificationResult verificationResult)
    {
        if (string.Equals(purchase.Platform, Platform.Apple, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(verificationResult.OriginalTransactionId))
            {
                throw new InvalidOperationException(
                    "A verified Apple subscription must include its original transaction ID.");
            }

            return SubscriptionRecord.CreateAppleKey(verificationResult.OriginalTransactionId);
        }

        return SubscriptionRecord.CreateGoogleKey(request.ReceiptPayload);
    }

    private async Task<(SubscriptionRecord? ByKey, SubscriptionRecord? ActiveForPlayer)>
        GetSubscriptionGrantStateAsync(
            string subscriptionKey,
            string playerId,
            CancellationToken ct)
    {
        var byKeyTask = _repository.GetSubscriptionAsync(subscriptionKey, ct);
        var activeTask = _repository.GetActiveSubscriptionAsync(playerId, ct);
        await Task.WhenAll(byKeyTask, activeTask);
        return (await byKeyTask, await activeTask);
    }

    private static bool MatchesActiveSubscriptionGrant(
        SubscriptionRecord subscription,
        PurchaseRecord purchase)
    {
        if (!string.Equals(subscription.PlayerId, purchase.PlayerId, StringComparison.Ordinal) ||
            !string.Equals(subscription.Platform, purchase.Platform, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(subscription.ProductId, purchase.ProductId, StringComparison.Ordinal) ||
            !string.Equals(subscription.TierKey, purchase.TierKey, StringComparison.Ordinal) ||
            subscription.TierPrecedence != purchase.TierPrecedence ||
            subscription.ActiveEconomyItemIds.Count != purchase.GrantEconomyItemIds.Count)
        {
            return false;
        }

        for (var index = 0; index < subscription.ActiveEconomyItemIds.Count; index++)
        {
            if (!string.Equals(
                    subscription.ActiveEconomyItemIds[index],
                    purchase.GrantEconomyItemIds[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> TryRenewLeaseAsync(
        PurchaseRecord record,
        string leaseId,
        CancellationToken ct)
    {
        var renewedAt = UtcNow;
        var renewed = await _repository.TryRenewPurchaseLeaseAsync(
            record.TransactionKey,
            leaseId,
            renewedAt,
            _processingLeaseDuration,
            ct);
        if (!renewed)
        {
            return false;
        }

        record.ProcessingLeaseId = leaseId;
        record.ProcessingLeaseExpiresAtUtc = renewedAt.Add(_processingLeaseDuration);
        record.UpdatedAtUtc = renewedAt;
        return true;
    }

    private async Task<T> ExecuteWithDeadlineAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_outboundOperationTimeout);
        return await operation(deadline.Token);
    }

    private async Task<VerifyPurchaseServiceResponse?> PrepareGrantAttemptAsync(
        PurchaseRecord record,
        string leaseId,
        CancellationToken ct)
    {
        var now = UtcNow;
        if (!record.FirstGrantAttemptAtUtc.HasValue)
        {
            // Rows written before FirstGrantAttemptAtUtc existed are ambiguous: a provider grant
            // may have committed before the worker crashed. Never manufacture a fresh 13-day
            // window for such a row, because the provider may already have forgotten its key.
            if (!record.HasGrantAttemptTracking)
            {
                return await RequireGrantReconciliationAsync(record, leaseId, now, ct);
            }

            record.FirstGrantAttemptAtUtc = now;
            record.UpdatedAtUtc = now;
            if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
            {
                return LeaseLostResponse();
            }

            return null;
        }

        var firstAttemptAtUtc = record.FirstGrantAttemptAtUtc.Value;
        if (now >= firstAttemptAtUtc &&
            now - firstAttemptAtUtc >= AutomaticGrantRetryWindow)
        {
            return await RequireGrantReconciliationAsync(record, leaseId, now, ct);
        }

        return null;
    }

    private async Task<VerifyPurchaseServiceResponse> RequireGrantReconciliationAsync(
        PurchaseRecord record,
        string leaseId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        const string errorCode = "GRANT_RECONCILIATION_REQUIRED";
        const string errorMessage =
            "Automatic entitlement retry is no longer safe; reconcile the provider inventory before continuing.";
        CompletePermanentFailure(record, errorCode, errorMessage, nowUtc);
        if (!await _repository.TryUpdatePurchaseAsync(record, leaseId, ct))
        {
            return LeaseLostResponse();
        }

        return VerifyPurchaseServiceResponse.Fail(errorCode, errorMessage);
    }

    private static void ApplyGrantPayloadSnapshot(
        PurchaseRecord record,
        ProductConfig productConfig)
    {
        record.GrantEconomyItemIds = new List<string>(productConfig.EconomyItemIds);
        record.GrantQuantities = null;
        if (productConfig.Type == ProductType.Consumable)
        {
            record.GrantQuantities = new List<int>(record.GrantEconomyItemIds.Count);
            for (var index = 0; index < record.GrantEconomyItemIds.Count; index++)
            {
                record.GrantQuantities.Add(productConfig.Quantity);
            }
        }

        record.GrantMetadata = CreateServerMetadataSnapshot(productConfig.GrantMetadata);
        record.TierKey = productConfig.TierKey;
        record.TierPrecedence = productConfig.TierPrecedence;
        record.HasGrantPayloadSnapshot = true;
    }

    private static Dictionary<string, string>? CreateServerMetadataSnapshot(
        Dictionary<string, string>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return null;
        }

        var keys = new List<string>(metadata.Keys);
        keys.Sort(StringComparer.Ordinal);
        var snapshot = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            snapshot[key] = metadata[key] ?? string.Empty;
        }

        return snapshot;
    }

    private static bool IsGrantPayloadSnapshotSafe(PurchaseRecord record)
    {
        if (!record.HasGrantPayloadSnapshot ||
            string.IsNullOrWhiteSpace(record.ProductId) ||
            record.ProductId.Length > ProductGrantLimits.MaxProductIdLength ||
            !Enum.IsDefined(typeof(ProductType), record.ProductType) ||
            record.GrantEconomyItemIds.Count is < 1 or > ProductGrantLimits.MaxEconomyItemsPerProduct)
        {
            return false;
        }

        var uniqueItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemId in record.GrantEconomyItemIds)
        {
            if (string.IsNullOrWhiteSpace(itemId) ||
                itemId.Length > ProductGrantLimits.MaxEconomyItemIdLength ||
                !uniqueItems.Add(itemId))
            {
                return false;
            }
        }

        if (record.ProductType == ProductType.Consumable)
        {
            if (record.GrantQuantities == null ||
                record.GrantQuantities.Count != record.GrantEconomyItemIds.Count)
            {
                return false;
            }

            foreach (var quantity in record.GrantQuantities)
            {
                if (quantity is < 1 or > ProductGrantLimits.MaxConsumableQuantity)
                {
                    return false;
                }
            }
        }
        else if (record.GrantQuantities is { Count: > 0 })
        {
            return false;
        }

        if (record.ProductType == ProductType.Subscription)
        {
            if (string.IsNullOrWhiteSpace(record.TierKey) ||
                record.TierKey.Length > ProductGrantLimits.MaxTierKeyLength ||
                record.TierPrecedence is < 0 or > ProductGrantLimits.MaxTierPrecedence)
            {
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(record.TierKey) || record.TierPrecedence != 0)
        {
            return false;
        }

        if (record.GrantMetadata == null)
        {
            return true;
        }

        if (record.GrantMetadata.Count > ProductGrantLimits.MaxMetadataEntries)
        {
            return false;
        }

        var metadataUtf8Bytes = 0;
        foreach (var pair in record.GrantMetadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > ProductGrantLimits.MaxMetadataKeyLength ||
                pair.Value == null ||
                pair.Value.Length > ProductGrantLimits.MaxMetadataValueLength)
            {
                return false;
            }

            metadataUtf8Bytes += Encoding.UTF8.GetByteCount(pair.Key);
            metadataUtf8Bytes += Encoding.UTF8.GetByteCount(pair.Value);
            if (metadataUtf8Bytes > ProductGrantLimits.MaxMetadataUtf8Bytes)
            {
                return false;
            }
        }

        return true;
    }

    private void ScheduleRetry(
        PurchaseRecord record,
        PurchaseStatus resumeStatus,
        string errorCode,
        string errorMessage,
        DateTime nowUtc)
    {
        record.Status = resumeStatus;
        record.ErrorCode = errorCode;
        record.ErrorMessage = errorMessage;
        record.IsRetryable = true;
        record.NextRetryAtUtc = nowUtc.Add(CalculateRetryDelay(record.AttemptCount));
        record.UpdatedAtUtc = nowUtc;
        record.ProcessingLeaseId = null;
        record.ProcessingLeaseExpiresAtUtc = null;
    }

    private static void CompletePermanentFailure(
        PurchaseRecord record,
        string errorCode,
        string errorMessage,
        DateTime nowUtc)
    {
        record.Status = PurchaseStatus.Failed;
        record.ErrorCode = errorCode;
        record.ErrorMessage = errorMessage;
        record.IsRetryable = false;
        record.NextRetryAtUtc = null;
        record.UpdatedAtUtc = nowUtc;
        record.ProcessingLeaseId = null;
        record.ProcessingLeaseExpiresAtUtc = null;
    }

    private TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 10);
        var multiplier = 1L << exponent;
        var delayTicks = _baseRetryDelay.Ticks >= MaximumRetryDelay.Ticks / multiplier
            ? MaximumRetryDelay.Ticks
            : _baseRetryDelay.Ticks * multiplier;
        return TimeSpan.FromTicks(delayTicks);
    }

    private static void ApplyVerificationSnapshot(
        PurchaseRecord record,
        VerificationResult result,
        DateTime nowUtc)
    {
        record.Status = PurchaseStatus.Verified;
        record.HasStoreVerificationSnapshot = true;
        if (!string.IsNullOrWhiteSpace(result.TransactionId))
        {
            record.StoreTransactionId = result.TransactionId;
        }
        record.OriginalTransactionId = result.OriginalTransactionId;
        record.StorePurchaseDateUtc = result.PurchaseDateUtc;
        record.StoreExpirationDateUtc = result.ExpirationDateUtc;
        record.StoreSubscriptionStatus = result.SubscriptionStatus;
        record.StoreAutoRenew = result.AutoRenew;
        record.StoreIsSandbox = result.IsSandbox;
        record.StoreGracePeriodEndUtc = result.GracePeriodEndUtc;
        record.IsRetryable = false;
        record.NextRetryAtUtc = null;
        record.ErrorCode = null;
        record.ErrorMessage = null;
        record.UpdatedAtUtc = nowUtc;
    }

    private static VerificationResult RestoreVerificationSnapshot(PurchaseRecord record) =>
        VerificationResult.Valid() with
        {
            ProductId = record.ProductId,
            TransactionId = record.StoreTransactionId,
            OriginalTransactionId = record.OriginalTransactionId,
            PurchaseDateUtc = record.StorePurchaseDateUtc,
            ExpirationDateUtc = record.StoreExpirationDateUtc,
            IsSubscription = record.ProductType == ProductType.Subscription,
            SubscriptionStatus = record.StoreSubscriptionStatus,
            AutoRenew = record.StoreAutoRenew,
            IsSandbox = record.StoreIsSandbox,
            GracePeriodEndUtc = record.StoreGracePeriodEndUtc
        };

    private static bool IsRetryableGrantFailure(string errorCode)
    {
        // Unknown PlayFab/provider failures are retained for retry. Only deterministic request,
        // identity, and catalog/configuration errors are terminal; this avoids stranding a paid
        // order during an outage while keeping malformed operations out of an infinite loop.
        return errorCode.ToUpperInvariant() switch
        {
            "INVALID_REQUEST" => false,
            "INVALID_PARAMS" => false,
            "INVALID_PLAYER" => false,
            "INVALID_ITEM" => false,
            "ITEM_NOT_FOUND" => false,
            "INVALID_QUANTITY" => false,
            "UNAUTHORIZED" => false,
            "FORBIDDEN" => false,
            "CONFIGURATION_ERROR" => false,
            _ => true
        };
    }

    private async Task<SubscriptionServiceDto?> HandleSubscriptionAsync(
        PurchaseRecord purchase,
        VerificationResult verificationResult,
        string subscriptionKey,
        CancellationToken ct)
    {
        var purchaseDateUtc = verificationResult.PurchaseDateUtc ??
                              throw new InvalidOperationException(
                                  "A verified subscription must include a purchase date.");
        var expirationDateUtc = verificationResult.ExpirationDateUtc ??
                                throw new InvalidOperationException(
                                    "A verified subscription must include an expiration date.");

        var existing = await _repository.GetSubscriptionAsync(subscriptionKey, ct);
        var now = UtcNow;
        var snapshotOrderUtc = string.Equals(
            purchase.Platform,
            Platform.Apple,
            StringComparison.OrdinalIgnoreCase)
            ? purchaseDateUtc
            : now;

        if (existing != null)
        {
            if (!IsSubscriptionSnapshotNewer(
                    existing,
                    snapshotOrderUtc,
                    expirationDateUtc))
            {
                return ToSubscriptionDto(existing);
            }

            ApplyVerifiedSubscriptionSnapshot(
                existing,
                purchase,
                verificationResult,
                purchaseDateUtc,
                expirationDateUtc,
                snapshotOrderUtc,
                now);
            existing = await PersistSubscriptionSnapshotAsync(existing, ct);
            return ToSubscriptionDto(existing);
        }

        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = subscriptionKey,
            Platform = purchase.Platform,
            PlayerId = purchase.PlayerId,
            ProductId = purchase.ProductId,
            TierKey = purchase.TierKey!,
            TierPrecedence = purchase.TierPrecedence,
            Status = MapVerificationToSubscriptionStatus(verificationResult),
            AutoRenew = verificationResult.AutoRenew ?? false,
            LatestStoreOrderId = verificationResult.TransactionId,
            IsSandbox = verificationResult.IsSandbox,
            PeriodStartUtc = purchaseDateUtc,
            PeriodEndUtc = expirationDateUtc,
            OriginalPurchaseDateUtc = purchaseDateUtc,
            LastEventAtUtc = snapshotOrderUtc,
            GracePeriodEndUtc = verificationResult.GracePeriodEndUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        subscription.SetActiveEconomyItemIds(purchase.GrantEconomyItemIds);

        if (!await _repository.CreateSubscriptionAsync(subscription, ct))
        {
            // Concurrent Apple activation/renewal transactions share a subscription key. Merge
            // the verified snapshot into the durable winner instead of losing the renewal state.
            subscription = await _repository.GetSubscriptionAsync(subscriptionKey, ct) ??
                           throw new InvalidOperationException(
                               "Subscription creation conflicted without a durable record.");
            if (!IsSubscriptionSnapshotNewer(
                    subscription,
                    snapshotOrderUtc,
                    expirationDateUtc))
            {
                return ToSubscriptionDto(subscription);
            }

            ApplyVerifiedSubscriptionSnapshot(
                subscription,
                purchase,
                verificationResult,
                purchaseDateUtc,
                expirationDateUtc,
                snapshotOrderUtc,
                now);
            subscription = await PersistSubscriptionSnapshotAsync(subscription, ct);
        }

        return ToSubscriptionDto(subscription);
    }

    private void ApplyVerifiedSubscriptionSnapshot(
        SubscriptionRecord subscription,
        PurchaseRecord purchase,
        VerificationResult verificationResult,
        DateTime purchaseDateUtc,
        DateTime expirationDateUtc,
        DateTime snapshotOrderUtc,
        DateTime nowUtc)
    {
        subscription.SetActiveEconomyItemIds(purchase.GrantEconomyItemIds);
        subscription.Status = MapVerificationToSubscriptionStatus(verificationResult);
        subscription.AutoRenew = verificationResult.AutoRenew ?? false;
        subscription.LatestStoreOrderId = verificationResult.TransactionId;
        subscription.IsSandbox = verificationResult.IsSandbox;
        subscription.PeriodStartUtc = purchaseDateUtc;
        subscription.PeriodEndUtc = expirationDateUtc;
        subscription.GracePeriodEndUtc = verificationResult.GracePeriodEndUtc;
        subscription.LastEventAtUtc = snapshotOrderUtc;
        subscription.UpdatedAtUtc = nowUtc;
    }

    private async Task<SubscriptionRecord> PersistSubscriptionSnapshotAsync(
        SubscriptionRecord candidate,
        CancellationToken ct)
    {
        const int maximumAttempts = 3;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            if (await _repository.TryUpdateSubscriptionIfNotNewerAsync(candidate, ct))
            {
                // The conditional contract also reports success when a newer durable event
                // safely ignored this candidate. Re-read so the response never exposes stale
                // projection data.
                return await _repository.GetSubscriptionAsync(candidate.SubscriptionKey, ct) ??
                       throw new InvalidOperationException(
                           "The durable subscription disappeared after projection.");
            }

            var durable = await _repository.GetSubscriptionAsync(candidate.SubscriptionKey, ct) ??
                          throw new InvalidOperationException(
                              "The durable subscription disappeared during projection.");
            if (!IsSubscriptionSnapshotNewer(
                    durable,
                    candidate.LastEventAtUtc,
                    candidate.PeriodEndUtc))
            {
                return durable;
            }
        }

        throw new InvalidOperationException(
            "The subscription projection could not win a bounded conditional update.");
    }

    private static bool IsSubscriptionSnapshotNewer(
        SubscriptionRecord subscription,
        DateTime purchaseDateUtc,
        DateTime expirationDateUtc) =>
        purchaseDateUtc > subscription.LastEventAtUtc ||
        (purchaseDateUtc == subscription.LastEventAtUtc &&
         expirationDateUtc >= subscription.PeriodEndUtc);

    private SubscriptionServiceDto ToSubscriptionDto(SubscriptionRecord subscription) =>
        new()
        {
            ProductId = subscription.ProductId,
            TierKey = subscription.TierKey,
            Status = subscription.Status,
            AutoRenew = subscription.AutoRenew,
            PeriodStartUtc = subscription.PeriodStartUtc,
            PeriodEndUtc = subscription.PeriodEndUtc,
            OriginalPurchaseDateUtc = subscription.OriginalPurchaseDateUtc,
            Platform = subscription.Platform,
            GrantedItemId = subscription.ActiveEconomyItemId,
            GracePeriodDaysRemaining = CalculateGracePeriodDaysRemaining(subscription)
        };

    private int? CalculateGracePeriodDaysRemaining(SubscriptionRecord subscription)
    {
        if (!subscription.GracePeriodEndUtc.HasValue)
        {
            return null;
        }

        var remainingDays = (subscription.GracePeriodEndUtc.Value - UtcNow).TotalDays;
        return Math.Max(0, (int)Math.Ceiling(remainingDays));
    }

    private SubscriptionStatus MapVerificationToSubscriptionStatus(VerificationResult result)
    {
        if (result.SubscriptionStatus.HasValue)
        {
            return result.SubscriptionStatus.Value;
        }

        if (result.ExpirationDateUtc.HasValue && result.ExpirationDateUtc.Value < UtcNow)
        {
            return SubscriptionStatus.Expired;
        }

        return SubscriptionStatus.Active;
    }

    private IStoreVerifier GetVerifier(string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            Platform.Apple => _appleVerifier,
            Platform.Google => _googleVerifier,
            _ => throw new ArgumentException($"Unknown platform: {platform}")
        };
    }
}
