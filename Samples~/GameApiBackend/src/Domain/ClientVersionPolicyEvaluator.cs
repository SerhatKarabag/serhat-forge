using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Serhat.Forge.CloudScript.Domain;

public static class ClientVersionPolicyEvaluator
{
    public const string TitleDataKey = "client_version_policy";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool IsManagedPlatform(string? platform)
    {
        return !string.IsNullOrEmpty(NormalizePlatform(platform));
    }

    public static string NormalizeManagedPlatform(string? platform)
    {
        return NormalizePlatform(platform);
    }

    public static bool TryEvaluate(
        string policyJson,
        string? platform,
        string? appVersion,
        out ClientVersionRequirement? requirement,
        out string validationError)
    {
        requirement = null;
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(policyJson))
        {
            validationError = "Version policy is empty.";
            return false;
        }

        var normalizedPlatform = NormalizePlatform(platform);
        if (string.IsNullOrEmpty(normalizedPlatform))
        {
            validationError = $"Unsupported platform '{platform ?? "<null>"}'.";
            return false;
        }

        ClientVersionPolicyConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ClientVersionPolicyConfig>(policyJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            validationError = $"Version policy JSON parse failed: {ex.Message}";
            return false;
        }

        if (config == null)
        {
            validationError = "Version policy JSON produced a null config.";
            return false;
        }

        var platformPolicy = normalizedPlatform switch
        {
            "android" => config.Android,
            "ios" => config.Ios,
            _ => null
        };

        if (platformPolicy == null || string.IsNullOrWhiteSpace(platformPolicy.MinSupportedVersion))
        {
            validationError = $"No minimum version configured for platform '{normalizedPlatform}'.";
            return false;
        }

        if (!TryParseVersion(appVersion, out var currentVersion))
        {
            validationError = $"Invalid caller app version '{appVersion ?? "<null>"}'.";
            return false;
        }

        if (!TryParseVersion(platformPolicy.MinSupportedVersion, out var minimumVersion))
        {
            validationError =
                $"Invalid minimum supported version '{platformPolicy.MinSupportedVersion}' for platform '{normalizedPlatform}'.";
            return false;
        }

        if (CompareVersions(currentVersion, minimumVersion) >= 0)
        {
            validationError =
                $"Client version {appVersion?.Trim() ?? "<null>"} satisfies minimum {platformPolicy.MinSupportedVersion.Trim()}.";
            return false;
        }

        var resolvedTitle = string.IsNullOrWhiteSpace(platformPolicy.Title)
            ? "Update Required"
            : platformPolicy.Title.Trim();

        var resolvedMessage = string.IsNullOrWhiteSpace(platformPolicy.Message)
            ? $"A newer version is required to continue. Please update the game to at least {platformPolicy.MinSupportedVersion}."
            : platformPolicy.Message.Trim();

        requirement = new ClientVersionRequirement(
            normalizedPlatform,
            appVersion?.Trim() ?? string.Empty,
            platformPolicy.MinSupportedVersion.Trim(),
            platformPolicy.StoreUrl?.Trim() ?? string.Empty,
            resolvedTitle,
            resolvedMessage);

        return true;
    }

    private static string NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return string.Empty;
        }

        if (platform.Contains("android", StringComparison.OrdinalIgnoreCase))
        {
            return "android";
        }

        if (platform.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("ios", StringComparison.OrdinalIgnoreCase))
        {
            return "ios";
        }

        return string.Empty;
    }

    private static bool TryParseVersion(string? rawValue, out int[] segments)
    {
        segments = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var parts = rawValue.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var normalized = new int[4];
        var count = Math.Min(parts.Length, normalized.Length);

        for (var i = 0; i < count; i++)
        {
            if (!TryParseSegment(parts[i], out normalized[i]))
            {
                return false;
            }
        }

        segments = normalized;
        return true;
    }

    private static bool TryParseSegment(string rawSegment, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(rawSegment))
        {
            return false;
        }

        var digitCount = 0;
        foreach (var ch in rawSegment)
        {
            if (char.IsDigit(ch))
            {
                value = (value * 10) + (ch - '0');
                digitCount++;
                continue;
            }

            if (digitCount == 0)
            {
                return false;
            }

            break;
        }

        return digitCount > 0;
    }

    private static int CompareVersions(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        var length = Math.Max(left.Count, right.Count);
        for (var i = 0; i < length; i++)
        {
            var leftValue = i < left.Count ? left[i] : 0;
            var rightValue = i < right.Count ? right[i] : 0;
            if (leftValue != rightValue)
            {
                return leftValue.CompareTo(rightValue);
            }
        }

        return 0;
    }
}

public sealed class ClientVersionPolicyConfig
{
    public ClientVersionPlatformPolicy? Android { get; set; }
    public ClientVersionPlatformPolicy? Ios { get; set; }
}

public sealed class ClientVersionPlatformPolicy
{
    public string MinSupportedVersion { get; set; } = string.Empty;
    public string StoreUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ClientVersionRequirement
{
    public ClientVersionRequirement(
        string platform,
        string currentVersion,
        string minimumSupportedVersion,
        string storeUrl,
        string title,
        string message)
    {
        Platform = platform;
        CurrentVersion = currentVersion;
        MinimumSupportedVersion = minimumSupportedVersion;
        StoreUrl = storeUrl;
        Title = title;
        Message = message;
    }

    public string Platform { get; }
    public string CurrentVersion { get; }
    public string MinimumSupportedVersion { get; }
    public string StoreUrl { get; }
    public string Title { get; }
    public string Message { get; }

    public Dictionary<string, string> ToErrorDetails()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["platform"] = Platform,
            ["currentVersion"] = CurrentVersion,
            ["minSupportedVersion"] = MinimumSupportedVersion,
            ["storeUrl"] = StoreUrl,
            ["title"] = Title,
            ["message"] = Message
        };
    }
}
