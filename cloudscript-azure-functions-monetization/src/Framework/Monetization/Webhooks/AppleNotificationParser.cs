using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;

/// <summary>
/// Parser and verifier for App Store Server Notifications v2.
/// </summary>
public sealed class AppleNotificationParser
{
    private const string AutoRenewableSubscriptionType = "Auto-Renewable Subscription";
    private const string FullRefundType = "REFUND_FULL";
    private const string FamilyRevokeType = "FAMILY_REVOKE";
    private const int FullRevocationPercentage = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<AppleNotificationParser> _logger;
    private readonly AppleNotificationConfig _config;
    private readonly IAppleJwsVerifier _jwsVerifier;
    private readonly TimeProvider _timeProvider;

    public AppleNotificationParser(
        AppleNotificationConfig config,
        ILogger<AppleNotificationParser> logger)
        : this(config, CreateVerifier(config), logger, TimeProvider.System)
    {
    }

    public AppleNotificationParser(
        AppleNotificationConfig config,
        IAppleJwsVerifier jwsVerifier,
        ILogger<AppleNotificationParser> logger,
        TimeProvider? timeProvider = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _jwsVerifier = jwsVerifier ?? throw new ArgumentNullException(nameof(jwsVerifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_config.Enabled && _config.SkipSignatureValidation && !_config.IsDevelopmentHost)
        {
            throw new InvalidOperationException(
                "Apple signature validation bypass is allowed only in Development/Local/Test.");
        }

        if (_config.Enabled && !_config.IsDevelopmentHost && _config.AppAppleId <= 0)
        {
            throw new InvalidOperationException(
                "Apple appAppleId must be configured for a production notification parser.");
        }
    }

    public AppleNotificationResult Parse(string requestBody)
    {
        if (!_config.Enabled)
        {
            return AppleNotificationResult.Failure("STORE_DISABLED", "Apple Store is disabled");
        }

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return AppleNotificationResult.Failure("INVALID_FORMAT", "Missing request body");
        }

        try
        {
            var notification = JsonSerializer.Deserialize<AppleSignedNotification>(requestBody, JsonOptions);
            if (string.IsNullOrWhiteSpace(notification?.SignedPayload))
            {
                return AppleNotificationResult.Failure("INVALID_FORMAT", "Missing signedPayload");
            }

            var payloadResult = DecodeAndValidateJws(notification.SignedPayload);
            if (!payloadResult.IsValid)
            {
                return AppleNotificationResult.Failure(
                    payloadResult.ErrorCode ?? "INVALID_SIGNATURE",
                    "JWS validation failed");
            }

            var payload = JsonSerializer.Deserialize<AppleNotificationPayload>(
                payloadResult.Payload!,
                JsonOptions);
            if (payload == null)
            {
                return AppleNotificationResult.Failure("INVALID_PAYLOAD", "Invalid notification payload");
            }

            var envelopeValidation = ValidateEnvelope(payload);
            if (envelopeValidation != null)
            {
                return envelopeValidation;
            }

            if (string.Equals(payload.NotificationType, "TEST", StringComparison.OrdinalIgnoreCase))
            {
                return AppleNotificationResult.TestNotification(payload.NotificationUUID);
            }

            AppleTransactionInfo? transaction = null;
            if (!string.IsNullOrWhiteSpace(payload.Data?.SignedTransactionInfo))
            {
                var transactionResult = DecodeAndValidateJws(payload.Data.SignedTransactionInfo);
                if (!transactionResult.IsValid)
                {
                    return AppleNotificationResult.Failure(
                        "INVALID_TRANSACTION_SIGNATURE",
                        "signedTransactionInfo validation failed");
                }

                transaction = JsonSerializer.Deserialize<AppleTransactionInfo>(
                    transactionResult.Payload!,
                    JsonOptions);
                if (transaction == null || !ValidateTransaction(transaction, payload.Data))
                {
                    return AppleNotificationResult.Failure(
                        "INVALID_TRANSACTION",
                        "Transaction identity does not match the notification");
                }
            }

            AppleRenewalInfo? renewal = null;
            if (!string.IsNullOrWhiteSpace(payload.Data?.SignedRenewalInfo))
            {
                var renewalResult = DecodeAndValidateJws(payload.Data.SignedRenewalInfo);
                if (!renewalResult.IsValid)
                {
                    return AppleNotificationResult.Failure(
                        "INVALID_RENEWAL_SIGNATURE",
                        "signedRenewalInfo validation failed");
                }

                renewal = JsonSerializer.Deserialize<AppleRenewalInfo>(
                    renewalResult.Payload!,
                    JsonOptions);
                if (renewal == null || !string.Equals(
                        renewal.Environment,
                        _config.ExpectedEnvironment,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return AppleNotificationResult.Failure(
                        "INVALID_RENEWAL",
                        "Renewal environment does not match the notification");
                }
            }

            var webhookEvent = MapToWebhookEvent(payload, transaction, renewal);
            if (webhookEvent.EventType is WebhookEventType.Refunded or WebhookEventType.Revoked &&
                string.IsNullOrWhiteSpace(transaction?.TransactionId))
            {
                return AppleNotificationResult.Failure(
                    "MISSING_REFUND_IDENTITY",
                    "Refund or revocation notification has no transaction ID");
            }

            if (RequiresSubscriptionIdentity(webhookEvent) &&
                string.IsNullOrWhiteSpace(transaction?.OriginalTransactionId))
            {
                return AppleNotificationResult.Failure(
                    "MISSING_SUBSCRIPTION_IDENTITY",
                    "Notification has no original transaction ID");
            }

            return AppleNotificationResult.Success(webhookEvent, payload.NotificationUUID!);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Apple notification JSON rejected: {ErrorType}", ex.GetType().Name);
            return AppleNotificationResult.Failure("INVALID_JSON", "Invalid JSON payload");
        }
        catch (FormatException ex)
        {
            _logger.LogWarning("Apple notification encoding rejected: {ErrorType}", ex.GetType().Name);
            return AppleNotificationResult.Failure("INVALID_ENCODING", "Invalid encoded payload");
        }
        catch (Exception ex)
        {
            _logger.LogError("Apple notification parsing failed: {ErrorType}", ex.GetType().Name);
            return AppleNotificationResult.Failure("PARSE_ERROR", "Notification parsing failed");
        }
    }

    private AppleNotificationResult? ValidateEnvelope(AppleNotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.NotificationUUID))
        {
            return AppleNotificationResult.Failure("MISSING_EVENT_ID", "notificationUUID is required");
        }

