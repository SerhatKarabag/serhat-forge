using System;
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
            eventTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            subscriptionNotification = new
            {
                version = "1.0",
                notificationType = 4, // SUBSCRIPTION_PURCHASED
                purchaseToken = "purchase_token_001",
                subscriptionId = "premium_monthly"
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
        Assert.NotNull(result.Event);
        Assert.Equal(WebhookEventType.InitialPurchase, result.Event.EventType);
        Assert.Equal(Platform.Google, result.Event.Platform);
        Assert.Equal("premium_monthly", result.Event.ProductId);
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
        Assert.Null(result.Event);
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
        Assert.NotNull(result.Event);
        Assert.Equal(WebhookEventType.Refunded, result.Event.EventType);
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
    public void GoogleRtdnParser_AllSubscriptionNotificationTypes_MapCorrectly()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<GoogleRtdnParser>>();
        var config = new GoogleRtdnConfig { ExpectedPackageName = "com.test.app" };
        var parser = new GoogleRtdnParser(config, loggerMock.Object);

        var testCases = new[]
        {
            (1, WebhookEventType.Recovered),        // SUBSCRIPTION_RECOVERED
            (2, WebhookEventType.Renewed),          // SUBSCRIPTION_RENEWED
            (3, WebhookEventType.Cancelled),        // SUBSCRIPTION_CANCELED
            (4, WebhookEventType.InitialPurchase),  // SUBSCRIPTION_PURCHASED
            (5, WebhookEventType.GracePeriodStarted), // SUBSCRIPTION_ON_HOLD
            (6, WebhookEventType.GracePeriodStarted), // SUBSCRIPTION_IN_GRACE_PERIOD
            (7, WebhookEventType.Resubscribed),     // SUBSCRIPTION_RESTARTED
            (10, WebhookEventType.Paused),          // SUBSCRIPTION_PAUSED
            (12, WebhookEventType.Revoked),         // SUBSCRIPTION_REVOKED
            (13, WebhookEventType.Expired)          // SUBSCRIPTION_EXPIRED
        };

        foreach (var (notificationType, expectedEventType) in testCases)
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
            Assert.Equal(expectedEventType, result.Event!.EventType);
        }
    }

    #endregion

    #region Helper Methods

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
