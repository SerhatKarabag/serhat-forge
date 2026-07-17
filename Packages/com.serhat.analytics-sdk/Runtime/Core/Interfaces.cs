#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Analytics.Core
{
    /// <summary>
    /// Clock abstraction for testability.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        long TimestampMs { get; }
    }

    /// <summary>
    /// Logger abstraction for analytics.
    /// </summary>
    public interface IAnalyticsLogger
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
    /// Event queue status information.
    /// </summary>
    public sealed class EventQueueStatus
    {
        public int PendingCount { get; set; }
        public int OfflineQueueCount { get; set; }
        public DateTime? OldestPendingUtc { get; set; }
        public DateTime? LastFlushUtc { get; set; }
        public bool IsProcessing { get; set; }
    }

    /// <summary>
    /// Analytics mode configuration.
    /// </summary>
    public enum AnalyticsMode
    {
        /// <summary>
        /// No tracking at all.
        /// </summary>
        Disabled,

        /// <summary>
        /// Console logging only, no remote tracking.
        /// </summary>
        DebugOnly,

        /// <summary>
        /// Both console logging and remote tracking.
        /// </summary>
        DebugAndRemote,

        /// <summary>
        /// Remote tracking only, no console logging.
        /// </summary>
        RemoteOnly
    }
}