        if (payload.Data == null || string.IsNullOrWhiteSpace(payload.Data.BundleId))
        {
            return AppleNotificationResult.Failure("MISSING_APP_IDENTITY", "Notification has no bundle ID");
        }

        if (!string.Equals(payload.Data.BundleId, _config.BundleId, StringComparison.Ordinal))
        {
            return AppleNotificationResult.Failure("BUNDLE_ID_MISMATCH", "Bundle ID does not match");
        }

        if (_config.AppAppleId > 0 && payload.Data.AppAppleId != _config.AppAppleId)
        {
            return AppleNotificationResult.Failure("APP_APPLE_ID_MISMATCH", "Apple app ID does not match");
        }

        if (!string.Equals(
                payload.Data.Environment,
                _config.ExpectedEnvironment,
                StringComparison.OrdinalIgnoreCase))
        {
            return AppleNotificationResult.Failure("ENVIRONMENT_MISMATCH", "Environment does not match");
        }

        if (!_config.SkipSignatureValidation)
        {
            if (!payload.SignedDate.HasValue)
            {
                return AppleNotificationResult.Failure("MISSING_SIGNED_DATE", "signedDate is required");
            }

            var signedAt = DateTimeOffset.FromUnixTimeMilliseconds(payload.SignedDate.Value);
            var now = _timeProvider.GetUtcNow();
            if (signedAt > now.AddMinutes(5) || now - signedAt > _config.MaxNotificationAge)
            {
                return AppleNotificationResult.Failure("STALE_NOTIFICATION", "Notification timestamp rejected");
            }
        }

