using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public class WebhookParserTests
{
    #region Apple Notification Parser Tests

    [Fact]
    public void AppleNotificationParser_ValidNotification_ParsesSuccessfully()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<AppleNotificationParser>>();
        var config = new AppleNotificationConfig
        {
            BundleId = "com.test.app",
            SkipSignatureValidation = true,
            HostEnvironmentName = "Development"
        };
        var parser = new AppleNotificationParser(config, loggerMock.Object);

        // Create a fake JWS payload (base64 encoded)
        var payloadJson = JsonSerializer.Serialize(new
        {
            notificationType = "DID_RENEW",
            subtype = (string?)null,
            notificationUUID = "test-uuid-001",
            data = new
            {
                bundleId = "com.test.app",
                environment = "Production",
                signedTransactionInfo = CreateFakeJws(new
                {
                    transactionId = "txn_001",
                    originalTransactionId = "orig_txn_001",
                    productId = "premium_monthly",
                    bundleId = "com.test.app",
                    environment = "Production",
                    purchaseDate = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds(),
                    expiresDate = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds()
                }),
                signedRenewalInfo = CreateFakeJws(new
                {
                    originalTransactionId = "orig_txn_001",
                    environment = "Production",
                    autoRenewStatus = 1
                })
            }
        });

        var requestBody = JsonSerializer.Serialize(new
        {
            signedPayload = CreateFakeJws(payloadJson, raw: true)
        });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Event);
        Assert.Equal(WebhookEventType.Renewed, result.Event.EventType);
        Assert.Equal(Platform.Apple, result.Event.Platform);
        Assert.Contains("orig_txn_001", result.Event.SubscriptionKey);
    }

    [Fact]
    public void AppleNotificationParser_FullOneTimeRefund_MapsSignedReconciliationData()
    {
        var parser = CreateDevelopmentAppleParser();
        var revocationDate = DateTimeOffset.UtcNow.AddMinutes(-1);
        var requestBody = CreateAppleNotification(
            "REFUND",
            new
            {
                transactionId = "one-time-transaction",
                originalTransactionId = "one-time-transaction",
                productId = "remove_ads",
                bundleId = "com.test.app",
                environment = "Production",
                type = "Non-Consumable",
                quantity = 1,
                revocationDate = revocationDate.ToUnixTimeMilliseconds(),
                revocationType = "REFUND_FULL",
                revocationPercentage = 100_000
            });

        var result = parser.Parse(requestBody);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Event);
        Assert.Equal(WebhookEventType.Refunded, result.Event.EventType);
        Assert.False(result.Event.IsSubscription);
        Assert.True(result.Event.IsFullRefund);
        Assert.Null(result.Event.SubscriptionKey);
        Assert.Equal("one-time-transaction", result.Event.TransactionId);
        Assert.Equal("REFUND_FULL", result.Event.RevocationType);
        Assert.Equal(100_000, result.Event.RevocationPercentage);
        Assert.Equal(revocationDate.ToUnixTimeMilliseconds(),
            new DateTimeOffset(result.Event.EventTimestampUtc).ToUnixTimeMilliseconds());
        Assert.StartsWith("apple-refund:", result.Event.EntitlementOperationId, StringComparison.Ordinal);
    }

    [Fact]
    public void AppleNotificationParser_ProratedSubscriptionRefund_PreservesSubscriptionIdentity()
    {
        var parser = CreateDevelopmentAppleParser();
        var requestBody = CreateAppleNotification(
            "REFUND",
            new
            {
                transactionId = "renewal-transaction",
                originalTransactionId = "original-transaction",
                productId = "premium_monthly",
                bundleId = "com.test.app",
                environment = "Production",
                type = "Auto-Renewable Subscription",
                quantity = 1,
                revocationDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                revocationType = "REFUND_PRORATED",
                revocationPercentage = 50_000
            });

        var result = parser.Parse(requestBody);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Event);
        Assert.True(result.Event.IsSubscription);
        Assert.False(result.Event.IsFullRefund);
        Assert.Equal("apple:original-transaction", result.Event.SubscriptionKey);
        Assert.Equal("REFUND_PRORATED", result.Event.RevocationType);
        Assert.Equal(50_000, result.Event.RevocationPercentage);
    }

    [Fact]
    public void AppleNotificationParser_RefundWithoutTransaction_FailsClosed()
    {
        var parser = CreateDevelopmentAppleParser();
        var payload = JsonSerializer.Serialize(new
        {
            notificationType = "REFUND",
            notificationUUID = "refund-without-transaction",
            data = new
            {
                bundleId = "com.test.app",
                environment = "Production"
            }
        });
        var requestBody = JsonSerializer.Serialize(new
        {
            signedPayload = CreateFakeJws(payload, raw: true)
        });

        var result = parser.Parse(requestBody);

        Assert.False(result.IsSuccess);
        Assert.Equal("MISSING_REFUND_IDENTITY", result.ErrorCode);
    }

    [Fact]
    public void AppleNotificationParser_InvalidJson_ReturnsFailure()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<AppleNotificationParser>>();
        var config = new AppleNotificationConfig
        {
            SkipSignatureValidation = true,
            HostEnvironmentName = "Development"
        };
        var parser = new AppleNotificationParser(config, loggerMock.Object);

        // Act
        var result = parser.Parse("not valid json");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_JSON", result.ErrorCode);
    }

    [Fact]
    public void AppleNotificationParser_MissingPayload_ReturnsFailure()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<AppleNotificationParser>>();
        var config = new AppleNotificationConfig
        {
            SkipSignatureValidation = true,
            HostEnvironmentName = "Development"
        };
        var parser = new AppleNotificationParser(config, loggerMock.Object);

        var requestBody = JsonSerializer.Serialize(new { signedPayload = (string?)null });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_FORMAT", result.ErrorCode);
    }

    #endregion

    #region Google RTDN Parser Tests

    [Fact]
    public void GoogleRtdnParser_ValidSubscriptionNotification_ParsesSuccessfully()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig { ExpectedPackageName = "com.test.app" };
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var notificationData = JsonSerializer.Serialize(new
        {
            version = "1.0",
            packageName = "com.test.app",
            eventTimeMillis = DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds()
                .ToString(CultureInfo.InvariantCulture),
            subscriptionNotification = new
            {
                version = "1.0",
                notificationType = 4, // SUBSCRIPTION_PURCHASED
                purchaseToken = "purchase_token_001"
            }
        });

        var requestBody = JsonSerializer.Serialize(new
        {
            message = new
            {
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(notificationData)),
                messageId = "msg_001",
                publishTime = DateTime.UtcNow.ToString("o")
            },
            subscription = "projects/test/subscriptions/rtdn"
        });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsTestNotification);
        Assert.NotNull(result.Notification);
        Assert.Equal(
            GoogleRtdnNotificationKind.SubscriptionStateChanged,
            result.Notification.Kind);
        Assert.Equal(4, result.Notification.NotificationType);
        Assert.Null(result.Notification.ProductIdHint);
    }

    [Fact]
    public void GoogleRtdnParser_TestNotification_ReturnsTestResult()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig { ExpectedPackageName = "com.test.app" };
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var notificationData = JsonSerializer.Serialize(new
        {
            version = "1.0",
            packageName = "com.test.app",
            eventTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            testNotification = new
            {
                version = "1.0"
            }
        });

        var requestBody = JsonSerializer.Serialize(new
        {
            message = new
            {
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(notificationData)),
                messageId = "msg_test_001",
                publishTime = DateTime.UtcNow.ToString("o")
            }
        });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.IsTestNotification);
        Assert.Equal("1.0", result.TestVersion);
        Assert.Null(result.Notification);
    }

    [Fact]
    public void GoogleRtdnParser_VoidedPurchase_ParsesAsRefund()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig { ExpectedPackageName = "com.test.app" };
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var notificationData = JsonSerializer.Serialize(new
        {
            version = "1.0",
            packageName = "com.test.app",
            eventTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            voidedPurchaseNotification = new
            {
                purchaseToken = "purchase_token_voided",
                orderId = "order_001",
                productType = 1, // Subscription
                refundType = 1, // Full refund
                productId = "premium_monthly"
            }
        });

        var requestBody = JsonSerializer.Serialize(new
        {
            message = new
            {
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(notificationData)),
                messageId = "msg_voided_001",
                publishTime = DateTime.UtcNow.ToString("o")
            }
        });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Notification);
        Assert.Equal(GoogleRtdnNotificationKind.VoidedPurchase, result.Notification.Kind);
        Assert.Equal(1, result.Notification.RefundType);
    }

    [Fact]
    public void GoogleRtdnParser_PackageMismatch_ReturnsFailure()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig { ExpectedPackageName = "com.test.app" };
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var notificationData = JsonSerializer.Serialize(new
        {
            version = "1.0",
            packageName = "com.other.app", // Different package
            eventTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            subscriptionNotification = new
            {
                notificationType = 4,
                purchaseToken = "token",
                subscriptionId = "product"
            }
        });

        var requestBody = JsonSerializer.Serialize(new
        {
            message = new
            {
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(notificationData)),
                messageId = "msg_wrong_pkg",
                publishTime = DateTime.UtcNow.ToString("o")
            }
        });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("PACKAGE_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public void GoogleRtdnParser_InvalidBase64_ReturnsFailure()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig();
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var requestBody = JsonSerializer.Serialize(new
        {
            message = new
            {
                data = "not-valid-base64!!!",
                messageId = "msg_invalid",
                publishTime = DateTime.UtcNow.ToString("o")
            }
        });

        // Act
        var result = parser.Parse(requestBody);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_BASE64", result.ErrorCode);
    }

    [Fact]
    public void GoogleRtdnParser_SubscriptionNotificationType_IsOnlyPreservedAsHint()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig { ExpectedPackageName = "com.test.app" };
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var notificationTypes = new[] { 1, 2, 3, 4, 5, 6, 7, 9, 10, 12, 13 };

        foreach (var notificationType in notificationTypes)
        {
            var notificationData = JsonSerializer.Serialize(new
            {
                version = "1.0",
                packageName = "com.test.app",
                eventTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                subscriptionNotification = new
                {
                    notificationType,
                    purchaseToken = "token",
                    subscriptionId = "product"
                }
            });

            var requestBody = JsonSerializer.Serialize(new
            {
                message = new
                {
                    data = Convert.ToBase64String(Encoding.UTF8.GetBytes(notificationData)),
                    messageId = $"msg_type_{notificationType}",
                    publishTime = DateTime.UtcNow.ToString("o")
                }
            });

            // Act
            var result = parser.Parse(requestBody);

            // Assert
            Assert.True(result.IsSuccess, $"Failed for notification type {notificationType}");
            Assert.Equal(
                GoogleRtdnNotificationKind.SubscriptionStateChanged,
                result.Notification!.Kind);
            Assert.Equal(notificationType, result.Notification.NotificationType);
        }
    }

    #endregion

    #region Helper Methods

    private static AppleNotificationParser CreateDevelopmentAppleParser() =>
        new(
            new AppleNotificationConfig
            {
                BundleId = "com.test.app",
                SkipSignatureValidation = true,
                HostEnvironmentName = "Development"
            },
            Mock.Of<ILogger<AppleNotificationParser>>());

    private static string CreateAppleNotification(string notificationType, object transaction)
    {
        var payload = JsonSerializer.Serialize(new
        {
            notificationType,
            notificationUUID = $"notification-{Guid.NewGuid():N}",
            data = new
            {
                bundleId = "com.test.app",
                environment = "Production",
                signedTransactionInfo = CreateFakeJws(transaction)
            }
        });
        return JsonSerializer.Serialize(new
        {
            signedPayload = CreateFakeJws(payload, raw: true)
        });
    }

    private static string CreateFakeJws(object payload, bool raw = false)
    {
        var header = new { alg = "ES256", typ = "JWT" };
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = raw ? (string)payload : JsonSerializer.Serialize(payload);

        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signature = Base64UrlEncode(Encoding.UTF8.GetBytes("fake_signature"));

        return $"{headerBase64}.{payloadBase64}.{signature}";
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion
}
