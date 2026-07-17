using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;

/// <summary>
/// Strict parser for Google Play RTDN Pub/Sub push envelopes.
/// Request-level Google OIDC authentication is performed before this parser is called.
/// RTDN notification types are retained only as change hints; they are never translated
/// into entitlement state by this parser.
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

            var payloadCount = CountPayloads(notification);
            if (payloadCount != 1)
            {
                return GoogleRtdnResult.Failure(
                    "INVALID_NOTIFICATION",
                    "Exactly one developer notification payload is required");
            }

            var messageId = envelope.Message.MessageId;
            if (notification.TestNotification != null)
            {
                return GoogleRtdnResult.TestNotification(
                    notification.TestNotification.Version ?? "unknown",
                    messageId);
            }

            GoogleRtdnNotification? parsedNotification = null;
            if (notification.SubscriptionNotification != null)
            {
                var item = notification.SubscriptionNotification;
                if (string.IsNullOrWhiteSpace(item.PurchaseToken))
                {
                    return GoogleRtdnResult.Failure(
                        "MISSING_PURCHASE_IDENTITY",
                        "Subscription purchase token is required");
                }

                parsedNotification = new GoogleRtdnNotification
                {
                    EventId = messageId,
                    Kind = GoogleRtdnNotificationKind.SubscriptionStateChanged,
                    PurchaseToken = item.PurchaseToken,
                    ProductIdHint = item.SubscriptionId,
                    NotificationType = item.NotificationType,
                    EventTimestampUtc = eventTime.UtcDateTime,
                    ReceivedAtUtc = now.UtcDateTime
                };
            }
            else if (notification.OneTimeProductNotification != null)
            {
                var item = notification.OneTimeProductNotification;
                if (string.IsNullOrWhiteSpace(item.PurchaseToken) ||
                    string.IsNullOrWhiteSpace(item.Sku))
                {
                    return GoogleRtdnResult.Failure(
                        "MISSING_PURCHASE_IDENTITY",
                        "One-time product identity is required");
                }

                parsedNotification = new GoogleRtdnNotification
                {
                    EventId = messageId,
                    Kind = GoogleRtdnNotificationKind.OneTimeProductChanged,
                    PurchaseToken = item.PurchaseToken,
                    ProductIdHint = item.Sku,
                    NotificationType = item.NotificationType,
                    EventTimestampUtc = eventTime.UtcDateTime,
                    ReceivedAtUtc = now.UtcDateTime
                };
            }
            else if (notification.VoidedPurchaseNotification != null)
            {
                var item = notification.VoidedPurchaseNotification;
                if (string.IsNullOrWhiteSpace(item.PurchaseToken) ||
                    !item.ProductType.HasValue ||
                    !item.RefundType.HasValue)
                {
                    return GoogleRtdnResult.Failure(
                        "MISSING_PURCHASE_IDENTITY",
                        "Voided purchase identity is required");
                }

                parsedNotification = new GoogleRtdnNotification
                {
                    EventId = messageId,
                    Kind = GoogleRtdnNotificationKind.VoidedPurchase,
                    PurchaseToken = item.PurchaseToken,
                    ProductIdHint = item.ProductId,
                    OrderIdHint = item.OrderId,
                    ProductType = item.ProductType,
                    RefundType = item.RefundType,
                    EventTimestampUtc = eventTime.UtcDateTime,
                    ReceivedAtUtc = now.UtcDateTime
                };
            }

            return parsedNotification == null
                ? GoogleRtdnResult.Failure("UNKNOWN_TYPE", "Unknown notification type")
                : GoogleRtdnResult.Success(parsedNotification, messageId);
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

    private static int CountPayloads(GoogleDeveloperNotification notification)
    {
        var count = 0;
        count += notification.SubscriptionNotification == null ? 0 : 1;
        count += notification.OneTimeProductNotification == null ? 0 : 1;
        count += notification.VoidedPurchaseNotification == null ? 0 : 1;
        count += notification.TestNotification == null ? 0 : 1;
        return count;
    }
}

public enum GoogleRtdnNotificationKind
{
    SubscriptionStateChanged,
    OneTimeProductChanged,
    VoidedPurchase
}

/// <summary>
/// Authenticated RTDN change hint. <see cref="PurchaseToken"/> is a sensitive credential:
/// it may be passed to Google and hashed for durable lookup, but must never be logged or stored.
/// </summary>
public sealed class GoogleRtdnNotification
{
    public string EventId { get; init; } = string.Empty;
    public GoogleRtdnNotificationKind Kind { get; init; }
    public string PurchaseToken { get; init; } = string.Empty;
    public string? ProductIdHint { get; init; }
    public string? OrderIdHint { get; init; }
    public int? NotificationType { get; init; }
    public int? ProductType { get; init; }
    public int? RefundType { get; init; }
    public DateTime EventTimestampUtc { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
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
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
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
        GoogleRtdnNotification? notification,
        string? messageId,
        string? testVersion,
        string? errorCode,
        string? errorMessage)
    {
        IsSuccess = success;
        IsTestNotification = isTest;
        Notification = notification;
        MessageId = messageId;
        TestVersion = testVersion;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsTestNotification { get; }
    public GoogleRtdnNotification? Notification { get; }
    public string? MessageId { get; }
    public string? TestVersion { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static GoogleRtdnResult Success(
        GoogleRtdnNotification notification,
        string messageId) =>
        new(true, false, notification, messageId, null, null, null);

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
