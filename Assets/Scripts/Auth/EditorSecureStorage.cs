using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Auth
{
    /// <summary>Editor-only insecure storage for testing. Uses PlayerPrefs.</summary>
    public class EditorSecureStorage : ISecureStorage
    {
        private const string PERSISTENT_KEY_NAME = "editor_persistent_device_key";

        public Task<Result<string>> GetOrCreatePersistentKeyAsync()
        {
            string existing = PlayerPrefs.GetString(PERSISTENT_KEY_NAME, string.Empty);
            if (!string.IsNullOrEmpty(existing))
                return Task.FromResult(Result<string>.Success(existing));

            string newKey = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(PERSISTENT_KEY_NAME, newKey);
            PlayerPrefs.Save();
            return Task.FromResult(Result<string>.Success(newKey));
        }

        public Task<Result<string>> GetKeyAsync(string keyName) =>
            Task.FromResult(Result<string>.Success(PlayerPrefs.GetString(keyName, string.Empty)));

        public Task<Result<bool>> SetKeyAsync(string keyName, string value)
        {
            PlayerPrefs.SetString(keyName, value);
            PlayerPrefs.Save();
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> DeleteKeyAsync(string keyName)
        {
            PlayerPrefs.DeleteKey(keyName);
            PlayerPrefs.Save();
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