        return null;
    }

    private bool ValidateTransaction(AppleTransactionInfo transaction, AppleNotificationData data)
    {
        if (!string.Equals(transaction.BundleId, _config.BundleId, StringComparison.Ordinal) ||
            !string.Equals(transaction.BundleId, data.BundleId, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
            transaction.Environment,
            _config.ExpectedEnvironment,
            StringComparison.OrdinalIgnoreCase);
    }

    private AppleJwsVerificationResult DecodeAndValidateJws(string compactJws)
    {
        if (!_config.SkipSignatureValidation)
        {
            return _jwsVerifier.Verify(compactJws);
        }

        var parts = compactJws.Split('.');
        if (parts.Length != 3)
        {
            return AppleJwsVerificationResult.Failure("INVALID_JWS_FORMAT");
        }

        var payload = AppleJwsVerifier.Base64UrlDecode(parts[1]);
        return AppleJwsVerificationResult.Success(Encoding.UTF8.GetString(payload));
    }

    private WebhookEvent MapToWebhookEvent(
        AppleNotificationPayload notification,
        AppleTransactionInfo? transaction,
        AppleRenewalInfo? renewal)
    {
        var eventType = MapNotificationType(notification, renewal);
        var originalTransactionId = transaction?.OriginalTransactionId;
        var isSubscription = string.Equals(
                                 transaction?.Type,
                                 AutoRenewableSubscriptionType,
                                 StringComparison.Ordinal) ||
                             IsSubscriptionLifecycleEvent(eventType);
        var isRefundOrRevocation =
            eventType is WebhookEventType.Refunded or WebhookEventType.Revoked;
        var revocationTimestamp = isRefundOrRevocation
            ? ToUtcDateTime(transaction?.RevocationDate)
            : null;

        return new WebhookEvent
        {
            EventId = notification.NotificationUUID!,
            EventType = eventType,
            Platform = Platform.Apple,
            SubscriptionKey = !isSubscription || string.IsNullOrWhiteSpace(originalTransactionId)
                ? null
                : SubscriptionRecord.CreateAppleKey(originalTransactionId),
            ProductId = transaction?.ProductId ?? renewal?.ProductId ?? string.Empty,
            OriginalTransactionId = originalTransactionId,
            TransactionId = transaction?.TransactionId,
            EventTimestampUtc = revocationTimestamp ?? (notification.SignedDate.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(notification.SignedDate.Value).UtcDateTime
                : _timeProvider.GetUtcNow().UtcDateTime),
            PeriodStartUtc = ToUtcDateTime(transaction?.PurchaseDate),
            PeriodEndUtc = ToUtcDateTime(transaction?.ExpiresDate),
            AutoRenew = renewal?.AutoRenewStatus == 1,
            GracePeriodEndUtc = ToUtcDateTime(renewal?.GracePeriodExpiresDate),
            ReceivedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            IsSandbox = string.Equals(
                notification.Data?.Environment,
                "Sandbox",
                StringComparison.OrdinalIgnoreCase),
            RawPayloadPreview = $"Type:{notification.NotificationType};Subtype:{notification.Subtype}",
            IsSubscription = isSubscription,
            RevocationType = NormalizeRevocationType(transaction?.RevocationType),
            RevocationPercentage = NormalizeRevocationPercentage(
                transaction?.RevocationPercentage),
            IsFullRefund = IsFullRefundOrRevocation(transaction),
            EntitlementOperationId = isRefundOrRevocation &&
                                     !string.IsNullOrWhiteSpace(transaction?.TransactionId)
                ? CreateRefundOperationId(transaction.TransactionId)
                : null
        };
    }

    private static DateTime? ToUtcDateTime(long? unixMilliseconds) =>
        unixMilliseconds.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value).UtcDateTime
            : null;

    private static bool RequiresSubscriptionIdentity(WebhookEvent evt) =>
        (evt.EventType is not WebhookEventType.Refunded and not WebhookEventType.Revoked ||
         evt.IsSubscription) &&
        evt.EventType is WebhookEventType.Renewed or
        WebhookEventType.Cancelled or
        WebhookEventType.Expired or
        WebhookEventType.Refunded or
        WebhookEventType.Chargeback or
        WebhookEventType.GracePeriodStarted or
        WebhookEventType.GracePeriodEnded or
        WebhookEventType.Paused or
        WebhookEventType.Resumed or
        WebhookEventType.UpgradeDowngrade or
        WebhookEventType.Revoked or
        WebhookEventType.Recovered;

    private static bool IsSubscriptionLifecycleEvent(WebhookEventType eventType) => eventType is
        WebhookEventType.InitialPurchase or
        WebhookEventType.Resubscribed or
        WebhookEventType.Renewed or
        WebhookEventType.Cancelled or
        WebhookEventType.Expired or
        WebhookEventType.GracePeriodStarted or
        WebhookEventType.GracePeriodEnded or
        WebhookEventType.Paused or
        WebhookEventType.Resumed or
        WebhookEventType.UpgradeDowngrade or
        WebhookEventType.Recovered;

    private static bool IsFullRefundOrRevocation(AppleTransactionInfo? transaction) =>
        transaction != null &&
        (string.Equals(transaction.RevocationType, FullRefundType, StringComparison.Ordinal) ||
         string.Equals(transaction.RevocationType, FamilyRevokeType, StringComparison.Ordinal) ||
         transaction.RevocationPercentage == FullRevocationPercentage);

    private static string? NormalizeRevocationType(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 32
            ? null
            : value.ToUpperInvariant();

    private static int? NormalizeRevocationPercentage(int? value) =>
        value is >= 0 and <= FullRevocationPercentage ? value : null;

    private static string CreateRefundOperationId(string transactionId)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"apple-refund\n{transactionId}"));
        return $"apple-refund:{Convert.ToHexString(digest)}";
    }

    private WebhookEventType MapNotificationType(
        AppleNotificationPayload notification,
        AppleRenewalInfo? renewal)
    {
        var type = notification.NotificationType?.ToUpperInvariant();
        var subtype = notification.Subtype?.ToUpperInvariant();

        // DID_FAIL_TO_RENEW is not proof that Billing Grace Period is enabled. Apple status 4,
        // a GRACE_PERIOD subtype, and a future signed grace-period deadline must all agree
        // before benefits are retained. Billing-retry status 3 is fail-closed and revokes.
        if (type == "DID_FAIL_TO_RENEW")
        {
            var gracePeriodEnd = ToUtcDateTime(renewal?.GracePeriodExpiresDate);
            return subtype == "GRACE_PERIOD" &&
                   notification.Data?.Status == 4 &&
                   gracePeriodEnd > _timeProvider.GetUtcNow().UtcDateTime
                ? WebhookEventType.GracePeriodStarted
                : WebhookEventType.GracePeriodEnded;
        }

        return (type, subtype) switch
        {
            ("SUBSCRIBED", "INITIAL_BUY") => WebhookEventType.InitialPurchase,
            ("SUBSCRIBED", "RESUBSCRIBE") => WebhookEventType.Resubscribed,
            ("DID_RENEW", _) => WebhookEventType.Renewed,
            ("DID_RECOVER", _) => WebhookEventType.Recovered,
            ("DID_CHANGE_RENEWAL_STATUS", "AUTO_RENEW_DISABLED") => WebhookEventType.Cancelled,
            ("DID_CHANGE_RENEWAL_STATUS", "AUTO_RENEW_ENABLED") => WebhookEventType.Resubscribed,
            ("DID_CHANGE_RENEWAL_PREF", _) => WebhookEventType.UpgradeDowngrade,
            ("EXPIRED", _) => WebhookEventType.Expired,
            ("GRACE_PERIOD_EXPIRED", _) => WebhookEventType.GracePeriodEnded,
            ("REFUND", _) => WebhookEventType.Refunded,
            ("REVOKE", _) => WebhookEventType.Revoked,
            _ => WebhookEventType.Other
        };
    }

    private static IAppleJwsVerifier CreateVerifier(AppleNotificationConfig config)
    {
        if (!Enum.TryParse<X509RevocationMode>(
                config.CertificateRevocationMode,
                true,
                out var revocationMode))
        {
            throw new InvalidOperationException("Invalid Apple certificate revocation mode.");
        }

        return new AppleJwsVerifier(
            new AppleJwsVerificationOptions
            {
                TrustedRootCertificatesBase64 = config.TrustedRootCertificatesBase64,
                RevocationMode = revocationMode
            },
            NullLogger<AppleJwsVerifier>.Instance);
    }
}

