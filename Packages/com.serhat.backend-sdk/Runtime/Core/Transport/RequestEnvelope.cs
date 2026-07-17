#nullable enable
using System;
using System.Collections.Generic;

namespace Serhat.Backend.Core.Transport
{
    /// <summary>
    /// Request envelope sent to cloud functions.
    /// Transport-agnostic structure.
    /// </summary>
    [Serializable]
    public sealed class RequestEnvelope<T> where T : class
    {
        public string FunctionName { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string? IdempotencyKey { get; set; }
        public T? Payload { get; set; }
        public CallerContext Caller { get; set; } = new();
        public long TimestampMs { get; set; }
        public string SdkVersion { get; set; } = "2.0.0";
    }

    /// <summary>
    /// Caller context information.
    /// Transport-agnostic - does not contain provider-specific fields.
    /// </summary>
    [Serializable]
    public sealed class CallerContext
    {
        public string UserId { get; set; } = string.Empty;
        // Backward compatibility for backends expecting "playerId".
        public string PlayerId { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public Dictionary<string, string> CustomProperties { get; set; } = new();
    }

    /// <summary>
    /// Response envelope from cloud functions.
    /// </summary>
    [Serializable]
    public sealed class ResponseEnvelope<T> where T : class
    {
        public string CorrelationId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public T? Data { get; set; }
        public ErrorPayload? Error { get; set; }
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// Authoritative server UTC at the moment the response was built.
        /// Clients should use this to anchor a monotonic server-time clock so local-device-time
        /// manipulation cannot spoof time-gated features (daily gift, cooldowns, expirations).
        /// Value is <see cref="DateTime.MinValue"/> for servers that predate this field.
        /// </summary>
        public DateTime ServerUtcNow { get; set; }
    }

    /// <summary>
    /// Error payload in response envelope.
    /// </summary>
    [Serializable]
    public sealed class ErrorPayload
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Retryable { get; set; }
        public Dictionary<string, string>? Details { get; set; }
    }
}
