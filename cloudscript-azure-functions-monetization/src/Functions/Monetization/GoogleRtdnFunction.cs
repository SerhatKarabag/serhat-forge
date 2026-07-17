using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Functions.Monetization;

/// <summary>
/// Google Play RTDN endpoint. AuthorizationLevel.Anonymous is intentional because Pub/Sub
/// supplies a Google-signed OIDC bearer token which is validated before the body is read.
/// </summary>
public sealed class GoogleRtdnFunction
{
    private const int MaxRequestBodyBytes = 256 * 1024;

    private readonly GooglePubSubAuthenticator _authenticator;
    private readonly GoogleRtdnParser _parser;
    private readonly GoogleRtdnReconciliationService _reconciliationService;
    private readonly MonetizationConfig _config;
    private readonly ILogger<GoogleRtdnFunction> _logger;

    public GoogleRtdnFunction(
        GooglePubSubAuthenticator authenticator,
        GoogleRtdnParser parser,
        GoogleRtdnReconciliationService reconciliationService,
        MonetizationConfig config,
        ILogger<GoogleRtdnFunction> logger)
    {
        _authenticator = authenticator;
        _parser = parser;
        _reconciliationService = reconciliationService;
        _config = config;
        _logger = logger;
    }

    [Function("GoogleRtdn")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/google")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!_config.Google.Enabled)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        IEnumerable<string>? authorizationHeaders = null;
        if (req.Headers.TryGetValues("Authorization", out var values))
        {
            authorizationHeaders = values;
        }

        var authentication = await _authenticator
            .AuthenticateAsync(authorizationHeaders, ct)
            .ConfigureAwait(false);
        if (!authentication.IsAuthenticated)
        {
            _logger.LogWarning(
                "Google RTDN request rejected: {AuthenticationCode}",
                authentication.ErrorCode);
            var status = authentication.IsUnavailable
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.Unauthorized;
            var rejected = req.CreateResponse(status);
            if (status == HttpStatusCode.Unauthorized)
            {
                rejected.Headers.Add("WWW-Authenticate", "Bearer");
            }

            return rejected;
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
                    "Authenticated Google RTDN payload rejected: {ValidationCode}",
                    parsed.ErrorCode);
                // The message is authenticated but permanently malformed; acknowledge it to
                // prevent a poison message from creating an unbounded Pub/Sub retry loop.
                return req.CreateResponse(HttpStatusCode.OK);
            }

            var messageToken = SensitiveLogValue.Fingerprint(parsed.MessageId);
            if (parsed.IsTestNotification)
            {
                _logger.LogInformation(
                    "Google RTDN test notification accepted: MessageToken={MessageToken}",
                    messageToken);
                return req.CreateResponse(HttpStatusCode.OK);
            }

            if (parsed.Notification == null)
            {
                return req.CreateResponse(HttpStatusCode.OK);
            }

            _logger.LogInformation(
                "Processing Google RTDN change hint: Kind={NotificationKind}, MessageToken={MessageToken}, PurchaseToken={PurchaseToken}",
                parsed.Notification.Kind,
                messageToken,
                SensitiveLogValue.Fingerprint(parsed.Notification.PurchaseToken));

            var result = await _reconciliationService
                .ProcessAsync(parsed.Notification, ct)
                .ConfigureAwait(false);
            if (!result.IsSuccess && !result.IsDuplicate)
            {
                _logger.LogWarning(
                    "Google RTDN processing failed: Code={ProcessingCode}, MessageToken={MessageToken}",
                    result.ErrorCode,
                    messageToken);
                if (result.IsRetryable)
                {
                    return req.CreateResponse(HttpStatusCode.InternalServerError);
                }
            }

            _logger.LogInformation(
                "Google RTDN processed: MessageToken={MessageToken}, Duplicate={IsDuplicate}",
                messageToken,
                result.IsDuplicate);
            return req.CreateResponse(HttpStatusCode.OK);
        }
        catch (RequestBodyTooLargeException)
        {
            _logger.LogWarning("Google RTDN request body exceeded the configured limit");
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
            _logger.LogError("Unexpected Google RTDN failure: {ErrorType}", ex.GetType().Name);
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
}
