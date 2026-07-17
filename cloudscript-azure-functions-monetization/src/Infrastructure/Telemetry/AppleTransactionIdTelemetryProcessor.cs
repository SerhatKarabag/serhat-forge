using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Serhat.Forge.CloudScript.Infrastructure.Telemetry;

/// <summary>
/// Removes Apple transaction identifiers from automatically collected App Store Server API
/// dependency URLs before telemetry leaves the process.
/// </summary>
public sealed class AppleTransactionIdTelemetryProcessor : ITelemetryProcessor
{
    private const string TransactionPathMarker = "/inApps/v1/transactions/";
    private const string RedactedValue = "[REDACTED]";

    private readonly ITelemetryProcessor _next;

    public AppleTransactionIdTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item is DependencyTelemetry dependency)
        {
            dependency.Name = RedactTransactionId(dependency.Name);
            dependency.Data = RedactTransactionId(dependency.Data);
        }

        _next.Process(item);
    }

    public static string? RedactTransactionId(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var markerIndex = value.IndexOf(
            TransactionPathMarker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return value;
        }

        var identifierStart = markerIndex + TransactionPathMarker.Length;
        var identifierEnd = identifierStart;
        while (identifierEnd < value.Length && !IsIdentifierTerminator(value[identifierEnd]))
        {
            identifierEnd++;
        }

        if (identifierEnd == identifierStart)
        {
            return value;
        }

        return value[..identifierStart] + RedactedValue + value[identifierEnd..];
    }

    private static bool IsIdentifierTerminator(char value) =>
        value is '/' or '?' or '#' or '&' or ' ' or '\t' or '\r' or '\n' or '\'' or '"';
}
