#nullable enable
using System;
using System.Collections.Generic;

namespace Serhat.Analytics.Providers.Firebase
{
    /// <summary>
    /// Maps analytics events to Firebase-compatible format.
    /// </summary>
    public static class FirebaseEventMapper
    {
        /// <summary>
        /// Maps a custom event name to Firebase standard event name if applicable.
        /// </summary>
        public static string MapEventName(string eventName)
        {
            return eventName switch
            {
                // Gameplay events
                "level_completed" => global::Firebase.Analytics.FirebaseAnalytics.EventLevelEnd,
                "level_started" => global::Firebase.Analytics.FirebaseAnalytics.EventLevelStart,
                "level_up" => global::Firebase.Analytics.FirebaseAnalytics.EventLevelUp,
                "post_score" => global::Firebase.Analytics.FirebaseAnalytics.EventPostScore,

                // Purchase events
                "purchase_completed" => global::Firebase.Analytics.FirebaseAnalytics.EventPurchase,
                "purchase_refund" => global::Firebase.Analytics.FirebaseAnalytics.EventRefund,

                // Engagement events
                "login" => global::Firebase.Analytics.FirebaseAnalytics.EventLogin,
                "sign_up" => global::Firebase.Analytics.FirebaseAnalytics.EventSignUp,
                "tutorial_begin" => global::Firebase.Analytics.FirebaseAnalytics.EventTutorialBegin,
                "tutorial_complete" => global::Firebase.Analytics.FirebaseAnalytics.EventTutorialComplete,

                // Ecommerce events
                "add_to_cart" => global::Firebase.Analytics.FirebaseAnalytics.EventAddToCart,
                "view_item" => global::Firebase.Analytics.FirebaseAnalytics.EventViewItem,
                "begin_checkout" => global::Firebase.Analytics.FirebaseAnalytics.EventBeginCheckout,

                // Share events
                "share" => global::Firebase.Analytics.FirebaseAnalytics.EventShare,

                // Screen events
                "screen_view" => global::Firebase.Analytics.FirebaseAnalytics.EventScreenView,

                // Default: use custom event name as-is
                _ => eventName
            };
        }

        /// <summary>
        /// Maps event parameters to Firebase Parameter array.
        /// </summary>
        public static global::Firebase.Analytics.Parameter[] MapParameters(Dictionary<string, object>? parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return Array.Empty<global::Firebase.Analytics.Parameter>();
            }

            var result = new List<global::Firebase.Analytics.Parameter>(parameters.Count);

            foreach (var kvp in parameters)
            {
                var param = MapParameter(kvp.Key, kvp.Value);
                if (param != null)
                {
                    result.Add(param);
                }
            }

            return result.ToArray();
        }

        private static global::Firebase.Analytics.Parameter? MapParameter(string key, object? value)
        {
            if (value == null) return null;

            // Map common parameter names to Firebase standard parameters
            var mappedKey = MapParameterKey(key);

            return value switch
            {
                string s => new global::Firebase.Analytics.Parameter(mappedKey, s),
                int i => new global::Firebase.Analytics.Parameter(mappedKey, i),
                long l => new global::Firebase.Analytics.Parameter(mappedKey, l),
                float f => new global::Firebase.Analytics.Parameter(mappedKey, f),
                double d => new global::Firebase.Analytics.Parameter(mappedKey, d),
                bool b => new global::Firebase.Analytics.Parameter(mappedKey, b ? 1L : 0L),
                decimal dec => new global::Firebase.Analytics.Parameter(mappedKey, (double)dec),
                _ => new global::Firebase.Analytics.Parameter(mappedKey, value.ToString() ?? "")
            };
        }

        private static string MapParameterKey(string key)
        {
            return key switch
            {
                // Gameplay parameters
                "level" => global::Firebase.Analytics.FirebaseAnalytics.ParameterLevel,
                "level_name" => global::Firebase.Analytics.FirebaseAnalytics.ParameterLevelName,
                "score" => global::Firebase.Analytics.FirebaseAnalytics.ParameterScore,
                "character" => global::Firebase.Analytics.FirebaseAnalytics.ParameterCharacter,

                // Purchase parameters
                "currency" => global::Firebase.Analytics.FirebaseAnalytics.ParameterCurrency,
                "value" => global::Firebase.Analytics.FirebaseAnalytics.ParameterValue,
                "price" => global::Firebase.Analytics.FirebaseAnalytics.ParameterPrice,
                "transaction_id" => "transaction_id",
                "item_id" => "item_id",
                "item_name" => global::Firebase.Analytics.FirebaseAnalytics.ParameterItemName,
                "item_category" => global::Firebase.Analytics.FirebaseAnalytics.ParameterItemCategory,
                "quantity" => global::Firebase.Analytics.FirebaseAnalytics.ParameterQuantity,

                // Engagement parameters
                "method" => global::Firebase.Analytics.FirebaseAnalytics.ParameterMethod,
                "content_type" => global::Firebase.Analytics.FirebaseAnalytics.ParameterContentType,
                "content_id" => "item_id",

                // Screen parameters
                "screen_name" => global::Firebase.Analytics.FirebaseAnalytics.ParameterScreenName,
                "screen_class" => global::Firebase.Analytics.FirebaseAnalytics.ParameterScreenClass,

                // Achievement parameters
                "achievement_id" => "achievement_id",

                // Success parameter
                "success" => global::Firebase.Analytics.FirebaseAnalytics.ParameterSuccess,

                // Default: use custom parameter name as-is
                _ => key
            };
        }
    }
}
