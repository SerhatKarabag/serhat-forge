using System;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Abstraction for the full-screen loading overlay.
    /// Show before heavy operations (level load, scene transitions),
    /// Hide when the work is done. Minimum display time is handled internally.
    /// </summary>
    public interface ILoadingScreen
    {
        /// <summary>
        /// Fired after the loading screen has fully faded out and is no longer visible.
        /// </summary>
        event Action Hidden;

        /// <summary>True while the screen is visible or fading.</summary>
        bool IsVisible { get; }

        /// <summary>Fade in the loading screen. Optional callback when fully visible.</summary>
        void Show(Action onReady = null);

        /// <summary>
        /// Fade out the loading screen.
        /// Respects minimum display time before starting fade.
        /// <paramref name="onBeforeFadeOut"/> fires while the screen is still fully opaque
        /// — use for camera switches or scene cleanup that must happen invisibly.
        /// <paramref name="onComplete"/> fires after the fade-out finishes.
        /// </summary>
        void Hide(Action onBeforeFadeOut = null, Action onComplete = null);

        /// <summary>
        /// Prevents a pending Hide from fading out until the caller releases the hold.
        /// Holds are reference-counted.
        /// </summary>
        void AcquireHold();

        /// <summary>
        /// Releases a previously acquired hold. When the hold count reaches zero,
        /// a pending Hide may continue.
        /// </summary>
        void ReleaseHold();
    }
}
