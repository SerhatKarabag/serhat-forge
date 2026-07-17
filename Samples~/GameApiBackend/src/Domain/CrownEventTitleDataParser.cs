using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Parses crown-event configuration from PlayFab Title Data.
/// </summary>
public static class CrownEventTitleDataParser
{
    public const string TitleDataKey = "crown_event_v1";
    private const int MaxMilestoneCount = 512;
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
        out CrownEventConfigDto config,
        out string? error)
    {
        config = new CrownEventConfigDto();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        CrownEventTitleDataModel? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CrownEventTitleDataModel>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        if (payload == null)
        {
            error = "Title data payload is null.";
            return false;
        }

        var sourceMilestones = payload.Milestones ?? new List<CrownEventMilestoneModel>();
        if (sourceMilestones.Count > MaxMilestoneCount)
        {
            error = $"Milestone count exceeds max {MaxMilestoneCount}.";
            return false;
        }

        if (payload.CycleDurationSeconds < 0)
        {
            error = "cycleDurationSeconds must be 0 or greater.";
            return false;
        }

        var milestones = new List<CrownEventMilestoneDto>(sourceMilestones.Count);
        var seenIndices = new HashSet<int>();

        for (var i = 0; i < sourceMilestones.Count; i++)
        {
            var source = sourceMilestones[i];
            if (source == null)
            {
                error = $"Milestones[{i}] is null.";
                return false;
            }

            if (source.MilestoneIndex <= 0)
            {
                error = $"Milestones[{i}].milestoneIndex must be greater than 0.";
                return false;
            }

            if (!seenIndices.Add(source.MilestoneIndex))
            {
                error = $"Milestone index '{source.MilestoneIndex}' is duplicated.";
                return false;
            }

            if (source.RequiredCrowns <= 0)
            {
                error = $"Milestones[{i}].requiredCrowns must be greater than 0.";
                return false;
            }

            var boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source.Boosters != null)
            {
                foreach (var kvp in source.Boosters)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        error = $"Milestones[{i}].boosters contains an empty key.";
                        return false;
                    }

                    if (!TryNormalizeBoosterKey(kvp.Key, out var normalizedBoosterKey))
                    {
                        error = $"Milestones[{i}].boosters contains unknown booster key '{kvp.Key}'.";
                        return false;
                    }

                    if (kvp.Value <= 0)
                    {
                        continue;
                    }

                    boosters[normalizedBoosterKey] = kvp.Value;
                }
            }

            milestones.Add(new CrownEventMilestoneDto
            {
                MilestoneIndex = source.MilestoneIndex,
                RequiredCrowns = source.RequiredCrowns,
                Coins = Math.Max(0, source.Coins),
                InfiniteLivesMinutes = Math.Max(0, source.InfiniteLivesMinutes),
                Boosters = boosters
            });
        }

        milestones = milestones
            .OrderBy(x => x.MilestoneIndex)
            .ToList();

        for (var i = 0; i < milestones.Count; i++)
        {
            var expectedMilestoneIndex = i + 1;
            if (milestones[i].MilestoneIndex != expectedMilestoneIndex)
            {
                error = "Milestone indices must be sequential and start from 1.";
                return false;
            }
        }

        var derivedCrownsPerCycle = 0;
        for (var i = 0; i < milestones.Count; i++)
        {
            derivedCrownsPerCycle += milestones[i].RequiredCrowns;
        }

        config = new CrownEventConfigDto
        {
            CrownsPerCycle = payload.CrownsPerCycle > 0
                ? payload.CrownsPerCycle
                : Math.Max(0, derivedCrownsPerCycle),
            CycleDurationSeconds = payload.CycleDurationSeconds,
            Milestones = milestones
        };

        if (config.CrownsPerCycle != derivedCrownsPerCycle)
        {
            error = $"crownsPerCycle ({config.CrownsPerCycle}) must match milestone sum ({derivedCrownsPerCycle}).";
            return false;
        }

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

    internal sealed class CrownEventTitleDataModel
    {
        public int CrownsPerCycle { get; set; }
        public int CycleDurationSeconds { get; set; }
        public List<CrownEventMilestoneModel>? Milestones { get; set; }
    }

    internal sealed class CrownEventMilestoneModel
    {
        public int MilestoneIndex { get; set; }
        public int RequiredCrowns { get; set; }
        public int Coins { get; set; }
        public int InfiniteLivesMinutes { get; set; }
        public Dictionary<string, int>? Boosters { get; set; }
    }
}
