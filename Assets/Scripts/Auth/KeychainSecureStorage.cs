#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Auth
{
    public class KeychainSecureStorage : ISecureStorage
    {
        private const string PersistentKeyName = "persistent_device_key";
        private const int KeychainSuccess = 0;
        private const int KeychainItemNotFound = -25300;


        private static string ServiceName => string.IsNullOrWhiteSpace(Application.identifier)
            ? "com.serhat.forge.auth"
            : $"{Application.identifier}.auth";

        [DllImport("__Internal")]
        private static extern int _KeychainGet(string service, string key, out IntPtr value);

        [DllImport("__Internal")]
        private static extern void _KeychainFree(IntPtr value);

        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool _KeychainSet(string service, string key, string value);

        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool _KeychainDelete(string service, string key);

        public async Task<Result<string>> GetOrCreatePersistentKeyAsync()
        {
            var existing = await GetKeyAsync(PersistentKeyName);
            if (!existing.IsSuccess)
                return existing;
            if (!string.IsNullOrEmpty(existing.Value))
                return existing;

            var newKey = Guid.NewGuid().ToString();
            var setResult = await SetKeyAsync(PersistentKeyName, newKey);

            return setResult.IsSuccess
                ? Result<string>.Success(newKey)
                : Result<string>.Failure(new AuthError(AuthErrorCode.SecureStorageError,
                    "Güvenlik anahtarı oluşturulamadı.", "Keychain set failed"));
        }

        public Task<Result<string>> GetKeyAsync(string keyName)
        {
            var nativeValue = IntPtr.Zero;
            try
            {
                var status = _KeychainGet(ServiceName, keyName, out nativeValue);
                if (status == KeychainItemNotFound)
                    return Task.FromResult(Result<string>.Success(string.Empty));
                if (status != KeychainSuccess)
                {
                    return Task.FromResult(Result<string>.Failure(
                        new AuthError(
                            AuthErrorCode.SecureStorageError,
                            "Güvenlik anahtarı okunamadı.",
                            $"Keychain get failed with OSStatus {status}.")));
                }
                if (nativeValue == IntPtr.Zero)
                {
                    return Task.FromResult(Result<string>.Failure(
                        new AuthError(AuthErrorCode.SecureStorageError,
                            "Güvenlik anahtarı okunamadı.", "Keychain returned no value.")));
                }

                var value = Marshal.PtrToStringUTF8(nativeValue) ?? string.Empty;
                return Task.FromResult(Result<string>.Success(value));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<string>.Failure(
                    new AuthError(AuthErrorCode.SecureStorageError,
                        "Güvenlik anahtarı okunamadı.", $"Keychain get failed: {ex.Message}",
                        innerException: ex)));
            }
            finally
            {
                if (nativeValue != IntPtr.Zero)
                    _KeychainFree(nativeValue);
            }
        }

        public Task<Result<bool>> SetKeyAsync(string keyName, string value)
        {
            try
            {
                bool success = _KeychainSet(ServiceName, keyName, value);
                return Task.FromResult(success
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure(new AuthError(AuthErrorCode.SecureStorageError,
                        "Güvenlik anahtarı kaydedilemedi.", "Keychain set returned false")));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool>.Failure(
                    new AuthError(AuthErrorCode.SecureStorageError,
                        "Güvenlik anahtarı kaydedilemedi.", $"Keychain set exception: {ex.Message}",
                        innerException: ex)));
            }
        }

        public Task<Result<bool>> DeleteKeyAsync(string keyName)
        {
            try
            {
                bool success = _KeychainDelete(ServiceName, keyName);
                return Task.FromResult(Result<bool>.Success(success));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool>.Failure(
                    new AuthError(AuthErrorCode.SecureStorageError,
                        "Anahtar silinemedi.", $"Keychain delete exception: {ex.Message}",
                        innerException: ex)));
            }
        }
    }
}
#endif
