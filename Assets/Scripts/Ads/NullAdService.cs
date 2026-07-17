using System;
using UnityEngine;

namespace Serhat.Forge.Ads
{
    /// <summary>
    /// Safe no-op fallback used when ad runtime settings disable ads.
    /// Prevents ad SDK initialization and disables ad buttons in-place.
    /// </summary>
    public sealed class NullAdService : IAdService
    {
        public static NullAdService Instance { get; } = new();

        public bool IsWatchingAd => false;

        public event Action OnRewardedLoaded
        {
            add { }
            remove { }
        }

        public event Action OnRewardedLoadFailed
        {
            add { }
            remove { }
        }

        public event Action OnRewardedClosed
        {
            add { }
            remove { }
        }

        public event Action OnRewardGranted
        {
            add { }
            remove { }
        }

        public event Action OnInterstitialLoaded
        {
            add { }
            remove { }
        }

        public event Action OnInterstitialClosed
        {
            add { }
            remove { }
        }

        public event Action OnFullscreenAdOpened
        {
            add { }
            remove { }
        }

        public event Action OnFullscreenAdClosed
        {
            add { }
            remove { }
        }

        public event Action<AdRevenueData> OnAdRevenuePaid
        {
            add { }
            remove { }
        }

        private NullAdService()
        {
        }

        public bool IsRewardedReady()
        {
            return false;
        }

        public void ShowRewarded(Action onRewardGranted = null)
        {
        }

        public bool IsInterstitialReady()
        {
            return false;
        }

        public void ShowInterstitial()
        {
        }

        public void ShowBanner()
        {
        }

        public void HideBanner()
        {
        }

        public void RegisterAdButton(IAdButton button)
        {
            if (button?.GameObject == null)
            {
                return;
            }

            button.SetAvailable(false);
            button.SetLoading(false);
        }

        public void UnregisterAdButton(IAdButton button)
        {
        }
    }
}
