using System;

namespace Serhat.Forge.Ads
{
    /// <summary>
    /// Abstraction for ad management (Rewarded, Interstitial, Banner).
    /// Implement with any ad SDK (AppLovin MAX, AdMob, IronSource, etc.).
    /// </summary>
    public interface IAdService
    {
        /// <summary>True while any fullscreen ad is being displayed.</summary>
        bool IsWatchingAd { get; }

        // ── Rewarded ──
        bool IsRewardedReady();
        void ShowRewarded(Action onRewardGranted = null);

        // ── Interstitial ──
        bool IsInterstitialReady();
        void ShowInterstitial();

        // ── Banner ──
        void ShowBanner();
        void HideBanner();

        // ── Events ──
        event Action OnRewardedLoaded;
        event Action OnRewardedLoadFailed;
        event Action OnRewardedClosed;
        event Action OnRewardGranted;
        event Action OnInterstitialLoaded;
        event Action OnInterstitialClosed;
        event Action OnFullscreenAdOpened;
        event Action OnFullscreenAdClosed;

        /// <summary>
        /// Fired when any ad generates revenue.
        /// Parameters: adUnitId, revenue, networkName, placement, precision.
        /// </summary>
        event Action<AdRevenueData> OnAdRevenuePaid;

        // ── Ad Button Registration ──
        void RegisterAdButton(IAdButton button);
        void UnregisterAdButton(IAdButton button);
    }

    /// <summary>
    /// Platform-agnostic ad revenue data.
    /// </summary>
    public struct AdRevenueData
    {
        public string AdUnitId;
        public double Revenue;
        public string NetworkName;
        public string Placement;
        public string RevenuePrecision;
    }

    /// <summary>
    /// Abstraction for ad button loading state.
    /// </summary>
    public interface IAdButton
    {
        void SetAvailable(bool isAvailable);
        void SetLoading(bool isLoading);
        UnityEngine.GameObject GameObject { get; }
    }
}
