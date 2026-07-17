#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Backend.Core
{
    /// <summary>
    /// Abstraction for invoking cloud functions.
    /// Transport-agnostic interface that can be implemented for PlayFab, custom HTTP, etc.
    /// </summary>
    public interface ICloudFunctionInvoker
    {
        Task<CloudResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
            string functionName,
            TRequest request,
            CloudCallOptions options,
            CancellationToken ct = default)
            where TRequest : class
            where TResponse : class;
    }

    /// <summary>
    /// Clock abstraction for testability.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        long TimestampMs { get; }
    }

    /// <summary>
    /// Optional capability on <see cref="IClock"/> implementations that can be realigned to the
    /// server's authoritative UTC. The transport layer calls <see cref="AnchorToServerTime"/>
    /// after every successful response that carries a non-default <c>ServerUtcNow</c>, so the
    /// same clock instance that feeds request timestamps also exposes a tamper-resistant
    /// "server now" reading to application code.
    /// </summary>
    public interface IServerTimeAnchor
    {
        void AnchorToServerTime(DateTime serverUtcNow);
    }

    /// <summary>
    /// Random number generator abstraction for testability.
    /// </summary>
    public interface IRandom
    {
        int Next(int minValue, int maxValue);
        double NextDouble();
    }

    /// <summary>
    /// Logger abstraction.
    /// </summary>
    public interface IBackendLogger
    {
        void Debug(string message, params object[] args);
        void Info(string message, params object[] args);
        void Warning(string message, params object[] args);
        void Error(string message, Exception? exception = null, params object[] args);
    }

    /// <summary>
    /// Connectivity checker abstraction.
    /// </summary>
    public interface IConnectivity
    {
        bool IsOnline { get; }
        event Action<bool>? OnConnectivityChanged;
    }

    /// <summary>
    /// Storage abstraction for persistence.
    /// </summary>
    public interface IStorage
    {
        Task<string?> ReadAsync(string key, CancellationToken ct = default);
        Task WriteAsync(string key, string data, CancellationToken ct = default);
        Task DeleteAsync(string key, CancellationToken ct = default);
        Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    }

    /// <summary>
    /// Serializer abstraction.
    /// </summary>
    public interface ISerializer
    {
        string Serialize<T>(T value);
        T? Deserialize<T>(string json);
    }

    /// <summary>
    /// Options for cloud function calls.
    /// </summary>
    public sealed class CloudCallOptions
    {
        public string CorrelationId { get; set; } = string.Empty;
        public Guid? IdempotencyKey { get; set; }
        public TimeSpan? Timeout { get; set; }
        public bool BypassCache { get; set; }

        public CloudCallOptions WithCorrelationId(string correlationId)
        {
            CorrelationId = correlationId;
            return this;
        }

        public CloudCallOptions WithIdempotencyKey(Guid key)
        {
            IdempotencyKey = key;
            return this;
        }
    }

    /// <summary>
    /// Outbox status information.
    /// </summary>
    public sealed class OutboxStatus
    {
        public int PendingCount { get; set; }
        public int DeadLetterCount { get; set; }
        public DateTime? OldestPendingUtc { get; set; }
        public DateTime? LastFlushAttemptUtc { get; set; }
        public bool IsProcessing { get; set; }
    }
}
