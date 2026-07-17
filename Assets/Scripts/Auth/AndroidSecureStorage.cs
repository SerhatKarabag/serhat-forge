#if UNITY_ANDROID
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Auth
{
    /// <summary>
    /// Android secure storage using ANDROID_ID for persistent device identification.
    /// ANDROID_ID persists across app reinstalls (until factory reset).
    /// </summary>
    public class AndroidSecureStorage : ISecureStorage, IDisposable
    {
        private const string PLAYERPREFS_PREFIX = "SerhatForge_Auth_";

        private string _cachedAndroidId;
        private bool _disposed;

        public AndroidSecureStorage()
        {
            // Cache ANDROID_ID on initialization
            _cachedAndroidId = GetAndroidId();
        }

        /// <summary>
        /// Gets the Android device ID (Settings.Secure.ANDROID_ID).
        /// This ID persists across app reinstalls but resets on factory reset.
        /// </summary>
        private string GetAndroidId()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var contentResolver = currentActivity.Call<AndroidJavaObject>("getContentResolver");
                using var secureSettings = new AndroidJavaClass("android.provider.Settings$Secure");

                string androidId = secureSettings.CallStatic<string>("getString", contentResolver, "android_id");

                if (string.IsNullOrEmpty(androidId))
                {
                    Debug.LogWarning("[AndroidSecureStorage] ANDROID_ID is null or empty");
                    return null;
                }

                Debug.Log($"[AndroidSecureStorage] ANDROID_ID acquired: {MaskId(androidId)}");
                return androidId;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidSecureStorage] Failed to get ANDROID_ID: {ex.Message}");
                return null;
            }
        }

        private string MaskId(string id) =>
            string.IsNullOrEmpty(id) || id.Length < 8
                ? "***"
                : $"{id.Substring(0, 4)}...{id.Substring(id.Length - 4)}";

        public Task<Result<string>> GetOrCreatePersistentKeyAsync()
        {
            // Return cached ANDROID_ID - no storage needed, it's system-provided
            if (!string.IsNullOrEmpty(_cachedAndroidId))
            {
                return Task.FromResult(Result<string>.Success(_cachedAndroidId));
            }

            // Retry getting ANDROID_ID if it wasn't available at init
            _cachedAndroidId = GetAndroidId();
            if (!string.IsNullOrEmpty(_cachedAndroidId))
            {
                return Task.FromResult(Result<string>.Success(_cachedAndroidId));
            }

            // ANDROID_ID unavailable - this is rare but possible on some devices
            return Task.FromResult(Result<string>.Failure(
                new AuthError(AuthErrorCode.SecureStorageError,
                    "Cihaz kimliği alınamadı.",
                    "ANDROID_ID is unavailable on this device")));
        }

        public Task<Result<string>> GetKeyAsync(string keyName)
        {
            try
            {
                string value = PlayerPrefs.GetString(PLAYERPREFS_PREFIX + keyName, string.Empty);
                return Task.FromResult(Result<string>.Success(value ?? string.Empty));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<string>.Failure(
                    new AuthError(AuthErrorCode.SecureStorageError,
                        "Anahtar okunamadı.", $"Storage get failed: {ex.Message}",
                        innerException: ex)));
            }
        }

        public Task<Result<bool>> SetKeyAsync(string keyName, string value)
        {
            try
            {
                PlayerPrefs.SetString(PLAYERPREFS_PREFIX + keyName, value);
                PlayerPrefs.Save();
                return Task.FromResult(Result<bool>.Success(true));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool>.Failure(
                    new AuthError(AuthErrorCode.SecureStorageError,
                        "Anahtar kaydedilemedi.", $"Storage set exception: {ex.Message}",
                        innerException: ex)));
            }
        }

        public Task<Result<bool>> DeleteKeyAsync(string keyName)
        {
            try
            {
                PlayerPrefs.DeleteKey(PLAYERPREFS_PREFIX + keyName);
                PlayerPrefs.Save();
                return Task.FromResult(Result<bool>.Success(true));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool>.Failure(
                    new AuthError(AuthErrorCode.SecureStorageError,
                        "Anahtar silinemedi.", $"Storage delete exception: {ex.Message}",
                        innerException: ex)));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cachedAndroidId = null;
        }
    }
}
#endif