public sealed class AppleSignedNotification
{
    public string? SignedPayload { get; set; }
}

public sealed class AppleNotificationPayload
{
    public string? NotificationType { get; set; }
    public string? Subtype { get; set; }
    public string? NotificationUUID { get; set; }
    public string? Version { get; set; }
    public long? SignedDate { get; set; }
    public AppleNotificationData? Data { get; set; }
}

public sealed class AppleNotificationData
{
    public long? AppAppleId { get; set; }
    public string? BundleId { get; set; }
    public string? BundleVersion { get; set; }
    public string? Environment { get; set; }
    public string? SignedTransactionInfo { get; set; }
    public string? SignedRenewalInfo { get; set; }
    public int? Status { get; set; }
}

public sealed class AppleTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public string? WebOrderLineItemId { get; set; }
    public string? BundleId { get; set; }
    public string? ProductId { get; set; }
    public string? SubscriptionGroupIdentifier { get; set; }
    public long? PurchaseDate { get; set; }
    public long? OriginalPurchaseDate { get; set; }
    public long? ExpiresDate { get; set; }
    public int? Quantity { get; set; }
    public string? Type { get; set; }
    public string? InAppOwnershipType { get; set; }
    public long? SignedDate { get; set; }
    public string? Environment { get; set; }
    public string? TransactionReason { get; set; }
    public string? Storefront { get; set; }
    public string? StorefrontId { get; set; }
    public long? RevocationDate { get; set; }
    public int? RevocationReason { get; set; }
    public string? RevocationType { get; set; }
    public int? RevocationPercentage { get; set; }
}

