#nullable enable
using System.Collections.Generic;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Events
{
    /// <summary>
    /// Purchase-related analytics events (IAP, subscriptions).
    /// </summary>
    public static class PurchaseEvents
    {
        public const string Category = EventCategory.Purchase;

        /// <summary>
        /// Track purchase initiation.
        /// </summary>
        public static AnalyticsEvent PurchaseInitiated(string productId, decimal price, string currency, string? productType = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "purchase_initiated",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["product_id"] = productId,
                    ["price"] = (double)price,
                    ["currency"] = currency
                }
            };

            if (productType != null)
            {
                evt.Parameters["product_type"] = productType;
            }

            return evt;
        }

        /// <summary>
        /// Track successful purchase completion.
        /// </summary>
        public static AnalyticsEvent PurchaseCompleted(
            string productId,
            string transactionId,
            decimal price,
            string currency,
            string? productType = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "purchase_completed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["product_id"] = productId,
                    ["transaction_id"] = transactionId,
                    ["price"] = (double)price,
                    ["currency"] = currency,
                    ["value"] = (double)price, // Firebase standard parameter
                    ["success"] = true
                }
            };

            if (productType != null)
            {
                evt.Parameters["product_type"] = productType;
            }

            return evt;
        }

        /// <summary>
        /// Track purchase failure.
        /// </summary>
        public static AnalyticsEvent PurchaseFailed(
            string productId,
            string errorCode,
            string errorMessage,
            decimal? price = null,
            string? currency = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "purchase_failed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["product_id"] = productId,
                    ["error_code"] = errorCode,
                    ["error_message"] = errorMessage.Length > 100 ? errorMessage.Substring(0, 100) : errorMessage,
                    ["success"] = false
                }
            };

            if (price.HasValue)
            {
                evt.Parameters["price"] = (double)price.Value;
            }

            if (currency != null)
            {
                evt.Parameters["currency"] = currency;
            }

            return evt;
        }

        /// <summary>
        /// Track purchase cancellation.
        /// </summary>
        public static AnalyticsEvent PurchaseCancelled(string productId, string? reason = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "purchase_cancelled",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["product_id"] = productId
                }
            };

            if (reason != null)
            {
                evt.Parameters["reason"] = reason;
            }

            return evt;
        }

        /// <summary>
        /// Track purchase restoration.
        /// </summary>
        public static AnalyticsEvent PurchaseRestored(string productId, string transactionId)
        {
            return new AnalyticsEvent
            {
                EventName = "purchase_restored",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["product_id"] = productId,
                    ["transaction_id"] = transactionId
                }
            };
        }

        /// <summary>
        /// Track subscription state change.
        /// </summary>
        public static AnalyticsEvent SubscriptionChanged(
            string subscriptionTier,
            string platform,
            bool isNew,
            decimal? priceUsd = null,
            int? durationDays = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "subscription_changed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["subscription_tier"] = subscriptionTier,
                    ["platform"] = platform,
                    ["is_new"] = isNew
                }
            };

            if (priceUsd.HasValue)
            {
                evt.Parameters["price_usd"] = (double)priceUsd.Value;
            }

            if (durationDays.HasValue)
            {
                evt.Parameters["duration_days"] = durationDays.Value;
            }

            return evt;
        }

        /// <summary>
        /// Track product view (when user views a product in store).
        /// </summary>
        public static AnalyticsEvent ProductViewed(string productId, decimal price, string currency)
        {
            return new AnalyticsEvent
            {
                EventName = "view_item",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["item_id"] = productId,
                    ["price"] = (double)price,
                    ["currency"] = currency
                }
            };
        }
    }
}
