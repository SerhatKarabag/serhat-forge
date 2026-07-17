using System;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;

/// <summary>
/// Strict parser for Google Play RTDN Pub/Sub push envelopes.
/// Request-level Google OIDC authentication is performed before this parser is called.
/// </summary>
public sealed class GoogleRtdnParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<GoogleRtdnParser> _logger;
    private readonly GoogleRtdnConfig _config;
    private readonly TimeProvider _timeProvider;

    public GoogleRtdnParser(
        GoogleRtdnConfig config,
        ILogger<GoogleRtdnParser> logger,
        TimeProvider? timeProvider = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public GoogleRtdnResult Parse(string requestBody)
    {
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return GoogleRtdnResult.Failure("INVALID_FORMAT", "Missing request body");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<PubSubPushMessage>(requestBody, JsonOptions);
            if (envelope?.Message == null)
            {
                return GoogleRtdnResult.Failure("INVALID_FORMAT", "Missing Pub/Sub message");
            }

            if (string.IsNullOrWhiteSpace(envelope.Message.MessageId))
            {
                return GoogleRtdnResult.Failure("MISSING_EVENT_ID", "Pub/Sub messageId is required");
            }

            if (!DateTimeOffset.TryParse(envelope.Message.PublishTime, out var publishedAt))
            {
                return GoogleRtdnResult.Failure("INVALID_PUBLISH_TIME", "Pub/Sub publishTime is required");
            }

            var now = _timeProvider.GetUtcNow();
            if (publishedAt > now.AddMinutes(5) || now - publishedAt > _config.MaxMessageAge)
            {
                return GoogleRtdnResult.Failure("STALE_NOTIFICATION", "Pub/Sub message timestamp rejected");
            }

            string notificationJson;
            try
            {
                var bytes = Convert.FromBase64String(envelope.Message.Data ?? string.Empty);
                notificationJson = Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return GoogleRtdnResult.Failure("INVALID_BASE64", "Invalid Pub/Sub data encoding");
            }

            var notification = JsonSerializer.Deserialize<GoogleDeveloperNotification>(
                notificationJson,
                JsonOptions);
            if (notification == null || string.IsNullOrWhiteSpace(notification.PackageName))
            {
                return GoogleRtdnResult.Failure("INVALID_NOTIFICATION", "Missing developer notification");
            }

            if (!string.Equals(
                    notification.PackageName,
                    _config.ExpectedPackageName,
                    StringComparison.Ordinal))
            {
                _logger.LogWarning("Google RTDN package identity mismatch");
                return GoogleRtdnResult.Failure("PACKAGE_MISMATCH", "Package name does not match");
            }

            if (!notification.EventTimeMillis.HasValue)
            {
                return GoogleRtdnResult.Failure("MISSING_EVENT_TIME", "eventTimeMillis is required");
            }

            var eventTime = DateTimeOffset.FromUnixTimeMilliseconds(notification.EventTimeMillis.Value);
            if (eventTime > now.AddMinutes(5) || now - eventTime > _config.MaxMessageAge)
            {
                return GoogleRtdnResult.Failure("STALE_NOTIFICATION", "Developer event timestamp rejected");
            }

            var messageId = envelope.Message.MessageId;
            if (notification.TestNotification != null)
            {
                return GoogleRtdnResult.TestNotification(
                    notification.TestNotification.Version ?? "unknown",
                    messageId);
            }

            WebhookEvent? webhookEvent = null;
            if (notification.SubscriptionNotification != null)
            {
                var item = notification.SubscriptionNotification;
                if (string.IsNullOrWhiteSpace(item.PurchaseToken) ||
                    string.IsNullOrWhiteSpace(item.SubscriptionId))
                {
                    return GoogleRtdnResult.Failure("MISSING_PURCHASE_IDENTITY", "Subscription identity is required");
                }

                webhookEvent = MapSubscriptionNotification(notification, messageId, eventTime);
            }
            else if (notification.OneTimeProductNotification != null)
            {
                var item = notification.OneTimeProductNotification;
                if (string.IsNullOrWhiteSpace(item.PurchaseToken) || string.IsNullOrWhiteSpace(item.Sku))
                {
                    return GoogleRtdnResult.Failure("MISSING_PURCHASE_IDENTITY", "Product identity is required");
                }

                webhookEvent = MapOneTimeProductNotification(notification, messageId, eventTime);
            }
            else if (notification.VoidedPurchaseNotification != null)
            {
                var item = notification.VoidedPurchaseNotification;
                if (string.IsNullOrWhiteSpace(item.PurchaseToken))
                {
                    return GoogleRtdnResult.Failure("MISSING_PURCHASE_IDENTITY", "Voided purchase token is required");
                }

                webhookEvent = MapVoidedPurchaseNotification(notification, messageId, eventTime);
            }

            return webhookEvent == null
                ? GoogleRtdnResult.Failure("UNKNOWN_TYPE", "Unknown notification type")
                : GoogleRtdnResult.Success(webhookEvent, messageId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Google RTDN JSON rejected: {ErrorType}", ex.GetType().Name);
            return GoogleRtdnResult.Failure("INVALID_JSON", "Invalid JSON payload");
        }
        catch (ArgumentOutOfRangeException)
        {
            return GoogleRtdnResult.Failure("INVALID_EVENT_TIME", "Invalid event timestamp");
        }
        catch (Exception ex)
        {
            _logger.LogError("Google RTDN parsing failed: {ErrorType}", ex.GetType().Name);
            return GoogleRtdnResult.Failure("PARSE_ERROR", "Notification parsing failed");
        }
    }

    private WebhookEvent MapSubscriptionNotification(
        GoogleDeveloperNotification notification,
        string messageId,
        DateTimeOffset eventTime)
    {
        var item = notification.SubscriptionNotification!;
        var result = CreateEvent(messageId, eventTime, MapSubscriptionNotificationType(item.NotificationType));
        result.SubscriptionKey = SubscriptionRecord.CreateGoogleKey(item.PurchaseToken!);
        result.ProductId = item.SubscriptionId;
        result.TransactionId = item.PurchaseToken;
        result.RawPayloadPreview = $"Type:{item.NotificationType}";
        return result;
    }

    private WebhookEvent MapOneTimeProductNotification(
        GoogleDeveloperNotification notification,
        string messageId,
        DateTimeOffset eventTime)
    {
        var item = notification.OneTimeProductNotification!;
        var result = CreateEvent(messageId, eventTime, MapOneTimeProductNotificationType(item.NotificationType));
        result.ProductId = item.Sku;
        result.TransactionId = item.PurchaseToken;
        result.RawPayloadPreview = $"Type:{item.NotificationType}";
        return result;
    }

    private WebhookEvent MapVoidedPurchaseNotification(
        GoogleDeveloperNotification notification,
        string messageId,
        DateTimeOffset eventTime)
    {
        var item = notification.VoidedPurchaseNotification!;
        var eventType = item.RefundType is 1 or 2
            ? WebhookEventType.Refunded
            : WebhookEventType.Chargeback;

        var result = CreateEvent(messageId, eventTime, eventType);
        result.ProductId = item.ProductId ?? string.Empty;
        result.TransactionId = item.PurchaseToken;
        result.OriginalTransactionId = item.OrderId;
        result.RawPayloadPreview = $"VoidedType:{item.ProductType}";
        return result;
    }

    private WebhookEvent CreateEvent(
        string messageId,
        DateTimeOffset eventTime,
        WebhookEventType eventType) => new()
    {
        EventId = messageId,
        EventType = eventType,
        Platform = Platform.Google,
        EventTimestampUtc = eventTime.UtcDateTime,
        ReceivedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
    };

    private static WebhookEventType MapSubscriptionNotificationType(int? type) => type switch
    {
        1 => WebhookEventType.Recovered,
        2 => WebhookEventType.Renewed,
        3 => WebhookEventType.Cancelled,
        4 => WebhookEventType.InitialPurchase,
        5 or 6 => WebhookEventType.GracePeriodStarted,
        7 => WebhookEventType.Resubscribed,
        8 or 11 => WebhookEventType.UpgradeDowngrade,
        9 or 13 => WebhookEventType.Expired,
        10 => WebhookEventType.Paused,
        12 => WebhookEventType.Revoked,
        20 => WebhookEventType.GracePeriodEnded,
        _ => WebhookEventType.Other
    };

    private static WebhookEventType MapOneTimeProductNotificationType(int? type) => type switch
    {
        1 => WebhookEventType.InitialPurchase,
        2 => WebhookEventType.Cancelled,
        _ => WebhookEventType.Other
    };
}

