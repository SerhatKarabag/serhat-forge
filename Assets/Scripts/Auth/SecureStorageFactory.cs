using UnityEngine;

namespace Serhat.Forge.Auth
{
    public static class SecureStorageFactory
    {
        public static ISecureStorage Create()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new KeychainSecureStorage();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSecureStorage();
#else
            return new EditorSecureStorage();
#endif
        }
    }
}
