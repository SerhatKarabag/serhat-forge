#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Analytics.Core.UserProperties
{
    /// <summary>
    /// Manages user properties and their persistence.
    /// </summary>
    public sealed class UserPropertyManager : IDisposable
    {
        private const string StorageKey = "analytics_user_properties";

        private readonly IStorage _storage;
        private readonly ISerializer _serializer;
        private readonly IAnalyticsLogger _logger;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private UserPropertyState _state = new();
        private bool _loaded;
        private bool _disposed;
        private bool _dirty;

        /// <summary>
        /// Current user ID.
        /// </summary>
        public string? UserId => _state.UserId;

        /// <summary>
        /// Current session ID.
        /// </summary>
        public string? SessionId { get; private set; }

        /// <summary>
        /// Event raised when user ID changes.
        /// </summary>
        public event Action<string?>? OnUserIdChanged;

        public UserPropertyManager(
            IStorage storage,
            ISerializer serializer,
            IAnalyticsLogger logger)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads user properties from storage.
        /// </summary>
        public async Task LoadAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var json = await _storage.ReadAsync(StorageKey, ct);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = _serializer.Deserialize<UserPropertyState>(json);
                    if (loaded != null)
                    {
                        _state = loaded;
                        _logger.Debug("Loaded user properties: UserId={0}, Properties={1}",
                            _state.UserId ?? "null", _state.Properties.Count);
                    }
                }
                _loaded = true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load user properties", ex);
                _state = new UserPropertyState();
                _loaded = true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Sets the user ID.
        /// </summary>
        public async Task SetUserIdAsync(string? userId, CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                if (_state.UserId == userId) return;

                var previousId = _state.UserId;
                _state.UserId = userId;
                _dirty = true;

                await SaveAsync(ct);
                _logger.Info("User ID changed: {0} -> {1}", previousId ?? "null", userId ?? "null");
            }
            finally
            {
                _lock.Release();
            }

            OnUserIdChanged?.Invoke(userId);
        }

        /// <summary>
        /// Sets a user property.
        /// </summary>
        public async Task SetPropertyAsync(string name, object? value, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                if (value == null)
                {
                    _state.Properties.Remove(name);
                }
                else
                {
                    _state.Properties[name] = value;
                }
                _dirty = true;

                await SaveAsync(ct);
                _logger.Debug("User property set: {0}={1}", name, value?.ToString() ?? "null");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Sets multiple user properties.
        /// </summary>
        public async Task SetPropertiesAsync(Dictionary<string, object> properties, CancellationToken ct = default)
        {
            if (properties == null || properties.Count == 0) return;

            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                foreach (var kvp in properties)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        _state.Properties[kvp.Key] = kvp.Value;
                    }
                }
                _dirty = true;

                await SaveAsync(ct);
                _logger.Debug("Set {0} user properties", properties.Count);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Gets a user property value.
        /// </summary>
        public object? GetProperty(string name)
        {
            EnsureLoaded();
            return _state.Properties.TryGetValue(name, out var value) ? value : null;
        }

        /// <summary>
        /// Gets all user properties.
        /// </summary>
        public IReadOnlyDictionary<string, object> GetAllProperties()
        {
            EnsureLoaded();
            return _state.Properties;
        }

        /// <summary>
        /// Clears the user ID (for logout).
        /// </summary>
        public async Task ClearUserIdAsync(CancellationToken ct = default)
        {
            await SetUserIdAsync(null, ct);
        }

        /// <summary>
        /// Clears all user properties.
        /// </summary>
        public async Task ClearAllPropertiesAsync(CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                _state.Properties.Clear();
                _dirty = true;
                await SaveAsync(ct);
                _logger.Info("Cleared all user properties");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Generates a new session ID.
        /// </summary>
        public string GenerateSessionId()
        {
            SessionId = Guid.NewGuid().ToString("N");
            _logger.Debug("Generated new session ID: {0}", SessionId);
            return SessionId;
        }

        /// <summary>
        /// Sets the session ID.
        /// </summary>
        public void SetSessionId(string? sessionId)
        {
            SessionId = sessionId;
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            if (!_dirty) return;

            try
            {
                var json = _serializer.Serialize(_state);
                await _storage.WriteAsync(StorageKey, json, ct);
                _dirty = false;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save user properties", ex);
            }
        }

        private void EnsureLoaded()
        {
            if (!_loaded)
            {
                throw new InvalidOperationException("User properties not loaded. Call LoadAsync() first.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.Dispose();
        }
    }

    /// <summary>
    /// Internal state for user properties.
    /// </summary>
    [Serializable]
    internal sealed class UserPropertyState
    {
        public string? UserId { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
    }
}
