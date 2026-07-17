using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Verification;

public interface IAppleJwsVerifier
{
    AppleJwsVerificationResult Verify(string compactJws);
}

/// <summary>
/// Verifies Apple's ES256 compact JWS payloads against explicitly configured Apple root CAs.
/// The operating-system trust store is intentionally not sufficient: the chain must terminate
/// at one of the configured roots.
/// </summary>
public sealed class AppleJwsVerifier : IAppleJwsVerifier
{
    private const int MaxCompactJwsLength = 1_048_576;
    private const int MaxDecodedPayloadLength = 524_288;
    private const string AppleAppStoreLeafCertificateOid = "1.2.840.113635.100.6.11.1";
    private const string AppleWorldwideDeveloperRelationsIntermediateOid = "1.2.840.113635.100.6.2.1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppleJwsVerificationOptions _options;
    private readonly ILogger<AppleJwsVerifier> _logger;
    private readonly IReadOnlyList<byte[]> _trustedRootCertificates;

    public AppleJwsVerifier(
        AppleJwsVerificationOptions options,
        ILogger<AppleJwsVerifier> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _trustedRootCertificates = ParseTrustedRoots(options.TrustedRootCertificatesBase64);
    }

    public AppleJwsVerificationResult Verify(string compactJws)
    {
        if (string.IsNullOrWhiteSpace(compactJws) || compactJws.Length > MaxCompactJwsLength)
        {
            return AppleJwsVerificationResult.Failure("INVALID_JWS_FORMAT");
        }

        var parts = compactJws.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return AppleJwsVerificationResult.Failure("INVALID_JWS_FORMAT");
        }

        try
        {
            var headerBytes = Base64UrlDecode(parts[0]);
            var header = JsonSerializer.Deserialize<AppleJwsHeader>(headerBytes, JsonOptions);
            if (header == null || !string.Equals(header.Algorithm, "ES256", StringComparison.Ordinal))
            {
                return AppleJwsVerificationResult.Failure("UNSUPPORTED_JWS_ALGORITHM");
            }

            if (header.CriticalHeaders is { Count: > 0 })
            {
                return AppleJwsVerificationResult.Failure("UNSUPPORTED_CRITICAL_HEADER");
            }

            if (header.CertificateChain == null || header.CertificateChain.Count != 3)
            {
                return AppleJwsVerificationResult.Failure("MISSING_CERTIFICATE_CHAIN");
            }

            if (_trustedRootCertificates.Count == 0)
            {
                _logger.LogError("Apple JWS verification has no configured trusted root certificate");
                return AppleJwsVerificationResult.Failure("MISSING_TRUST_ANCHOR");
            }

            using var certificates = LoadCertificateChain(header.CertificateChain);
            if (!ValidateCertificateChain(certificates.Certificates))
            {
                return AppleJwsVerificationResult.Failure("INVALID_CERTIFICATE_CHAIN");
            }

            if (!HasExtension(certificates.Certificates[0], AppleAppStoreLeafCertificateOid) ||
                !HasExtension(certificates.Certificates[1], AppleWorldwideDeveloperRelationsIntermediateOid))
            {
                return AppleJwsVerificationResult.Failure("INVALID_APPLE_CERTIFICATE_PROFILE");
            }

            var signature = Base64UrlDecode(parts[2]);
            if (signature.Length != 64)
            {
                return AppleJwsVerificationResult.Failure("INVALID_SIGNATURE_FORMAT");
            }

            using var publicKey = certificates.Certificates[0].GetECDsaPublicKey();
            if (publicKey == null)
            {
                return AppleJwsVerificationResult.Failure("INVALID_SIGNING_KEY");
            }

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signatureValid = publicKey.VerifyData(
                signingInput,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            if (!signatureValid)
            {
                return AppleJwsVerificationResult.Failure("INVALID_SIGNATURE");
            }

            var payload = Base64UrlDecode(parts[1]);
            if (payload.Length == 0 || payload.Length > MaxDecodedPayloadLength)
            {
                return AppleJwsVerificationResult.Failure("INVALID_PAYLOAD_SIZE");
            }

            return AppleJwsVerificationResult.Success(Encoding.UTF8.GetString(payload));
        }
        catch (FormatException)
        {
            return AppleJwsVerificationResult.Failure("INVALID_BASE64URL");
        }
        catch (JsonException)
        {
            return AppleJwsVerificationResult.Failure("INVALID_JWS_HEADER");
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning("Apple JWS cryptographic validation failed: {ErrorType}", ex.GetType().Name);
            return AppleJwsVerificationResult.Failure("CRYPTOGRAPHIC_VALIDATION_FAILED");
        }
    }

