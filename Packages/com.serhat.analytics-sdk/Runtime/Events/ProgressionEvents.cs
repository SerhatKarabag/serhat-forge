#nullable enable
using System.Collections.Generic;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Events
{
    /// <summary>
    /// Progression-related analytics events (currency, rewards, unlocks).
    /// </summary>
    public static class ProgressionEvents
    {
        public const string Category = EventCategory.Progression;

        /// <summary>
        /// Track when coins are earned.
        /// </summary>
        public static AnalyticsEvent CoinsEarned(int amount, string source, int totalCoins)
        {
            return new AnalyticsEvent
            {
                EventName = "coins_earned",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["amount"] = amount,
                    ["source"] = source,
                    ["total_coins"] = totalCoins
                }
            };
        }

        /// <summary>
        /// Track when coins are spent.
        /// </summary>
        public static AnalyticsEvent CoinsSpent(int amount, string item, int totalCoins)
        {
            return new AnalyticsEvent
            {
                EventName = "coins_spent",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["amount"] = amount,
                    ["item"] = item,
                    ["total_coins"] = totalCoins
                }
            };
        }

        /// <summary>
        /// Track when gems are earned.
        /// </summary>
        public static AnalyticsEvent GemsEarned(int amount, string source, int totalGems)
        {
            return new AnalyticsEvent
            {
                EventName = "gems_earned",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["amount"] = amount,
                    ["source"] = source,
                    ["total_gems"] = totalGems
                }
            };
        }

        /// <summary>
        /// Track when gems are spent.
        /// </summary>
        public static AnalyticsEvent GemsSpent(int amount, string item, int totalGems)
        {
            return new AnalyticsEvent
            {
                EventName = "gems_spent",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["amount"] = amount,
                    ["item"] = item,
                    ["total_gems"] = totalGems
                }
            };
        }

        /// <summary>
        /// Track daily reward claim.
        /// </summary>
        public static AnalyticsEvent DailyRewardClaimed(int day, int coinsAwarded, int gemsAwarded, int consecutiveDays = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "daily_reward_claimed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["day"] = day,
                    ["coins_awarded"] = coinsAwarded,
                    ["gems_awarded"] = gemsAwarded,
                    ["consecutive_days"] = consecutiveDays
                }
            };
        }

        /// <summary>
        /// Track when an item is unlocked.
        /// </summary>
        public static AnalyticsEvent ItemUnlocked(string itemId, string itemType, string unlockMethod)
        {
            return new AnalyticsEvent
            {
                EventName = "item_unlocked",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["item_id"] = itemId,
                    ["item_type"] = itemType,
                    ["unlock_method"] = unlockMethod
                }
            };
        }

        /// <summary>
        /// Track milestone achievement.
        /// </summary>
        public static AnalyticsEvent MilestoneReached(string milestoneType, int value, int totalCoins = 0, int totalGems = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "milestone_reached",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["milestone_type"] = milestoneType,
                    ["value"] = value,
                    ["total_coins"] = totalCoins,
                    ["total_gems"] = totalGems
                }
            };
        }

        /// <summary>
        /// Track achievement unlock.
        /// </summary>
        public static AnalyticsEvent AchievementUnlocked(string achievementId, string achievementName)
        {
            return new AnalyticsEvent
            {
                EventName = "achievement_unlocked",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["achievement_id"] = achievementId,
                    ["achievement_name"] = achievementName
                }
            };
        }
    }
}
