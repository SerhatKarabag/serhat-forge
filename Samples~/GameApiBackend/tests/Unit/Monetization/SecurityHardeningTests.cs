using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Persistence;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Serhat.Forge.CloudScript.Functions.Monetization;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class SecurityHardeningTests
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void MonetizationRequest_ProductionRawCallerSpoof_IsRejected()
    {
        var request = JsonSerializer.Serialize(new
        {
            payload = new { value = "receipt" },
            caller = new { playerId = "attacker-controlled" }
        });

        var result = Infrastructure.Security.PlayFabRequestSecurity.ParseEnvelope<TestPayload>(
            request,
            RequestJsonOptions,
            "ABCD",
            "Production",
            "VerifyPurchase");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsUnauthorized);
        Assert.Equal("PLAYFAB_CONTEXT_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void GameApiRequest_ProductionRawCallerSpoof_IsRejected()
    {
        var request = JsonSerializer.Serialize(new
        {
            payload = new { value = "state" },
            caller = new { playerId = "attacker-controlled" }
        });

        var result = Infrastructure.GameApiSecurity.GameApiPlayFabRequestSecurity
            .ParseEnvelope<TestPayload>(
                request,
                RequestJsonOptions,
                "ABCD",
                "Production",
                "SyncPlayerState");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsUnauthorized);
        Assert.Equal("PLAYFAB_CONTEXT_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void MonetizationRequest_TrustedWrapper_OverridesSpoofedCaller()
    {
        var request = CreatePlayFabWrapper(
            titleId: "ABCD",
            trustedPlayerId: "trusted-player",
            claimedPlayerId: "attacker-controlled");

        var result = Infrastructure.Security.PlayFabRequestSecurity.ParseEnvelope<TestPayload>(
            request,
            RequestJsonOptions,
            "ABCD",
            "Production",
            "VerifyPurchase");

        Assert.True(result.IsSuccess);
        Assert.Equal("trusted-player", result.Envelope!.Caller.PlayerId);
        Assert.Equal("VerifyPurchase", result.Envelope.FunctionName);
    }

    [Fact]
    public void MonetizationRequest_WrongTitleContext_IsRejected()
    {
        var request = CreatePlayFabWrapper(
            titleId: "WRONG",
            trustedPlayerId: "trusted-player",
            claimedPlayerId: "trusted-player");

        var result = Infrastructure.Security.PlayFabRequestSecurity.ParseEnvelope<TestPayload>(
            request,
            RequestJsonOptions,
            "ABCD",
            "Production",
            "VerifyPurchase");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsUnauthorized);
        Assert.Equal("INVALID_PLAYFAB_CONTEXT", result.ErrorCode);
    }

    [Fact]
    public void SubscriptionEntity_UsesTitlePartition_ForPointAndPlayerLookups()
    {
        var record = new Framework.Monetization.Domain.SubscriptionRecord
        {
            SubscriptionKey = "apple:original-transaction",
            PlayerId = "player-1"
        };

        var entity = Framework.Monetization.Persistence.SubscriptionEntity.FromRecord(
            record,
            "ABCD");

        Assert.Equal("ABCD", entity.PartitionKey);
    }

    [Fact]
    public void ProductionConfiguration_FakeVerifier_FailsFast()
    {
        var config = new MonetizationConfig
        {
            EnvironmentName = "Production",
            UseFakeVerifier = true
        };

        var exception = Assert.Throws<InvalidOperationException>(config.ValidateForStartup);
        Assert.Contains("USE_FAKE_VERIFIER", exception.Message);
    }

    [Fact]
    public void DevelopmentFakeVerifier_ResolvesRtdnWithDisabledAuthoritativeProvider()
    {
        var config = new MonetizationConfig
        {
            EnvironmentName = "Development",
            UseFakeVerifier = true,
            Google = new GoogleStoreConfig { RequireObfuscatedAccountId = false }
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMonetization(config);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<DisabledGooglePlaySubscriptionSnapshotProvider>(
            provider.GetRequiredService<IGooglePlaySubscriptionSnapshotProvider>());
        Assert.NotNull(provider.GetRequiredService<GoogleRtdnReconciliationService>());
    }

    [Fact]
    public async Task DisabledAppleStore_DiVerifierFailsClosed()
    {
        var product = CreateValidProduct();
        var config = CreateDevelopmentConfig(product.ProductId, product);
        config.UseFakeVerifier = true;
        config.Apple.Enabled = false;
        config.Google.Enabled = true;
        config.Google.RequireObfuscatedAccountId = false;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMonetization(config);
        using var provider = services.BuildServiceProvider();

        var result = await provider
            .GetRequiredService<PurchaseVerificationService>()
            .VerifyAndGrantAsync(new VerifyPurchaseServiceRequest
            {
                PlayerId = "player-1",
                Platform = Platform.Apple,
                ProductId = product.ProductId,
                TransactionId = "apple-disabled-transaction",
                ReceiptPayload = string.Empty
            });

        Assert.False(result.Success);
        Assert.Equal("STORE_DISABLED", result.ErrorCode);
    }

    [Fact]
    public void ProductionConfiguration_AppleSignatureBypass_FailsFast()
    {
        var config = new MonetizationConfig
        {
            EnvironmentName = "Production",
            Apple = new AppleStoreConfig { SkipSignatureValidation = true }
        };

        var exception = Assert.Throws<InvalidOperationException>(config.ValidateForStartup);
        Assert.Contains("APPLE_SKIP_SIGNATURE_VALIDATION", exception.Message);
    }

    [Fact]
    public void ProductionConfiguration_AppleAccountBindingDisabled_FailsFast()
    {
        var config = new MonetizationConfig
        {
            EnvironmentName = "Production",
            Apple = new AppleStoreConfig { RequireAppAccountToken = false }
        };

        var exception = Assert.Throws<InvalidOperationException>(config.ValidateForStartup);
        Assert.Contains("APPLE_REQUIRE_APP_ACCOUNT_TOKEN", exception.Message);
    }

    [Fact]
    public void ProductionConfiguration_GoogleOnly_DoesNotRequireAppleSecrets()
    {
        var config = CreateProductionConfig();
        config.Apple = new AppleStoreConfig { Enabled = false };

        config.ValidateForStartup();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMonetization(config);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<GooglePlayStoreVerifier>(
            provider.GetRequiredService<IGooglePlaySubscriptionSnapshotProvider>());
    }

    [Fact]
    public void ProductionConfiguration_AppleOnly_DoesNotRequireGoogleSecrets()
    {
        using var certificates = TestCertificateChain.Create();
        var config = CreateProductionConfig();
        config.Apple.TrustedRootCertificatesBase64 =
            Convert.ToBase64String(certificates.Root.RawData);
        config.Google = new GoogleStoreConfig { Enabled = false };

        config.ValidateForStartup();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMonetization(config);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DisabledGooglePlaySubscriptionSnapshotProvider>(
            provider.GetRequiredService<IGooglePlaySubscriptionSnapshotProvider>());
    }

    [Fact]
    public void ProductionConfiguration_AllStoresDisabled_FailsFast()
    {
        var config = CreateProductionConfig();
        config.Apple.Enabled = false;
        config.Google.Enabled = false;

        var exception = Assert.Throws<InvalidOperationException>(config.ValidateForStartup);

        Assert.Contains("At least one store", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppleVerifierConfiguration_MapsApplicationAndAccountBindingPolicy()
    {
        var config = new MonetizationConfig
        {
            Apple = new AppleStoreConfig
            {
                AppAppleId = 123_456,
                RequireAppAccountToken = false
            }
        };

        var verifierConfig = config.ToAppleVerifierConfig();

        Assert.Equal(123_456, verifierConfig.AppAppleId);
        Assert.False(verifierConfig.RequireAppAccountToken);
    }

    [Fact]
    public void VerifyPurchaseTransport_GoogleWithoutTransactionId_IsValid()
    {
        var error = VerifyPurchaseFunction.ValidateRequiredFields(new VerifyPurchaseRequestDto
        {
            Platform = "google",
            ProductId = "coins_100",
            ReceiptPayload = "purchase-token"
        });

        Assert.Null(error);
    }

    [Fact]
    public void VerifyPurchaseTransport_AppleWithoutTransactionId_IsRejected()
    {
        var error = VerifyPurchaseFunction.ValidateRequiredFields(new VerifyPurchaseRequestDto
        {
            Platform = "apple",
            ProductId = "coins_100",
            ReceiptPayload = "signed-transaction"
        });

        Assert.NotNull(error);
        Assert.Contains("TransactionId", error);
    }

    [Fact]
    public void VerifyPurchaseTransport_AppleTransactionWithoutReceiptPayload_IsValid()
    {
        var error = VerifyPurchaseFunction.ValidateRequiredFields(new VerifyPurchaseRequestDto
        {
            Platform = "apple",
            ProductId = "remove_ads",
            TransactionId = "apple-transaction-1",
            ReceiptPayload = string.Empty
        });

        Assert.Null(error);
    }

    [Fact]
    public void VerifyPurchaseTransport_UnknownPlatform_ReachesServiceValidation()
    {
        var error = VerifyPurchaseFunction.ValidateRequiredFields(new VerifyPurchaseRequestDto
        {
            Platform = "unknown-store",
            ProductId = "coins_100",
            ReceiptPayload = "opaque-receipt"
        });

        Assert.Null(error);
    }

    [Fact]
    public void ProductAllowlist_DictionaryKeyMismatch_FailsAtStartup()
    {
        AssertInvalidProduct(
            CreateValidProduct(),
            "different-key",
            "must exactly match productId");
    }

    [Fact]
    public void ProductAllowlist_DuplicateEconomyItem_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.EconomyItemIds.Add("currency_coins");

        AssertInvalidProduct(product, product.ProductId, "duplicate Economy item ID");
    }

    [Fact]
    public void ProductAllowlist_InvalidConsumableQuantity_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.Quantity = 0;

        AssertInvalidProduct(product, product.ProductId, "quantity must be between");
    }

    [Fact]
    public void ProductAllowlist_SubscriptionWithoutTier_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.Type = ProductType.Subscription;
        product.Quantity = 1;
        product.TierKey = null;

        AssertInvalidProduct(product, product.ProductId, "tierKey is required");
    }

    [Fact]
    public void ProductAllowlist_NonSubscriptionTierFields_FailAtStartup()
    {
        var product = CreateValidProduct();
        product.TierKey = "forged-tier";
        product.TierPrecedence = 1;

        AssertInvalidProduct(product, product.ProductId, "cannot define tierKey");
    }

    [Fact]
    public void ProductAllowlist_OverlongEconomyItemId_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.EconomyItemIds[0] = new string('i', 257);

        AssertInvalidProduct(product, product.ProductId, "overlong Economy item ID");
    }

    [Fact]
    public void ProductAllowlist_ExcessiveGrantMetadata_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.GrantMetadata = Enumerable.Range(0, 17).ToDictionary(
            index => $"key-{index}",
            _ => "value",
            StringComparer.Ordinal);

        AssertInvalidProduct(product, product.ProductId, "more than 16 entries");
    }

    [Fact]
    public void ProductAllowlist_OverlongGrantMetadataValue_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.GrantMetadata = new Dictionary<string, string>
        {
            ["source"] = new string('v', 513)
        };

        AssertInvalidProduct(product, product.ProductId, "exceeds 512 characters");
    }

    [Fact]
    public void ProductAllowlist_OverlongGrantMetadataKey_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.GrantMetadata = new Dictionary<string, string>
        {
            [new string('k', 65)] = "value"
        };

        AssertInvalidProduct(product, product.ProductId, "overlong grantMetadata key");
    }

    [Fact]
    public void ProductAllowlist_GrantMetadataTotalSize_FailsAtStartup()
    {
        var product = CreateValidProduct();
        product.GrantMetadata = Enumerable.Range(0, 9).ToDictionary(
            index => $"key-{index}",
            _ => new string('v', 500),
            StringComparer.Ordinal);

        AssertInvalidProduct(product, product.ProductId, "exceeds 4096 UTF-8 bytes");
    }

    [Fact]
    public void ProductAllowlist_ValidServerGrantMetadata_PassesDevelopmentStartup()
    {
        var product = CreateValidProduct();
        product.GrantMetadata = new Dictionary<string, string>
        {
            ["source"] = "server-catalog"
        };
        var config = CreateDevelopmentConfig(product.ProductId, product);

        config.ValidateForStartup();
    }

    [Fact]
    public void ProductAllowlist_JsonStringEnumAndServerMetadata_ParseAsDocumented()
    {
        const string json = """
            {
              "products": {
                "coins_100": {
                  "productId": "coins_100",
                  "type": "Consumable",
                  "economyItemIds": ["currency_coins"],
                  "quantity": 100,
                  "grantMetadata": { "source": "iap" },
                  "enabled": true
                }
              }
            }
            """;

        var allowlist = MonetizationConfig.ParseProductAllowlistJson(json);
        var product = allowlist.Products["coins_100"];

        Assert.Equal(ProductType.Consumable, product.Type);
        Assert.Equal("iap", product.GrantMetadata!["source"]);
    }

    [Fact]
    public void AppleParser_ProductionSignatureBypass_Throws()
    {
        var config = new AppleNotificationConfig
        {
            SkipSignatureValidation = true,
            HostEnvironmentName = "Production"
        };

        Assert.Throws<InvalidOperationException>(() =>
            new AppleNotificationParser(
                config,
                Mock.Of<ILogger<AppleNotificationParser>>()));
    }

    [Fact]
    public void AppleJwsVerifier_ValidChainAndSignature_AcceptsPayload()
    {
        using var certificates = TestCertificateChain.Create();
        var verifier = CreateAppleVerifier(certificates.Root);
        var payload = JsonSerializer.Serialize(new { signedDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var jws = certificates.Sign(payload);

        var result = verifier.Verify(jws);

        Assert.True(result.IsValid, result.ErrorCode);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void AppleJwsVerifier_TamperedSignature_RejectsPayload()
    {
        using var certificates = TestCertificateChain.Create();
        var verifier = CreateAppleVerifier(certificates.Root);
        var jws = certificates.Sign("{\"value\":1}");
        var parts = jws.Split('.');
        var signature = Base64UrlDecode(parts[2]);
        signature[^1] ^= 0x01;
        var tampered = $"{parts[0]}.{parts[1]}.{Base64UrlEncode(signature)}";

        var result = verifier.Verify(tampered);

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_SIGNATURE", result.ErrorCode);
    }

    [Fact]
    public void AppleJwsVerifier_UntrustedRoot_RejectsPayload()
    {
        using var signingChain = TestCertificateChain.Create();
        using var unrelatedChain = TestCertificateChain.Create();
        var verifier = CreateAppleVerifier(unrelatedChain.Root);

        var result = verifier.Verify(signingChain.Sign("{\"value\":1}"));

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_CERTIFICATE_CHAIN", result.ErrorCode);
    }

    [Fact]
    public void AppleJwsVerifier_MissingAppleCertificateProfile_RejectsPayload()
    {
        using var certificates = TestCertificateChain.Create(includeAppleProfile: false);
        var verifier = CreateAppleVerifier(certificates.Root);

        var result = verifier.Verify(certificates.Sign("{\"value\":1}"));

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_APPLE_CERTIFICATE_PROFILE", result.ErrorCode);
    }
    [Fact]
    public async Task GoogleAuthenticator_MissingBearerToken_RejectsRequest()
    {
        var verifier = new Mock<IGoogleOidcTokenVerifier>(MockBehavior.Strict);
        var authenticator = CreateGoogleAuthenticator(verifier.Object);

        var result = await authenticator.AuthenticateAsync(null);

        Assert.False(result.IsAuthenticated);
        Assert.False(result.IsUnavailable);
        Assert.Equal("MISSING_OR_AMBIGUOUS_AUTHORIZATION", result.ErrorCode);
    }

    [Fact]
    public async Task GoogleAuthenticator_WrongServiceAccount_RejectsRequest()
    {
        var verifier = new Mock<IGoogleOidcTokenVerifier>();
        verifier
            .Setup(value => value.VerifyAsync(
                "signed-token",
                "https://example.test/webhooks/google",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleOidcClaims(
                "attacker@example.test",
                true,
                "subject",
                "https://accounts.google.com"));
        var authenticator = CreateGoogleAuthenticator(verifier.Object);

        var result = await authenticator.AuthenticateAsync(new[] { "Bearer signed-token" });

        Assert.False(result.IsAuthenticated);
        Assert.Equal("SERVICE_ACCOUNT_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task WebhookClaim_IsAtomicAndReplaySafe()
    {
        var repository = new InMemoryPurchaseRepository();
        var claims = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => repository.TryBeginWebhookProcessingAsync("provider-event-1")));

        Assert.Single(claims, claimed => claimed);
        await repository.CompleteWebhookProcessingAsync("provider-event-1");
        Assert.False(await repository.TryBeginWebhookProcessingAsync("provider-event-1"));

        Assert.True(await repository.TryBeginWebhookProcessingAsync("provider-event-2"));
        await repository.AbandonWebhookProcessingAsync("provider-event-2");
        Assert.True(await repository.TryBeginWebhookProcessingAsync("provider-event-2"));
    }

    private static ProductConfig CreateValidProduct() => new()
    {
        ProductId = "coins_100",
        Type = ProductType.Consumable,
        EconomyItemIds = new List<string> { "currency_coins" },
        Quantity = 100,
        Enabled = true
    };

    private static MonetizationConfig CreateDevelopmentConfig(
        string dictionaryKey,
        ProductConfig product) =>
        new()
        {
            EnvironmentName = "Development",
            Products = new ProductAllowlistConfig
            {
                Products = new Dictionary<string, ProductConfig>
                {
                    [dictionaryKey] = product
                }
            }
        };

    private static MonetizationConfig CreateProductionConfig()
    {
        var product = CreateValidProduct();
        return new MonetizationConfig
        {
            EnvironmentName = "Production",
            StorageConnectionString =
                "DefaultEndpointsProtocol=https;AccountName=forge;AccountKey=c2VjcmV0;EndpointSuffix=core.windows.net",
            PlayFabTitleId = "ABCD",
            PlayFabSecretKey = "playfab-secret",
            Apple = new AppleStoreConfig
            {
                Enabled = true,
                BundleId = "com.serhat.forge",
                AppAppleId = 123456,
                IssuerId = "issuer",
                KeyId = "key",
                PrivateKeyBase64 = "private-key",
                TrustedRootCertificatesBase64 = "root-ca",
                RequireAppAccountToken = true,
                CertificateRevocationMode = "Online"
            },
            Google = new GoogleStoreConfig
            {
                Enabled = true,
                PackageName = "com.serhat.forge",
                ServiceAccountEmail = "service@example.test",
                PrivateKeyBase64 = "private-key",
                PubSubAudience = "https://example.test/webhooks/google",
                PubSubServiceAccountEmail = "pubsub@example.test",
                RequireObfuscatedAccountId = true
            },
            Products = new ProductAllowlistConfig
            {
                Products = new Dictionary<string, ProductConfig>
                {
                    [product.ProductId] = product
                }
            }
        };
    }

    private static void AssertInvalidProduct(
        ProductConfig product,
        string dictionaryKey,
        string expectedMessage)
    {
        var config = CreateDevelopmentConfig(dictionaryKey, product);
        var exception = Assert.Throws<InvalidOperationException>(config.ValidateForStartup);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static AppleJwsVerifier CreateAppleVerifier(X509Certificate2 root) =>
        new(
            new AppleJwsVerificationOptions
            {
                TrustedRootCertificatesBase64 = Convert.ToBase64String(root.RawData),
                RevocationMode = X509RevocationMode.NoCheck
            },
            Mock.Of<ILogger<AppleJwsVerifier>>());

    private static GooglePubSubAuthenticator CreateGoogleAuthenticator(
        IGoogleOidcTokenVerifier verifier) =>
        new(
            new GoogleRtdnConfig
            {
                ExpectedAudience = "https://example.test/webhooks/google",
                ExpectedServiceAccountEmail = "pubsub@example.test"
            },
            verifier,
            Mock.Of<ILogger<GooglePubSubAuthenticator>>());

    private static string CreatePlayFabWrapper(
        string titleId,
        string trustedPlayerId,
        string claimedPlayerId) =>
        JsonSerializer.Serialize(new
        {
            functionArgument = new
            {
                payload = new { value = "payload" },
                caller = new { playerId = claimedPlayerId }
            },
            callerEntityProfile = new
            {
                lineage = new { titlePlayerAccountId = trustedPlayerId }
            },
            titleAuthenticationContext = new { id = titleId }
        });

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        return Convert.FromBase64String(padded);
    }

    private sealed class TestPayload
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class TestCertificateChain : IDisposable
    {
        private const string AppleLeafOid = "1.2.840.113635.100.6.11.1";
        private const string AppleIntermediateOid = "1.2.840.113635.100.6.2.1";

        private readonly ECDsa _rootKey;
        private readonly ECDsa _intermediateKey;
        private readonly ECDsa _leafKey;

        private TestCertificateChain(
            ECDsa rootKey,
            ECDsa intermediateKey,
            ECDsa leafKey,
            X509Certificate2 root,
            X509Certificate2 intermediate,
            X509Certificate2 leaf)
        {
            _rootKey = rootKey;
            _intermediateKey = intermediateKey;
            _leafKey = leafKey;
            Root = root;
            Intermediate = intermediate;
            Leaf = leaf;
        }

        public X509Certificate2 Root { get; }
        public X509Certificate2 Intermediate { get; }
        public X509Certificate2 Leaf { get; }

        public static TestCertificateChain Create(bool includeAppleProfile = true)
        {
            var now = DateTimeOffset.UtcNow;
            var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest(
                "CN=Serhat Forge Test Root",
                rootKey,
                HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, true, 1, true));
            rootRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));
            var root = rootRequest.CreateSelfSigned(now.AddHours(-1), now.AddDays(2));

            var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var intermediateRequest = new CertificateRequest(
                "CN=Serhat Forge Test Intermediate",
                intermediateKey,
                HashAlgorithmName.SHA256);
            intermediateRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, true, 0, true));
            intermediateRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));
            if (includeAppleProfile)
            {
                intermediateRequest.CertificateExtensions.Add(
                    new X509Extension(AppleIntermediateOid, new byte[] { 0x05, 0x00 }, false));
            }

            var publicIntermediate = intermediateRequest.Create(
                root,
                now.AddHours(-1),
                now.AddDays(2),
                RandomNumberGenerator.GetBytes(16));
            var intermediate = publicIntermediate.CopyWithPrivateKey(intermediateKey);
            publicIntermediate.Dispose();

            var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var leafRequest = new CertificateRequest(
                "CN=Serhat Forge Test Leaf",
                leafKey,
                HashAlgorithmName.SHA256);
            leafRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));
            leafRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            if (includeAppleProfile)
            {
                leafRequest.CertificateExtensions.Add(
                    new X509Extension(AppleLeafOid, new byte[] { 0x05, 0x00 }, false));
            }

            var publicLeaf = leafRequest.Create(
                intermediate,
                now.AddHours(-1),
                now.AddDays(1),
                RandomNumberGenerator.GetBytes(16));
            var leaf = publicLeaf.CopyWithPrivateKey(leafKey);
            publicLeaf.Dispose();

            return new TestCertificateChain(
                rootKey,
                intermediateKey,
                leafKey,
                root,
                intermediate,
                leaf);
        }

        public string Sign(string payload)
        {
            var header = JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "ES256",
                x5c = new[]
                {
                    Convert.ToBase64String(Leaf.RawData),
                    Convert.ToBase64String(Intermediate.RawData),
                    Convert.ToBase64String(Root.RawData)
                }
            });
            var headerPart = Base64UrlEncode(header);
            var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            var signingInput = $"{headerPart}.{payloadPart}";
            var signature = _leafKey.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{signingInput}.{Base64UrlEncode(signature)}";
        }

        public void Dispose()
        {
            Leaf.Dispose();
            Intermediate.Dispose();
            Root.Dispose();
            _leafKey.Dispose();
            _intermediateKey.Dispose();
            _rootKey.Dispose();
        }
    }
}
