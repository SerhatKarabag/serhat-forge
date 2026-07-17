using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Parses daily-gift configuration from PlayFab Title Data.
/// Expected JSON shape:
/// <code>
/// {
///   "rewards": [
///     { "day": 1, "coins": 50 },
///     { "day": 2, "coins": 50 },
///     { "day": 3, "infiniteLivesMinutes": 60 },
///     { "day": 4, "boosters": { "TimeBooster": 1, "CompassBooster": 1 } }
///   ]
/// }
/// </code>
/// </summary>
public static class DailyGiftTitleDataParser
{
    public const string TitleDataKey = "daily_gift_v1";
    public const int MinRewardCount = 1;
    public const int MaxRewardCount = 31;

    private static readonly string[] KnownBoosterKeys =
    {
        "SizeBooster",
        "MagnetBooster",
        "SpeedBooster",
        "TimeBooster",
        "CompassBooster",
        "StartXpBooster",
        "StartPowerBooster",
        "StartTimeBooster"
    };

    private static readonly Dictionary<string, string> BoosterAliasMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["XpBoost"] = "StartXpBooster",
            ["PowerBoost"] = "StartPowerBooster",
            ["TimeBoost"] = "StartTimeBooster"
        };

    private static readonly HashSet<string> KnownBoosterSet =
        new(KnownBoosterKeys, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(
        string? json,
        out DailyGiftConfigDto config,
        out string? error)
    {
        config = new DailyGiftConfigDto();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        DailyGiftTitleDataModel? payload;
        try
        {
            payload = JsonSerializer.Deserialize<DailyGiftTitleDataModel>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        if (payload == null)
        {
            error = "Daily gift title data payload is null.";
            return false;
        }

        var sourceRewards = payload.Rewards ?? new List<DailyGiftRewardModel>();
        if (sourceRewards.Count < MinRewardCount)
        {
            error = $"rewards array must contain at least {MinRewardCount} entry.";
            return false;
        }

        if (sourceRewards.Count > MaxRewardCount)
        {
            error = $"rewards count exceeds max {MaxRewardCount}.";
            return false;
        }

        var rewards = new List<DailyGiftRewardDto>(sourceRewards.Count);
        var seenDays = new HashSet<int>();

        for (var i = 0; i < sourceRewards.Count; i++)
        {
            var source = sourceRewards[i];
            if (source == null)
            {
                error = $"rewards[{i}] is null.";
                return false;
            }

            if (source.Day <= 0)
            {
                error = $"rewards[{i}].day must be greater than 0.";
                return false;
            }

            if (!seenDays.Add(source.Day))
            {
                error = $"rewards day '{source.Day}' is duplicated.";
                return false;
            }

            if (source.Coins < 0)
            {
                error = $"rewards[{i}].coins must be 0 or greater.";
                return false;
            }

            if (source.InfiniteLivesMinutes < 0)
            {
                error = $"rewards[{i}].infiniteLivesMinutes must be 0 or greater.";
                return false;
            }

            var boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source.Boosters != null)
            {
                foreach (var kvp in source.Boosters)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        error = $"rewards[{i}].boosters contains an empty key.";
                        return false;
                    }

                    if (!TryNormalizeBoosterKey(kvp.Key, out var normalizedKey))
                    {
                        error = $"rewards[{i}].boosters contains unknown booster key '{kvp.Key}'.";
                        return false;
                    }

                    if (kvp.Value <= 0)
                    {
                        continue;
                    }

                    boosters[normalizedKey] = kvp.Value;
                }
            }

            if (source.Coins == 0 && source.InfiniteLivesMinutes == 0 && boosters.Count == 0)
            {
                error = $"rewards[{i}] (day {source.Day}) has no reward payload (coins, infiniteLivesMinutes, or boosters).";
                return false;
            }

            rewards.Add(new DailyGiftRewardDto
            {
                Day = source.Day,
                Coins = source.Coins,
                InfiniteLivesMinutes = source.InfiniteLivesMinutes,
                Boosters = boosters
            });
        }

        rewards = rewards.OrderBy(x => x.Day).ToList();

        for (var i = 0; i < rewards.Count; i++)
        {
            var expectedDay = i + 1;
            if (rewards[i].Day != expectedDay)
            {
                error = "rewards days must be sequential and start from 1.";
                return false;
            }
        }

        config = new DailyGiftConfigDto
        {
            Rewards = rewards
        };

        error = null;
        return true;
    }

    private static bool TryNormalizeBoosterKey(string rawKey, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return false;
        }

        if (BoosterAliasMap.TryGetValue(rawKey.Trim(), out var aliasTarget))
        {
            normalizedKey = aliasTarget;
            return true;
        }

        if (!KnownBoosterSet.Contains(rawKey))
        {
            return false;
        }

        normalizedKey = KnownBoosterKeys
            .First(x => string.Equals(x, rawKey, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    internal sealed class DailyGiftTitleDataModel
    {
        public List<DailyGiftRewardModel>? Rewards { get; set; }
    }

    internal sealed class DailyGiftRewardModel
    {
        public int Day { get; set; }
        public int Coins { get; set; }
        public int InfiniteLivesMinutes { get; set; }
        public Dictionary<string, int>? Boosters { get; set; }
    }
}
