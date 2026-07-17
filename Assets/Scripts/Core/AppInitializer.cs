using UnityEngine;

namespace Serhat.Forge.Core
{
    /// <summary>
    /// Applies explicitly enabled app-wide runtime policies before the first scene.
    /// Missing or disabled settings leave Unity project settings untouched.
    /// </summary>
    public static class AppInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var settings = Resources.Load<AppRuntimeSettings>(AppRuntimeSettings.ResourceName);
            if (settings == null || !settings.ApplyFramePolicy)
            {
                return;
            }

            QualitySettings.vSyncCount = settings.VSyncCount;
            Application.targetFrameRate = settings.TargetFrameRate;

            Debug.Log(
                $"[AppInitializer] Applied frame policy: " +
                $"vSyncCount={QualitySettings.vSyncCount}, " +
                $"targetFrameRate={Application.targetFrameRate}.");
        }
    }
}