public sealed class PubSubPushMessage
{
    public PubSubMessage? Message { get; set; }
    public string? Subscription { get; set; }
}

public sealed class PubSubMessage
{
    public string? Data { get; set; }
    public string? MessageId { get; set; }
    public string? PublishTime { get; set; }
}

public sealed class GoogleDeveloperNotification
{
    public string? Version { get; set; }
    public string? PackageName { get; set; }
    public long? EventTimeMillis { get; set; }
    public GoogleSubscriptionNotification? SubscriptionNotification { get; set; }
    public GoogleOneTimeProductNotification? OneTimeProductNotification { get; set; }
    public GoogleVoidedPurchaseNotification? VoidedPurchaseNotification { get; set; }
    public GoogleTestNotification? TestNotification { get; set; }
}

public sealed class GoogleSubscriptionNotification
{
    public string? Version { get; set; }
    public int? NotificationType { get; set; }
    public string? PurchaseToken { get; set; }
    public string? SubscriptionId { get; set; }
}

public sealed class GoogleOneTimeProductNotification
{
    public string? Version { get; set; }
    public int? NotificationType { get; set; }
    public string? PurchaseToken { get; set; }
    public string? Sku { get; set; }
}

public sealed class GoogleVoidedPurchaseNotification
{
    public string? PurchaseToken { get; set; }
    public string? OrderId { get; set; }
    public int? ProductType { get; set; }
    public int? RefundType { get; set; }
    public string? ProductId { get; set; }
}

public sealed class GoogleTestNotification
{
    public string? Version { get; set; }
}

public sealed class GoogleRtdnResult
{
    private GoogleRtdnResult(
        bool success,
        bool isTest,
        WebhookEvent? evt,
        string? messageId,
        string? testVersion,
        string? errorCode,
        string? errorMessage)
    {
        IsSuccess = success;
        IsTestNotification = isTest;
        Event = evt;
        MessageId = messageId;
        TestVersion = testVersion;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsTestNotification { get; }
    public WebhookEvent? Event { get; }
    public string? MessageId { get; }
    public string? TestVersion { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static GoogleRtdnResult Success(WebhookEvent evt, string messageId) =>
        new(true, false, evt, messageId, null, null, null);

    public static GoogleRtdnResult TestNotification(string version, string messageId) =>
        new(true, true, null, messageId, version, null, null);

    public static GoogleRtdnResult Failure(string errorCode, string errorMessage) =>
        new(false, false, null, null, null, errorCode, errorMessage);
}

public sealed class GoogleRtdnConfig
{
    public string ExpectedPackageName { get; set; } = string.Empty;
    public string ExpectedAudience { get; set; } = string.Empty;
    public string ExpectedServiceAccountEmail { get; set; } = string.Empty;
    public TimeSpan MaxMessageAge { get; set; } = TimeSpan.FromDays(7);
}