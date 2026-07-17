using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;

public interface IGoogleOidcTokenVerifier
{
    Task<GoogleOidcClaims> VerifyAsync(string token, string expectedAudience, CancellationToken ct);
}

public sealed class GoogleOidcTokenVerifier : IGoogleOidcTokenVerifier
{
    public async Task<GoogleOidcClaims> VerifyAsync(
        string token,
        string expectedAudience,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { expectedAudience },
            IssuedAtClockTolerance = TimeSpan.FromMinutes(1),
            ExpirationTimeClockTolerance = TimeSpan.Zero
        };

        var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return new GoogleOidcClaims(
            payload.Email ?? string.Empty,
            payload.EmailVerified,
            payload.Subject ?? string.Empty,
            payload.Issuer ?? string.Empty);
    }
}

/// <summary>
/// Authenticates Pub/Sub push requests using Google's signed OIDC bearer token.
/// Pub/Sub must be configured with an exact audience and a dedicated service account.
/// </summary>
public sealed class GooglePubSubAuthenticator
{
    private const int MaxAuthorizationHeaderLength = 8192;

    private readonly GoogleRtdnConfig _config;
    private readonly IGoogleOidcTokenVerifier _tokenVerifier;
    private readonly ILogger<GooglePubSubAuthenticator> _logger;

    public GooglePubSubAuthenticator(
        GoogleRtdnConfig config,
        IGoogleOidcTokenVerifier tokenVerifier,
        ILogger<GooglePubSubAuthenticator> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tokenVerifier = tokenVerifier ?? throw new ArgumentNullException(nameof(tokenVerifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GooglePubSubAuthenticationResult> AuthenticateAsync(
        IEnumerable<string>? authorizationHeaders,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ExpectedAudience) ||
            string.IsNullOrWhiteSpace(_config.ExpectedServiceAccountEmail))
        {
            _logger.LogError("Google Pub/Sub OIDC authentication is not configured");
            return GooglePubSubAuthenticationResult.ConfigurationError();
        }

        var headers = authorizationHeaders?.ToArray() ?? Array.Empty<string>();
        if (headers.Length != 1)
        {
            return GooglePubSubAuthenticationResult.Unauthorized("MISSING_OR_AMBIGUOUS_AUTHORIZATION");
        }

        var header = headers[0];
        if (header.Length > MaxAuthorizationHeaderLength ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return GooglePubSubAuthenticationResult.Unauthorized("INVALID_AUTHORIZATION_SCHEME");
        }

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
        {
            return GooglePubSubAuthenticationResult.Unauthorized("INVALID_BEARER_TOKEN");
        }

        try
        {
            var claims = await _tokenVerifier
                .VerifyAsync(token, _config.ExpectedAudience, ct)
                .ConfigureAwait(false);

            if (!claims.EmailVerified ||
                !string.Equals(
                    claims.Email,
                    _config.ExpectedServiceAccountEmail,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GooglePubSubAuthenticationResult.Unauthorized("SERVICE_ACCOUNT_MISMATCH");
            }

            if (string.IsNullOrWhiteSpace(claims.Subject) ||
                !IsGoogleIssuer(claims.Issuer))
            {
                return GooglePubSubAuthenticationResult.Unauthorized("INVALID_TOKEN_IDENTITY");
            }

            return GooglePubSubAuthenticationResult.Success();
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning("Google Pub/Sub OIDC token rejected: {ErrorType}", ex.GetType().Name);
            return GooglePubSubAuthenticationResult.Unauthorized("INVALID_GOOGLE_TOKEN");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Google Pub/Sub OIDC validation unavailable: {ErrorType}", ex.GetType().Name);
            return GooglePubSubAuthenticationResult.Unavailable();
        }
    }

    private static bool IsGoogleIssuer(string issuer) =>
        string.Equals(issuer, "accounts.google.com", StringComparison.Ordinal) ||
        string.Equals(issuer, "https://accounts.google.com", StringComparison.Ordinal);
}

public sealed record GoogleOidcClaims(
    string Email,
    bool EmailVerified,
    string Subject,
    string Issuer);

public sealed class GooglePubSubAuthenticationResult
{
    private GooglePubSubAuthenticationResult(bool isAuthenticated, bool isUnavailable, string? errorCode)
    {
        IsAuthenticated = isAuthenticated;
        IsUnavailable = isUnavailable;
        ErrorCode = errorCode;
    }

    public bool IsAuthenticated { get; }
    public bool IsUnavailable { get; }
    public string? ErrorCode { get; }

    public static GooglePubSubAuthenticationResult Success() => new(true, false, null);
    public static GooglePubSubAuthenticationResult Unauthorized(string errorCode) => new(false, false, errorCode);
    public static GooglePubSubAuthenticationResult ConfigurationError() => new(false, true, "AUTH_NOT_CONFIGURED");
    public static GooglePubSubAuthenticationResult Unavailable() => new(false, true, "AUTH_VALIDATION_UNAVAILABLE");
}