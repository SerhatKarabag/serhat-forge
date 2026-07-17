namespace Serhat.Forge.CloudScript.Domain.DTOs;

/// <summary>
/// Request envelope from Unity client.
/// </summary>
public sealed class RequestEnvelope<T> where T : class
{
    public string FunctionName { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public T? Payload { get; set; }
    public CallerContext Caller { get; set; } = new();
    public long TimestampMs { get; set; }
    public string SdkVersion { get; set; } = string.Empty;
}

/// <summary>
/// Caller context from Unity client.
/// </summary>
public sealed class CallerContext
{
    public string PlayerId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? EntityType { get; set; }
    public string TitleId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// Response envelope to Unity client.
/// </summary>
public sealed class ResponseEnvelope<T> where T : class
{
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ErrorPayload? Error { get; set; }
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// Authoritative server UTC at the moment the response is built. The client anchors a
    /// monotonic clock to this value so fast-forwarding the device clock cannot spoof
    /// time-gated features (daily gift window, cooldowns, expirations).
    /// </summary>
    public DateTime ServerUtcNow { get; set; }

    public static ResponseEnvelope<T> Ok(T data, string correlationId, long processingTimeMs) => new()
    {
        Success = true,
        Data = data,
        CorrelationId = correlationId,
        ProcessingTimeMs = processingTimeMs,
        ServerUtcNow = DateTime.UtcNow
    };

    public static ResponseEnvelope<T> Fail(ErrorPayload error, string correlationId, long processingTimeMs) => new()
    {
        Success = false,
        Error = error,
        CorrelationId = correlationId,
        ProcessingTimeMs = processingTimeMs,
        ServerUtcNow = DateTime.UtcNow
    };
}

/// <summary>
/// Error payload in response.
/// </summary>
public sealed class ErrorPayload
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Retryable { get; set; }
    public Dictionary<string, string>? Details { get; set; }

    public static ErrorPayload Create(string code, string message, bool retryable = false,
        Dictionary<string, string>? details = null) => new()
    {
        Code = code,
        Message = message,
        Retryable = retryable,
        Details = details
    };
}
