using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Persistence;
using Serhat.Forge.CloudScript.Framework.Monetization.PlayFab;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Configuration;

/// <summary>
/// Extension methods for configuring monetization services.
/// </summary>
public static class MonetizationServiceExtensions
{
    /// <summary>
    /// Adds monetization services to the service collection.
    /// </summary>
    public static IServiceCollection AddMonetization(this IServiceCollection services)
    {
        // Load configuration
        var config = MonetizationConfig.LoadFromEnvironment();
        return AddMonetization(services, config);
    }

    /// <summary>
    /// Adds monetization services with a custom configuration.
    /// </summary>
    public static IServiceCollection AddMonetization(
        this IServiceCollection services,
        MonetizationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.ValidateForStartup();

        services.AddSingleton(config);
        services.AddSingleton(config.Products);

        // Repository
        if (string.IsNullOrEmpty(config.StorageConnectionString))
        {
            // Use in-memory for local development
            services.AddSingleton<IPurchaseRepository, InMemoryPurchaseRepository>();
        }
        else
        {
            services.AddSingleton<IPurchaseRepository>(sp =>
                new TableStoragePurchaseRepository(
                    config.StorageConnectionString,
                    config.PlayFabTitleId,
                    sp.GetRequiredService<ILogger<TableStoragePurchaseRepository>>()));
        }

        // Apple signed-data verification is shared by receipt and webhook validation.
        services.AddSingleton<IAppleJwsVerifier>(sp =>
        {
            if (!Enum.TryParse<X509RevocationMode>(
                    config.Apple.CertificateRevocationMode,
                    true,
                    out var revocationMode))
            {
                throw new InvalidOperationException("Invalid Apple certificate revocation mode.");
            }

            return new AppleJwsVerifier(
                new AppleJwsVerificationOptions
                {
                    TrustedRootCertificatesBase64 = config.Apple.TrustedRootCertificatesBase64,
                    RevocationMode = revocationMode
                },
                sp.GetRequiredService<ILogger<AppleJwsVerifier>>());
        });

        // Store verifiers
        if (config.UseFakeVerifier)
        {
            services.AddSingleton<FakeStoreVerifier>();
        }
        else
        {
            services.AddSingleton(sp =>
                new AppleStoreVerifier(
                    config.ToAppleVerifierConfig(),
                    sp.GetRequiredService<ILogger<AppleStoreVerifier>>(),
                    jwsVerifier: sp.GetRequiredService<IAppleJwsVerifier>()));

            services.AddSingleton(sp =>
                new GooglePlayStoreVerifier(
                    config.ToGoogleVerifierConfig(),
                    sp.GetRequiredService<ILogger<GooglePlayStoreVerifier>>()));
        }

        // Entitlement granter
        services.AddSingleton<IEntitlementGranter>(sp =>
            new PlayFabEconomyV2Granter(
                config.PlayFabTitleId,
                config.PlayFabSecretKey,
                sp.GetRequiredService<ILogger<PlayFabEconomyV2Granter>>()));

        // Services
        services.AddSingleton<PurchaseVerificationService>(sp =>
        {
            IStoreVerifier appleVerifier;
            IStoreVerifier googleVerifier;

            if (config.UseFakeVerifier)
            {
                var fake = sp.GetRequiredService<FakeStoreVerifier>();
                appleVerifier = fake;
                googleVerifier = fake;
            }
            else
            {
                appleVerifier = sp.GetRequiredService<AppleStoreVerifier>();
                googleVerifier = sp.GetRequiredService<GooglePlayStoreVerifier>();
            }

            return new PurchaseVerificationService(
                appleVerifier,
                googleVerifier,
                sp.GetRequiredService<IPurchaseRepository>(),
                sp.GetRequiredService<IEntitlementGranter>(),
                config.Products,
                sp.GetRequiredService<ILogger<PurchaseVerificationService>>());
        });

        services.AddSingleton<SubscriptionLifecycleService>(sp =>
            new SubscriptionLifecycleService(
                sp.GetRequiredService<IPurchaseRepository>(),
                sp.GetRequiredService<IEntitlementGranter>(),
                config.Products,
                sp.GetRequiredService<ILogger<SubscriptionLifecycleService>>()));

        // Webhook parsers
        services.AddSingleton(sp =>
            new AppleNotificationParser(
                config.ToAppleNotificationConfig(),
                sp.GetRequiredService<IAppleJwsVerifier>(),
                sp.GetRequiredService<ILogger<AppleNotificationParser>>()));

        var googleRtdnConfig = config.ToGoogleRtdnConfig();
        services.AddSingleton(googleRtdnConfig);
        services.AddSingleton<IGoogleOidcTokenVerifier, GoogleOidcTokenVerifier>();
        services.AddSingleton<GooglePubSubAuthenticator>();
        services.AddSingleton(sp =>
            new GoogleRtdnParser(
                googleRtdnConfig,
                sp.GetRequiredService<ILogger<GoogleRtdnParser>>()));

        return services;
    }
}
