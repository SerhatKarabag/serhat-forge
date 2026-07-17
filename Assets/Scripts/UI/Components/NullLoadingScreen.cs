using System;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Null-object implementation of ILoadingScreen.
    /// Used when no LoadingScreen MonoBehaviour exists in the scene,
    /// preventing NullReferenceExceptions throughout the codebase.
    /// Follows the same pattern as NullAudioService, NullAnalyticsService, etc.
    /// </summary>
    public sealed class NullLoadingScreen : ILoadingScreen
    {
        public event Action Hidden
        {
            add { }
            remove { }
        }

        public bool IsVisible => false;

        public void Show(Action onReady = null)
        {
            onReady?.Invoke();
        }

        public void Hide(Action onBeforeFadeOut = null, Action onComplete = null)
        {
            onBeforeFadeOut?.Invoke();
            onComplete?.Invoke();
        }

        public void AcquireHold()
        {
        }

        public void ReleaseHold()
        {
        }
    }
}