public sealed class AppleRenewalInfo
{
    public string? OriginalTransactionId { get; set; }
    public string? AutoRenewProductId { get; set; }
    public string? ProductId { get; set; }
    public int? AutoRenewStatus { get; set; }
    public int? IsInBillingRetryPeriod { get; set; }
    public int? PriceIncreaseStatus { get; set; }
    public long? GracePeriodExpiresDate { get; set; }
    public string? OfferType { get; set; }
    public string? OfferIdentifier { get; set; }
    public long? SignedDate { get; set; }
    public string? Environment { get; set; }
    public long? RecentSubscriptionStartDate { get; set; }
    public long? RenewalDate { get; set; }
}

public sealed class AppleNotificationResult
{
    private AppleNotificationResult(
        bool success,
        bool isTestNotification,
        WebhookEvent? evt,
        string? notificationId,
        string? errorCode,
        string? errorMessage)
    {
        IsSuccess = success;
        IsTestNotification = isTestNotification;
        Event = evt;
        NotificationId = notificationId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsTestNotification { get; }
    public WebhookEvent? Event { get; }
    public string? NotificationId { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static AppleNotificationResult Success(WebhookEvent evt, string notificationId) =>
        new(true, false, evt, notificationId, null, null);

    public static AppleNotificationResult TestNotification(string? notificationId) =>
        new(true, true, null, notificationId, null, null);

    public static AppleNotificationResult Failure(string errorCode, string errorMessage) =>
        new(false, false, null, null, errorCode, errorMessage);
}

public sealed class AppleNotificationConfig
{
    public bool Enabled { get; set; } = true;
    public string BundleId { get; set; } = string.Empty;
    public long AppAppleId { get; set; }
    public string ExpectedEnvironment { get; set; } = "Production";
    public string TrustedRootCertificatesBase64 { get; set; } = string.Empty;
    public string CertificateRevocationMode { get; set; } = "Online";
    public TimeSpan MaxNotificationAge { get; set; } = TimeSpan.FromDays(7);
    public bool SkipSignatureValidation { get; set; }
    public string HostEnvironmentName { get; set; } = "Production";

    public bool IsDevelopmentHost =>
        string.Equals(HostEnvironmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(HostEnvironmentName, "Local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(HostEnvironmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
