using System;
using System.Collections.Generic;
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
    public string? PackageName { get; set; }
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

    public static VerifyPurchaseServiceResponse Fail(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
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
    private readonly ILogger<PurchaseVerificationService> _logger;

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
        ILogger<PurchaseVerificationService> logger)
    {
        _appleVerifier = appleVerifier;
        _googleVerifier = googleVerifier;
        _repository = repository;
        _granter = granter;
        _productConfig = productConfig;
        _logger = logger;
    }

    public async Task<VerifyPurchaseServiceResponse> VerifyAndGrantAsync(
        VerifyPurchaseServiceRequest request,
        CancellationToken ct = default)
    {
        var transactionKey = PurchaseRecord.CreateTransactionKey(request.Platform, request.TransactionId);

        _logger.LogInformation(
            "Verifying purchase: Platform={Platform}, ProductId={ProductId}, TransactionKey={TransactionKey}",
            request.Platform, request.ProductId, transactionKey);

        // Step 1: Check product allowlist
        var productConfig = _productConfig.GetProduct(request.ProductId);
        if (productConfig == null)
        {
            _logger.LogWarning("Product not allowed: {ProductId}", request.ProductId);
            return VerifyPurchaseServiceResponse.Fail("PRODUCT_NOT_ALLOWED",
                $"Product {request.ProductId} is not in the allowlist");
        }

        // Step 2: Check idempotency - return cached result if exists
        var existingRecord = await _repository.GetPurchaseAsync(transactionKey, ct);
        if (existingRecord != null)
        {
            return HandleExistingRecord(existingRecord);
        }

        // Step 3: Create pending record
        var now = DateTime.UtcNow;
        var record = new PurchaseRecord
        {
            TransactionKey = transactionKey,
            Platform = request.Platform,
            ProductId = request.ProductId,
            ProductType = productConfig.Type,
            PlayerId = request.PlayerId,
            Status = PurchaseStatus.Pending,
            TierKey = productConfig.TierKey,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            StoreTransactionId = request.TransactionId
        };

        var created = await _repository.CreatePurchaseAsync(record, ct);
        if (!created)
        {
            // Race condition - another request won
            existingRecord = await _repository.GetPurchaseAsync(transactionKey, ct);
            if (existingRecord != null)
            {
                return HandleExistingRecord(existingRecord);
            }

            return VerifyPurchaseServiceResponse.Fail("IDEMPOTENCY_CONFLICT",
                "Request is already being processed");
        }

        // Step 4: Verify with store
        var verifier = GetVerifier(request.Platform);
        var verifyRequest = new VerifyRequest
        {
            ProductId = request.ProductId,
            TransactionId = request.TransactionId,
            ReceiptPayload = request.ReceiptPayload,
            PackageName = request.PackageName
        };

        var verificationResult = productConfig.IsSubscription
            ? await verifier.VerifySubscriptionAsync(verifyRequest, ct)
            : await verifier.VerifyOneTimePurchaseAsync(verifyRequest, ct);

        if (!verificationResult.IsValid)
        {
            record.Status = PurchaseStatus.Failed;
            record.ErrorCode = verificationResult.ErrorCode;
            record.ErrorMessage = verificationResult.ErrorMessage;
            record.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.UpdatePurchaseAsync(record, ct);

            _logger.LogWarning(
                "Verification failed: {ErrorCode} - {ErrorMessage}",
                verificationResult.ErrorCode, verificationResult.ErrorMessage);

            return VerifyPurchaseServiceResponse.Fail(
                verificationResult.ErrorCode ?? "VERIFICATION_FAILED",
                verificationResult.ErrorMessage ?? "Store verification failed");
        }

        // Step 5: Sandbox check
        if (verificationResult.IsSandbox && !_productConfig.AllowSandboxInProduction)
        {
            var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT");
            if (env == "Production")
            {
                record.Status = PurchaseStatus.Failed;
                record.ErrorCode = "SANDBOX_NOT_ALLOWED";
                record.ErrorMessage = "Sandbox purchases not allowed in production";
                record.UpdatedAtUtc = DateTime.UtcNow;
                await _repository.UpdatePurchaseAsync(record, ct);

                return VerifyPurchaseServiceResponse.Fail("SANDBOX_NOT_ALLOWED",
                    "Sandbox purchases not allowed in production");
            }
        }

        record.Status = PurchaseStatus.Verified;
        record.OriginalTransactionId = verificationResult.OriginalTransactionId;

        // Step 6: Grant entitlements
        var grantRequest = new GrantRequest
        {
            PlayerId = request.PlayerId,
            ItemIds = productConfig.EconomyItemIds,
            Quantities = productConfig.Type == ProductType.Consumable
                ? new List<int> { productConfig.Quantity }
                : null,
            IdempotencyKey = transactionKey,
            Metadata = request.Metadata
        };

        var grantResult = await _granter.GrantItemsAsync(grantRequest, ct);

        if (!grantResult.IsSuccess)
        {
            record.Status = PurchaseStatus.Failed;
            record.ErrorCode = grantResult.ErrorCode;
            record.ErrorMessage = grantResult.ErrorMessage;
            record.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.UpdatePurchaseAsync(record, ct);

            _logger.LogWarning(
                "Grant failed: {ErrorCode} - {ErrorMessage}",
                grantResult.ErrorCode, grantResult.ErrorMessage);

            return VerifyPurchaseServiceResponse.Fail(
                grantResult.ErrorCode ?? "GRANT_FAILED",
                grantResult.ErrorMessage ?? "Failed to grant entitlements");
        }

        // Step 7: Handle subscription record
        SubscriptionServiceDto? subscriptionDto = null;
        if (productConfig.IsSubscription)
        {
            subscriptionDto = await HandleSubscriptionAsync(
                request, productConfig, verificationResult, ct);
        }

        // Step 8: Complete purchase record
        record.Status = PurchaseStatus.Granted;
        record.GrantedEconomyItemIds = grantResult.GrantedItemIds;
        record.QuantityGranted = productConfig.Quantity;
        record.UpdatedAtUtc = DateTime.UtcNow;

        var response = VerifyPurchaseServiceResponse.Ok(
            transactionKey,
            grantResult.GrantedItemIds,
            subscriptionDto);

        record.CachedResponseJson = JsonSerializer.Serialize(response, JsonOptions);
        await _repository.UpdatePurchaseAsync(record, ct);

        _logger.LogInformation(
            "Purchase completed: TransactionToken={TransactionToken}, GrantedItems={Items}",
            SensitiveLogValue.Fingerprint(transactionKey), string.Join(",", grantResult.GrantedItemIds));

        return response;
    }

    private VerifyPurchaseServiceResponse HandleExistingRecord(PurchaseRecord record)
    {
        _logger.LogInformation("Found existing record: Status={Status}, TransactionToken={TransactionToken}",
            record.Status, SensitiveLogValue.Fingerprint(record.TransactionKey));

        switch (record.Status)
        {
            case PurchaseStatus.Granted:
                // Return cached response
                if (!string.IsNullOrEmpty(record.CachedResponseJson))
                {
                    var cached = JsonSerializer.Deserialize<VerifyPurchaseServiceResponse>(
                        record.CachedResponseJson, JsonOptions);
                    if (cached != null)
                    {
                        cached.IsDuplicate = true;
                        return cached;
                    }
                }

                return VerifyPurchaseServiceResponse.Ok(
                    record.TransactionKey,
                    record.GrantedEconomyItemIds,
                    isDuplicate: true);

            case PurchaseStatus.Pending:
            case PurchaseStatus.Verified:
                return VerifyPurchaseServiceResponse.Fail("IN_PROGRESS",
                    "Purchase is being processed");

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

    private async Task<SubscriptionServiceDto?> HandleSubscriptionAsync(
        VerifyPurchaseServiceRequest request,
        ProductConfig productConfig,
        VerificationResult verificationResult,
        CancellationToken ct)
    {
        var subscriptionKey = request.Platform == Platform.Apple
            ? SubscriptionRecord.CreateAppleKey(verificationResult.OriginalTransactionId ?? request.TransactionId)
            : SubscriptionRecord.CreateGoogleKey(request.ReceiptPayload);

        var existing = await _repository.GetSubscriptionAsync(subscriptionKey, ct);

        if (existing != null)
        {
            // Update existing subscription
            existing.Status = MapVerificationToSubscriptionStatus(verificationResult);
            existing.AutoRenew = verificationResult.AutoRenew ?? false;
            existing.PeriodEndUtc = verificationResult.ExpirationDateUtc ?? DateTime.UtcNow.AddMonths(1);
            existing.LastEventAtUtc = DateTime.UtcNow;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            await _repository.UpdateSubscriptionAsync(existing, ct);

            return new SubscriptionServiceDto
            {
                ProductId = existing.ProductId,
                TierKey = existing.TierKey,
                Status = existing.Status,
                AutoRenew = existing.AutoRenew,
                PeriodStartUtc = existing.PeriodStartUtc,
                PeriodEndUtc = existing.PeriodEndUtc
            };
        }

        // Create new subscription record
        var now = DateTime.UtcNow;
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = subscriptionKey,
            Platform = request.Platform,
            PlayerId = request.PlayerId,
            ProductId = request.ProductId,
            TierKey = productConfig.TierKey ?? "default",
            TierPrecedence = productConfig.TierPrecedence,
            Status = MapVerificationToSubscriptionStatus(verificationResult),
            ActiveEconomyItemId = productConfig.EconomyItemIds.Count > 0
                ? productConfig.EconomyItemIds[0]
                : null,
            AutoRenew = verificationResult.AutoRenew ?? false,
            PeriodStartUtc = verificationResult.PurchaseDateUtc ?? now,
            PeriodEndUtc = verificationResult.ExpirationDateUtc ?? now.AddMonths(1),
            OriginalPurchaseDateUtc = verificationResult.PurchaseDateUtc ?? now,
            LastEventAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _repository.CreateSubscriptionAsync(subscription, ct);

        return new SubscriptionServiceDto
        {
            ProductId = subscription.ProductId,
            TierKey = subscription.TierKey,
            Status = subscription.Status,
            AutoRenew = subscription.AutoRenew,
            PeriodStartUtc = subscription.PeriodStartUtc,
            PeriodEndUtc = subscription.PeriodEndUtc
        };
    }

    private static SubscriptionStatus MapVerificationToSubscriptionStatus(VerificationResult result)
    {
        if (result.SubscriptionStatus.HasValue)
        {
            return result.SubscriptionStatus.Value;
        }

        if (result.ExpirationDateUtc.HasValue && result.ExpirationDateUtc.Value < DateTime.UtcNow)
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