    private bool ValidateCertificateChain(IReadOnlyList<X509Certificate2> certificates)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.RevocationMode = _options.RevocationMode;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
        chain.ChainPolicy.DisableCertificateDownloads = false;

        for (var index = 1; index < certificates.Count; index++)
        {
            chain.ChainPolicy.ExtraStore.Add(certificates[index]);
        }

        foreach (var trustedRootBytes in _trustedRootCertificates)
        {
            chain.ChainPolicy.CustomTrustStore.Add(new X509Certificate2(trustedRootBytes));
        }

        try
        {
            if (!chain.Build(certificates[0]))
            {
                var statuses = string.Join(",", chain.ChainStatus.Select(status => status.Status));
                _logger.LogWarning("Apple JWS certificate chain rejected: {ChainStatuses}", statuses);
                return false;
            }

            var builtRoot = chain.ChainElements[^1].Certificate;
            return chain.ChainPolicy.CustomTrustStore
                .Cast<X509Certificate2>()
                .Any(root => CryptographicOperations.FixedTimeEquals(
                    root.GetCertHash(HashAlgorithmName.SHA256),
                    builtRoot.GetCertHash(HashAlgorithmName.SHA256)));
        }
        finally
        {
            foreach (var root in chain.ChainPolicy.CustomTrustStore)
            {
                root.Dispose();
            }
        }
    }

    private static bool HasExtension(X509Certificate2 certificate, string oid) =>
        certificate.Extensions
            .Cast<X509Extension>()
            .Any(extension => string.Equals(extension.Oid?.Value, oid, StringComparison.Ordinal));

    private static CertificateCollectionOwner LoadCertificateChain(IReadOnlyList<string> encodedCertificates)
    {
        var certificates = new List<X509Certificate2>(encodedCertificates.Count);
        try
        {
            foreach (var encodedCertificate in encodedCertificates)
            {
                certificates.Add(new X509Certificate2(Convert.FromBase64String(encodedCertificate)));
            }

            return new CertificateCollectionOwner(certificates);
        }
        catch
        {
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }

            throw;
        }
    }

    private static IReadOnlyList<byte[]> ParseTrustedRoots(string encodedRoots)
    {
        if (string.IsNullOrWhiteSpace(encodedRoots))
        {
            return Array.Empty<byte[]>();
        }

        return encodedRoots
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Convert.FromBase64String)
            .ToArray();
    }

    internal static byte[] Base64UrlDecode(string value)
    {
        if (value.Length % 4 == 1)
        {
            throw new FormatException("Invalid Base64Url length.");
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };

        return Convert.FromBase64String(padded);
    }

    private sealed class CertificateCollectionOwner : IDisposable
    {
        public CertificateCollectionOwner(IReadOnlyList<X509Certificate2> certificates) =>
            Certificates = certificates;

        public IReadOnlyList<X509Certificate2> Certificates { get; }

        public void Dispose()
        {
            foreach (var certificate in Certificates)
            {
                certificate.Dispose();
            }
        }
    }

    private sealed class AppleJwsHeader
    {
        [JsonPropertyName("alg")]
        public string? Algorithm { get; set; }

        [JsonPropertyName("x5c")]
        public List<string>? CertificateChain { get; set; }

        [JsonPropertyName("crit")]
        public List<string>? CriticalHeaders { get; set; }
    }
}

public sealed class AppleJwsVerificationOptions
{
    public string TrustedRootCertificatesBase64 { get; init; } = string.Empty;
    public X509RevocationMode RevocationMode { get; init; } = X509RevocationMode.Online;
}

public sealed class AppleJwsVerificationResult
{
    private AppleJwsVerificationResult(bool isValid, string? payload, string? errorCode)
    {
        IsValid = isValid;
        Payload = payload;
        ErrorCode = errorCode;
    }

    public bool IsValid { get; }
    public string? Payload { get; }
    public string? ErrorCode { get; }

    public static AppleJwsVerificationResult Success(string payload) => new(true, payload, null);
    public static AppleJwsVerificationResult Failure(string errorCode) => new(false, null, errorCode);
}