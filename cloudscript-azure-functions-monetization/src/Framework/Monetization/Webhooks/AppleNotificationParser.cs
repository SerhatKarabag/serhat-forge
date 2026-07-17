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

        if (_config.SkipSignatureValidation && !_config.IsDevelopmentHost)
        {
            throw new InvalidOperationException(
                "Apple signature validation bypass is allowed only in Development/Local/Test.");
        }

        if (!_config.IsDevelopmentHost && _config.AppAppleId <= 0)
        {
            throw new InvalidOperationException(
                "Apple appAppleId must be configured for a production notification parser.");
        }
    }

    public AppleNotificationResult Parse(string requestBody)
    {
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
            if (RequiresSubscriptionIdentity(webhookEvent.EventType) &&
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
        var eventType = MapNotificationType(notification.NotificationType, notification.Subtype);
        var originalTransactionId = transaction?.OriginalTransactionId;

        return new WebhookEvent
        {
            EventId = notification.NotificationUUID!,
            EventType = eventType,
            Platform = Platform.Apple,
            SubscriptionKey = string.IsNullOrWhiteSpace(originalTransactionId)
                ? null
                : SubscriptionRecord.CreateAppleKey(originalTransactionId),
            ProductId = transaction?.ProductId ?? renewal?.ProductId ?? string.Empty,
            OriginalTransactionId = originalTransactionId,
            TransactionId = transaction?.TransactionId,
            EventTimestampUtc = notification.SignedDate.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(notification.SignedDate.Value).UtcDateTime
                : _timeProvider.GetUtcNow().UtcDateTime,
            PeriodStartUtc = ToUtcDateTime(transaction?.PurchaseDate),
            PeriodEndUtc = ToUtcDateTime(transaction?.ExpiresDate),
            AutoRenew = renewal?.AutoRenewStatus == 1,
            GracePeriodEndUtc = ToUtcDateTime(renewal?.GracePeriodExpiresDate),
            ReceivedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            IsSandbox = string.Equals(
                notification.Data?.Environment,
                "Sandbox",
                StringComparison.OrdinalIgnoreCase),
            RawPayloadPreview = $"Type:{notification.NotificationType};Subtype:{notification.Subtype}"
        };
    }

    private static DateTime? ToUtcDateTime(long? unixMilliseconds) =>
        unixMilliseconds.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value).UtcDateTime
            : null;

    private static bool RequiresSubscriptionIdentity(WebhookEventType eventType) => eventType is
        WebhookEventType.Renewed or
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

    private static WebhookEventType MapNotificationType(string? type, string? subtype) =>
        (type?.ToUpperInvariant(), subtype?.ToUpperInvariant()) switch
        {
            ("SUBSCRIBED", "INITIAL_BUY") => WebhookEventType.InitialPurchase,
            ("SUBSCRIBED", "RESUBSCRIBE") => WebhookEventType.Resubscribed,
            ("DID_RENEW", _) => WebhookEventType.Renewed,
            ("DID_CHANGE_RENEWAL_STATUS", "AUTO_RENEW_DISABLED") => WebhookEventType.Cancelled,
            ("DID_CHANGE_RENEWAL_STATUS", "AUTO_RENEW_ENABLED") => WebhookEventType.Resubscribed,
            ("DID_CHANGE_RENEWAL_PREF", _) => WebhookEventType.UpgradeDowngrade,
            ("EXPIRED", _) => WebhookEventType.Expired,
            ("GRACE_PERIOD_EXPIRED", _) => WebhookEventType.GracePeriodEnded,
            ("DID_FAIL_TO_RENEW", _) => WebhookEventType.GracePeriodStarted,
            ("REFUND", _) => WebhookEventType.Refunded,
            ("REVOKE", _) => WebhookEventType.Revoked,
            _ => WebhookEventType.Other
        };

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