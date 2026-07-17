using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Serhat.Forge.CloudScript.Infrastructure.Telemetry;

/// <summary>
/// Removes bearer-like Google Play purchase tokens from automatically collected
/// Application Insights dependency URLs before telemetry leaves the process.
/// </summary>
public sealed class GooglePlayPurchaseTokenTelemetryProcessor : ITelemetryProcessor
{
    private const string TokenPathMarker = "/tokens/";
    private const string RedactedValue = "[REDACTED]";

    private readonly ITelemetryProcessor _next;

    public GooglePlayPurchaseTokenTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item is DependencyTelemetry dependency)
        {
            dependency.Name = RedactAndroidPublisherToken(dependency.Name);
            dependency.Data = RedactAndroidPublisherToken(dependency.Data);
        }

        _next.Process(item);
    }

    public static string? RedactAndroidPublisherToken(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.IndexOf("androidpublisher", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return value;
        }

        var markerIndex = value.IndexOf(TokenPathMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return value;
        }

        var tokenStart = markerIndex + TokenPathMarker.Length;
        var tokenEnd = tokenStart;
        while (tokenEnd < value.Length && !IsTokenTerminator(value[tokenEnd]))
        {
            tokenEnd++;
        }

        if (tokenEnd == tokenStart)
        {
            return value;
        }

        return value[..tokenStart] + RedactedValue + value[tokenEnd..];
    }

    private static bool IsTokenTerminator(char value) =>
        value is '/' or '?' or '#' or '&' or ' ' or '\t' or '\r' or '\n' or '\'' or '"';
}
