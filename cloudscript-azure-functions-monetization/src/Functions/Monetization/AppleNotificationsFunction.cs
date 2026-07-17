using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Functions.Monetization;

/// <summary>
/// App Store Server Notifications v2 endpoint. Apple's verified JWS chain is the request
/// authentication mechanism, so the Azure HTTP trigger remains anonymous by design.
/// </summary>
public sealed class AppleNotificationsFunction
{
    private const int MaxRequestBodyBytes = 1024 * 1024;

    private readonly AppleNotificationParser _parser;
    private readonly SubscriptionLifecycleService _lifecycleService;
    private readonly PurchaseRefundReconciliationService _refundService;
    private readonly MonetizationConfig _config;
    private readonly ILogger<AppleNotificationsFunction> _logger;

    public AppleNotificationsFunction(
        AppleNotificationParser parser,
        SubscriptionLifecycleService lifecycleService,
        PurchaseRefundReconciliationService refundService,
        MonetizationConfig config,
        ILogger<AppleNotificationsFunction> logger)
    {
        _parser = parser;
        _lifecycleService = lifecycleService;
        _refundService = refundService;
        _config = config;
        _logger = logger;
    }

    [Function("AppleNotifications")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/apple")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!_config.Apple.Enabled)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        try
        {
            var body = await HttpRequestSecurity.ReadUtf8BodyAsync(
                req.Body,
                MaxRequestBodyBytes,
                ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var parsed = _parser.Parse(body);
            if (!parsed.IsSuccess)
            {
                _logger.LogWarning(
                    "Apple notification rejected: {ValidationCode}",
                    parsed.ErrorCode);
                // Invalid signatures and malformed signed payloads are permanent failures.
                // Acknowledge them so they cannot create an unbounded retry loop.
                return req.CreateResponse(HttpStatusCode.OK);
            }

            var notificationToken = SensitiveLogValue.Fingerprint(parsed.NotificationId);
            if (parsed.IsTestNotification)
            {
                _logger.LogInformation(
                    "Apple test notification accepted: NotificationToken={NotificationToken}",
                    notificationToken);
                return req.CreateResponse(HttpStatusCode.OK);
            }

            if (parsed.Event == null)
            {
                return req.CreateResponse(HttpStatusCode.OK);
            }

            _logger.LogInformation(
                "Processing Apple notification: Type={EventType}, NotificationToken={NotificationToken}, SubscriptionToken={SubscriptionToken}",
                parsed.Event.EventType,
                notificationToken,
                SensitiveLogValue.Fingerprint(parsed.Event.SubscriptionKey));

            WebhookProcessingResult result;
            if (parsed.Event.EventType is WebhookEventType.Refunded or WebhookEventType.Revoked)
            {
                var transactionKey = PurchaseRecord.CreateTransactionKey(
                    Platform.Apple,
                    parsed.Event.TransactionId!);
                result = await _refundService.ProcessAsync(
                    new PurchaseRefundReconciliationRequest
                    {
                        EventId = parsed.Event.EventId,
                        TransactionKey = transactionKey,
                        Platform = Platform.Apple,
                        ProductIdHint = parsed.Event.ProductId,
                        SubscriptionKey = parsed.Event.SubscriptionKey,
                        // A prorated subscription refund still terminates the corresponding
                        // entitlement period. One-time products require signed proof of 100%.
                        IsFullRefund = parsed.Event.IsSubscription || parsed.Event.IsFullRefund,
                        LifecycleEventType = parsed.Event.EventType,
                        EventTimestampUtc = parsed.Event.EventTimestampUtc,
                        ReceivedAtUtc = parsed.Event.ReceivedAtUtc ?? DateTime.UtcNow
                    },
                    ct).ConfigureAwait(false);
            }
            else
            {
                result = await _lifecycleService
                    .ProcessWebhookEventAsync(parsed.Event, ct)
                    .ConfigureAwait(false);
            }
            if (!result.IsSuccess && !result.IsDuplicate)
            {
                _logger.LogWarning(
                    "Apple notification processing failed: Code={ProcessingCode}, NotificationToken={NotificationToken}",
                    result.ErrorCode,
                    notificationToken);
                if (result.IsRetryable)
                {
                    return req.CreateResponse(HttpStatusCode.InternalServerError);
                }
            }

            _logger.LogInformation(
                "Apple notification processed: NotificationToken={NotificationToken}, Duplicate={IsDuplicate}",
                notificationToken,
                result.IsDuplicate);
            return req.CreateResponse(HttpStatusCode.OK);
        }
        catch (RequestBodyTooLargeException)
        {
            _logger.LogWarning("Apple notification request body exceeded the configured limit");
            return req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
        }
        catch (InvalidDataException)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected Apple notification failure: {ErrorType}", ex.GetType().Name);
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
}
