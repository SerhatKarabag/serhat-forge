#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Backend.Core
{
    /// <summary>
    /// Default clock implementation using system time.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();
        public DateTime UtcNow => DateTime.UtcNow;
        public long TimestampMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Default random implementation.
    /// </summary>
    public sealed class SystemRandom : IRandom
    {
        private readonly System.Random _random = new();

        public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
        public double NextDouble() => _random.NextDouble();
    }

    /// <summary>
    /// Default logger using Unity Debug.Log.
    /// </summary>
    public sealed class UnityBackendLogger : IBackendLogger
    {
        private readonly string _prefix;

        public UnityBackendLogger(string prefix = "BackendSdk")
        {
            _prefix = prefix;
        }

        public void Debug(string message, params object[] args)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Debug.Log(Format("DEBUG", message, args));
#endif
        }

        public void Info(string message, params object[] args)
        {
            UnityEngine.Debug.Log(Format("INFO", message, args));
        }

        public void Warning(string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(Format("WARN", message, args));
        }

        public void Error(string message, Exception? exception = null, params object[] args)
        {
            var formatted = Format("ERROR", message, args);
            if (exception != null)
                UnityEngine.Debug.LogError($"{formatted}\n{exception}");
            else
                UnityEngine.Debug.LogError(formatted);
        }

        private string Format(string level, string message, object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            return $"[{_prefix}][{level}] {formatted}";
        }
    }

    /// <summary>
    /// Default connectivity checker using Unity's Network Reachability.
    /// </summary>
    public sealed class UnityConnectivity : IConnectivity
    {
        private bool _lastKnownState;
        public event Action<bool>? OnConnectivityChanged;

        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;

        public void CheckAndNotify()
        {
            var current = IsOnline;
            if (current != _lastKnownState)
            {
                _lastKnownState = current;
                OnConnectivityChanged?.Invoke(current);
            }
        }
    }

    /// <summary>
    /// File-based storage implementation for persistence.
    /// </summary>
    public sealed class FileStorage : IStorage
    {
        private readonly string _basePath;
        private readonly object _lock = new();

        public FileStorage(string? basePath = null)
        {
            _basePath = basePath ?? Path.Combine(Application.persistentDataPath, "backend_sdk");
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }
        }

        public Task<string?> ReadAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var path = GetPath(key);
            lock (_lock)
            {
                if (!File.Exists(path))
                    return Task.FromResult<string?>(null);

                var content = File.ReadAllText(path);
                return Task.FromResult<string?>(content);
            }
        }

        public Task WriteAsync(string key, string data, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var path = GetPath(key);
            var tempPath = path + ".tmp";

            lock (_lock)
            {
                // Write-then-rename for crash safety
                File.WriteAllText(tempPath, data);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var path = GetPath(key);
            lock (_lock)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var path = GetPath(key);
            return Task.FromResult(File.Exists(path));
        }

        private string GetPath(string key)
        {
            // Sanitize key to be a valid filename
            var sanitized = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_basePath, sanitized);
        }
    }

    /// <summary>
    /// JSON serializer for backend persistence and transport payloads.
    /// Newtonsoft.Json is required because backend DTOs intentionally expose
    /// properties and framework value types that Unity's JsonUtility cannot round-trip.
    /// </summary>
    public sealed class UnityJsonSerializer : ISerializer
    {
        public string Serialize<T>(T value)
        {
            if (value == null)
                return "null";

            return Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }

        public T? Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null")
                return default;

            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return default;
            }
        }
    }
}
