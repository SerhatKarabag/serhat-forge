#nullable enable
using System.Collections.Generic;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Events
{
    /// <summary>
    /// Gameplay-related analytics events.
    /// </summary>
    public static class GameplayEvents
    {
        public const string Category = EventCategory.Gameplay;

        /// <summary>
        /// Track when a level is started.
        /// </summary>
        public static AnalyticsEvent LevelStart(int levelId, IReadOnlyList<string>? startBoosters = null)
        {
            return new AnalyticsEvent
            {
                EventName = "level_start",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["level_id"] = levelId,
                    ["startboosters"] = SerializeStartBoosters(startBoosters)
                }
            };
        }

        /// <summary>
        /// Track when a level is completed successfully.
        /// </summary>
        public static AnalyticsEvent LevelComplete(int levelId, float durationSeconds)
        {
            return new AnalyticsEvent
            {
                EventName = "level_complete",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["level_id"] = levelId,
                    ["duration"] = durationSeconds
                }
            };
        }

        /// <summary>
        /// Track when a level fails.
        /// </summary>
        public static AnalyticsEvent LevelFail(int levelId)
        {
            return new AnalyticsEvent
            {
                EventName = "level_fail",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["level_id"] = levelId
                }
            };
        }

        /// <summary>
        /// Track when a level is completed successfully.
        /// </summary>
        public static AnalyticsEvent LevelCompleted(int level, int score, float durationSeconds, int coinsCollected = 0)
        {
            return LevelComplete(level, durationSeconds);
        }

        /// <summary>
        /// Track when a level is started.
        /// </summary>
        public static AnalyticsEvent LevelStarted(int level)
        {
            return LevelStart(level);
        }

        /// <summary>
        /// Track when a level fails.
        /// </summary>
        public static AnalyticsEvent LevelFailed(int level, int score, float durationSeconds, string reason)
        {
            return LevelFail(level);
        }

        /// <summary>
        /// Track game over event.
        /// </summary>
        public static AnalyticsEvent GameOver(int finalScore, string reason, int levelsPlayed = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "game_over",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["final_score"] = finalScore,
                    ["reason"] = reason,
                    ["levels_played"] = levelsPlayed
                }
            };
        }

        /// <summary>
        /// Track when a new high score is achieved.
        /// </summary>
        public static AnalyticsEvent HighScoreAchieved(int newHighScore, int previousHighScore, int level)
        {
            return new AnalyticsEvent
            {
                EventName = "high_score_achieved",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["new_high_score"] = newHighScore,
                    ["previous_high_score"] = previousHighScore,
                    ["level"] = level,
                    ["improvement"] = newHighScore - previousHighScore
                }
            };
        }

        /// <summary>
        /// Track when player's global rank changes.
        /// </summary>
        public static AnalyticsEvent RankUpdated(int newRank, int previousRank)
        {
            return new AnalyticsEvent
            {
                EventName = "rank_updated",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["new_rank"] = newRank,
                    ["previous_rank"] = previousRank,
                    ["rank_change"] = previousRank - newRank // Positive = improvement
                }
            };
        }

        /// <summary>
        /// Track tutorial completion.
        /// </summary>
        public static AnalyticsEvent TutorialCompleted(float durationSeconds, bool skipped = false)
        {
            return new AnalyticsEvent
            {
                EventName = "tutorial_complete",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["duration_seconds"] = durationSeconds,
                    ["skipped"] = skipped
                }
            };
        }

        /// <summary>
        /// Track when tutorial is started.
        /// </summary>
        public static AnalyticsEvent TutorialStarted()
        {
            return new AnalyticsEvent
            {
                EventName = "tutorial_begin",
                Category = Category,
                Parameters = new Dictionary<string, object>()
            };
        }

        private static string SerializeStartBoosters(IReadOnlyList<string>? startBoosters)
        {
            if (startBoosters == null || startBoosters.Count == 0)
            {
                return string.Empty;
            }

            if (startBoosters.Count == 1)
            {
                return startBoosters[0] ?? string.Empty;
            }

            return string.Join(",", startBoosters);
        }
    }
}
