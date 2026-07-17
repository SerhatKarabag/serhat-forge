#nullable enable
using System;
using System.Collections.Generic;

namespace Serhat.Backend.Core
{
    /// <summary>
    /// Result type for all backend operations. Never throws for expected failures.
    /// </summary>
    public readonly struct CloudResult<T>
    {
        public bool IsSuccess { get; }
        public T? Data { get; }
        public BackendError? Error { get; }

        private CloudResult(bool isSuccess, T? data, BackendError? error)
        {
            IsSuccess = isSuccess;
            Data = data;
            Error = error;
        }

        public static CloudResult<T> Success(T data) => new(true, data, null);
        public static CloudResult<T> Failure(BackendError error) => new(false, default, error);

        public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<BackendError, TResult> onFailure)
        {
            return IsSuccess ? onSuccess(Data!) : onFailure(Error!);
        }

        public void Match(Action<T> onSuccess, Action<BackendError> onFailure)
        {
            if (IsSuccess)
                onSuccess(Data!);
            else
                onFailure(Error!);
        }
    }

    /// <summary>
    /// Standardized error contract matching server-side format.
    /// </summary>
    public sealed class BackendError
    {
        public string Code { get; }
        public string Message { get; }
        public bool Retryable { get; }
        public int? HttpStatus { get; }
        public int? ProviderErrorCode { get; }
        public string CorrelationId { get; }
        public IReadOnlyDictionary<string, string>? Details { get; }

        public BackendError(
            string code,
            string message,
            bool retryable = false,
            int? httpStatus = null,
            int? providerErrorCode = null,
            string? correlationId = null,
            IReadOnlyDictionary<string, string>? details = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Retryable = retryable;
            HttpStatus = httpStatus;
            ProviderErrorCode = providerErrorCode;
            CorrelationId = correlationId ?? string.Empty;
            Details = details;
        }

        public override string ToString() =>
            $"[{Code}] {Message} (Retryable: {Retryable}, CorrelationId: {CorrelationId})";
    }

    /// <summary>
    /// Standard error codes used throughout the SDK.
    /// </summary>
    public static class ErrorCodes
    {
        // Transport errors
        public const string NetworkError = "NETWORK_ERROR";
        public const string Timeout = "TIMEOUT";
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
        public const string RateLimited = "RATE_LIMITED";

        // Auth errors
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Forbidden = "FORBIDDEN";
        public const string SessionExpired = "SESSION_EXPIRED";

        // Validation errors
        public const string ValidationFailed = "VALIDATION_FAILED";
        public const string InvalidRequest = "INVALID_REQUEST";

        // Business logic errors
        public const string NotFound = "NOT_FOUND";
        public const string Conflict = "CONFLICT";
        public const string VersionMismatch = "VERSION_MISMATCH";
        public const string InsufficientFunds = "INSUFFICIENT_FUNDS";

        // System errors
        public const string InternalError = "INTERNAL_ERROR";
        public const string CircuitOpen = "CIRCUIT_OPEN";
        public const string OutboxFull = "OUTBOX_FULL";
        public const string SerializationError = "SERIALIZATION_ERROR";

        // Provider specific (kept generic)
        public const string ProviderError = "PROVIDER_ERROR";
    }
}
