#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Analytics.Core
{
    /// <summary>
    /// Default system clock implementation.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();

        public DateTime UtcNow => DateTime.UtcNow;
        public long TimestampMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private SystemClock() { }
    }

    /// <summary>
    /// Default Unity-based logger implementation.
    /// </summary>
    public sealed class UnityAnalyticsLogger : IAnalyticsLogger
    {
        private const string Tag = "[Analytics]";

        public void Debug(string message, params object[] args)
        {
            UnityEngine.Debug.Log(FormatMessage("DEBUG", message, args));
        }

        public void Info(string message, params object[] args)
        {
            UnityEngine.Debug.Log(FormatMessage("INFO", message, args));
        }

        public void Warning(string message, params object[] args)
        {
            UnityEngine.Debug.LogWarning(FormatMessage("WARN", message, args));
        }

        public void Error(string message, Exception? exception = null, params object[] args)
        {
            var formattedMessage = FormatMessage("ERROR", message, args);
            if (exception != null)
            {
                UnityEngine.Debug.LogError($"{formattedMessage}\n{exception}");
            }
            else
            {
                UnityEngine.Debug.LogError(formattedMessage);
            }
        }

        private static string FormatMessage(string level, string message, object[] args)
        {
            try
            {
                var formatted = args.Length > 0 ? string.Format(message, args) : message;
                return $"{Tag} [{level}] {formatted}";
            }
            catch
            {
                return $"{Tag} [{level}] {message}";
            }
        }
    }

    /// <summary>
    /// Default Unity connectivity checker.
    /// </summary>
    public sealed class UnityConnectivity : IConnectivity
    {
        public bool IsOnline
        {
            get
            {
                if (TryGetOnlineState(out var isOnline))
                {
                    _lastOnlineState = isOnline;
                }

                return _lastOnlineState;
            }
        }
        public event Action<bool>? OnConnectivityChanged;

        private volatile bool _lastOnlineState = true;

        public UnityConnectivity()
        {
            _lastOnlineState = IsOnline;
        }

        /// <summary>
        /// Call this periodically to check for connectivity changes.
        /// </summary>
        public void CheckConnectivity()
        {
            if (!TryGetOnlineState(out var currentState))
            {
                return;
            }

            if (currentState != _lastOnlineState)
            {
                _lastOnlineState = currentState;
                OnConnectivityChanged?.Invoke(currentState);
            }
        }

        private static bool TryGetOnlineState(out bool isOnline)
        {
            try
            {
                isOnline = Application.internetReachability != NetworkReachability.NotReachable;
                return true;
            }
            catch (UnityException)
            {
                isOnline = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Default file-based storage implementation.
    /// </summary>
    public sealed class FileStorage : IStorage
    {
        private readonly string _basePath;

        public FileStorage(string subfolder = "analytics_sdk")
        {
            _basePath = Path.Combine(Application.persistentDataPath, subfolder);
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }
        }

        public Task<string?> ReadAsync(string key, CancellationToken ct = default)
        {
            var path = GetPath(key);
            if (!File.Exists(path))
            {
                return Task.FromResult<string?>(null);
            }

            try
            {
                var content = File.ReadAllText(path);
                return Task.FromResult<string?>(content);
            }
            catch (Exception)
            {
                return Task.FromResult<string?>(null);
            }
        }

        public Task WriteAsync(string key, string data, CancellationToken ct = default)
        {
            var path = GetPath(key);
            try
            {
                File.WriteAllText(path, data);
            }
            catch (Exception)
            {
                // Silently fail - analytics should not break the app
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            var path = GetPath(key);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Silently fail
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            var path = GetPath(key);
            return Task.FromResult(File.Exists(path));
        }

        private string GetPath(string key)
        {
            // Sanitize key for file system
            var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_basePath, $"{safeKey}.json");
        }
    }

    /// <summary>
    /// Default Unity JSON serializer.
    /// </summary>
    public sealed class UnityJsonSerializer : ISerializer
    {
        public string Serialize<T>(T value)
        {
            return JsonUtility.ToJson(value, false);
        }

        public T? Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return default;
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return default;
            }
        }
    }

    /// <summary>
    /// JSON serializer using Newtonsoft.Json for better dictionary support.
    /// Falls back to Unity JSON if Newtonsoft is not available.
    /// </summary>
    public sealed class AnalyticsJsonSerializer : ISerializer
    {
        public string Serialize<T>(T value)
        {
#if NEWTONSOFT_JSON
            return Newtonsoft.Json.JsonConvert.SerializeObject(value);
#else
            // For complex types with dictionaries, use a custom approach
            if (value is AnalyticsEvent evt)
            {
                return SerializeEvent(evt);
            }
            return JsonUtility.ToJson(value, false);
#endif
        }

        public T? Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return default;
            }

#if NEWTONSOFT_JSON
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return default;
            }
#else
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return default;
            }
#endif
        }

        private string SerializeEvent(AnalyticsEvent evt)
        {
            // Manual JSON building for events with dictionary parameters
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"EventId\":\"{0}\",", EscapeJson(evt.EventId));
            sb.AppendFormat("\"EventName\":\"{0}\",", EscapeJson(evt.EventName));
            sb.AppendFormat("\"Category\":\"{0}\",", EscapeJson(evt.Category));
            sb.AppendFormat("\"TimestampUtc\":\"{0:O}\",", evt.TimestampUtc);
            sb.AppendFormat("\"TimestampMs\":{0},", evt.TimestampMs);
            sb.AppendFormat("\"UserId\":{0},", evt.UserId != null ? $"\"{EscapeJson(evt.UserId)}\"" : "null");
            sb.AppendFormat("\"SessionId\":{0},", evt.SessionId != null ? $"\"{EscapeJson(evt.SessionId)}\"" : "null");
            sb.AppendFormat("\"SequenceNumber\":{0},", evt.SequenceNumber);
            sb.AppendFormat("\"RetryCount\":{0},", evt.RetryCount);
            sb.Append("\"Parameters\":{");

            var first = true;
            foreach (var kvp in evt.Parameters)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.AppendFormat("\"{0}\":{1}", EscapeJson(kvp.Key), SerializeValue(kvp.Value));
            }

            sb.Append("}}");
            return sb.ToString();
        }

        private string SerializeValue(object value)
        {
            return value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                string s => $"\"{EscapeJson(s)}\"",
                int or long or float or double or decimal => value.ToString()!,
                _ => $"\"{EscapeJson(value.ToString()!)}\""
            };
        }

        private string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
