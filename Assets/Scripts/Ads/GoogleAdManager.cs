#if GOOGLE_MOBILE_ADS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using Zenject;
using UnityEngine;

namespace Serhat.Forge.Ads
{
    /// <summary>
    /// Google AdMob implementation of IAdService.
    /// Drop-in replacement for the AppLovin MAX AdManager.
    /// </summary>
    public sealed class GoogleAdManager : MonoBehaviour, IAdService, IInitializable
    {
        private const string TAG = "[GoogleAdManager]";

        public enum BannerPos { Top, Bottom }

        [Header("Enabled Ad Types")]
        [SerializeField] private bool enableRewarded = true;
        [SerializeField] private bool enableInterstitial = true;
        [SerializeField] private bool enableBanner;

        [Header("Rewarded Ad Unit IDs")]
        [SerializeField] private string androidRewardedId = "";
        [SerializeField] private string iosRewardedId = "";

        [Header("Interstitial Ad Unit IDs")]
        [SerializeField] private string androidInterstitialId = "";
        [SerializeField] private string iosInterstitialId = "";

        [Header("Banner Ad Unit IDs")]
        [SerializeField] private string androidBannerId = "";
        [SerializeField] private string iosBannerId = "";

        [Header("Banner Settings")]
        [SerializeField] private BannerPos bannerPosition = BannerPos.Bottom;
        [SerializeField] private bool showBannerOnInit;

        [Header("Retry Settings")]
        [SerializeField] private int maxRetryExponent = 3;

        private string RewardedId =>
#if UNITY_IOS
            iosRewardedId;
#else
            androidRewardedId;
#endif

        private string InterstitialId =>
#if UNITY_IOS
            iosInterstitialId;
#else
            androidInterstitialId;
#endif

        private string BannerId =>
#if UNITY_IOS
            iosBannerId;
#else
            androidBannerId;
#endif

        private RewardedAd _rewardedAd;
        private InterstitialAd _interstitialAd;
        private BannerView _bannerView;

        private int _rewardedRetryAttempt;
        private int _interstitialRetryAttempt;
        private Action _pendingRewardCallback;
        private bool _rewardEarnedThisSession;
        private bool _initializationStarted;
        private readonly List<IAdButton> _adButtons = new();

        /// <summary>
        /// Thread-safe queue for dispatching AdMob callbacks to the Unity main thread.
        /// AdMob fires callbacks on the Java/Android thread — Unity API calls on
        /// background threads silently fail or freeze the app.
        /// </summary>
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        /// <summary>
        /// Optional callback invoked when an ad impression occurs. Hook into your analytics here.
        /// Args: (adType, networkName).
        /// </summary>
        public static Action<string, string> OnImpression;

        /// <summary>
        /// Optional callback to determine whether interruptive ads (interstitials)
        /// should be skipped, e.g. when the player has bought a "remove ads" entitlement.
        /// Default behaviour is to always show interstitials.
        /// </summary>
        public static Func<bool> AreInterruptiveAdsDisabledOverride;

        public bool IsWatchingAd { get; private set; }

        // ── Events ──
        public event Action OnRewardedLoaded;
        public event Action OnRewardedLoadFailed;
        public event Action OnRewardedClosed;
        public event Action OnRewardGranted;
        public event Action OnInterstitialLoaded;
        public event Action OnInterstitialClosed;
        public event Action OnFullscreenAdOpened;
        public event Action OnFullscreenAdClosed;
        public event Action<AdRevenueData> OnAdRevenuePaid;

        /// <summary>
        /// Drains the callback queue on the main thread every frame.
        /// </summary>
        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>Enqueue an action to run on the Unity main thread next frame.</summary>
        private void RunOnMainThread(Action action) => _mainThreadQueue.Enqueue(action);

        #region Initialization
        public void Initialize()
        {
            if (_initializationStarted)
            {
                return;
            }

            _initializationStarted = true;
            Debug.Log($"{TAG} Initialize() called. Rewarded={enableRewarded}, Interstitial={enableInterstitial}, Banner={enableBanner}");
            Debug.Log($"{TAG} Ad Unit IDs → Rewarded=\"{RewardedId}\", Interstitial=\"{InterstitialId}\", Banner=\"{BannerId}\"");

            MobileAds.Initialize(status => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} ✓ SDK initialized successfully.");

                // Log adapter status for each network
                var adapterMap = status.getAdapterStatusMap();
                foreach (var kvp in adapterMap)
                {
                    Debug.Log($"{TAG}   Adapter: {kvp.Key} | State={kvp.Value.InitializationState} | Latency={kvp.Value.Latency}ms | Desc={kvp.Value.Description}");
                }

                OnSdkInitialized();
            }));
        }

        private void OnSdkInitialized()
        {
            if (enableRewarded && !string.IsNullOrEmpty(RewardedId))
            {
                Debug.Log($"{TAG} Starting Rewarded ad load (ID: {RewardedId})...");
                LoadRewarded();
            }
            else
            {
                Debug.Log($"{TAG} Rewarded SKIPPED (enabled={enableRewarded}, hasId={!string.IsNullOrEmpty(RewardedId)})");
            }

            if (enableInterstitial && !string.IsNullOrEmpty(InterstitialId))
            {
                Debug.Log($"{TAG} Starting Interstitial ad load (ID: {InterstitialId})...");
                LoadInterstitial();
            }
            else
            {
                Debug.Log($"{TAG} Interstitial SKIPPED (enabled={enableInterstitial}, hasId={!string.IsNullOrEmpty(InterstitialId)})");
            }

            if (enableBanner && !string.IsNullOrEmpty(BannerId))
            {
                Debug.Log($"{TAG} Starting Banner init (ID: {BannerId}, pos={bannerPosition})...");
                InitializeBanner();
            }
            else
            {
                Debug.Log($"{TAG} Banner SKIPPED (enabled={enableBanner}, hasId={!string.IsNullOrEmpty(BannerId)})");
            }
        }
        #endregion

        #region Rewarded
        private void LoadRewarded()
        {
            Debug.Log($"{TAG} [Rewarded] Loading... (attempt #{_rewardedRetryAttempt + 1})");
            SetAdButtonsLoading(true);

            var request = new AdRequest();
            RewardedAd.Load(RewardedId, request, (ad, error) => RunOnMainThread(() =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"{TAG} [Rewarded] ✗ Load FAILED: {error.GetMessage()} (code={error.GetCode()})");
                    _rewardedRetryAttempt++;
                    float delay = (float)Math.Pow(2, Math.Min(maxRetryExponent, _rewardedRetryAttempt));
                    Debug.Log($"{TAG} [Rewarded] Will retry in {delay}s (attempt #{_rewardedRetryAttempt})");
                    Invoke(nameof(LoadRewarded), delay);
                    OnRewardedLoadFailed?.Invoke();
                    return;
                }

                Debug.Log($"{TAG} [Rewarded] ✓ Loaded successfully! CanShow={ad.CanShowAd()}");
                _rewardedAd = ad;
                _rewardedRetryAttempt = 0;
                SetAdButtonsLoading(false);
                RegisterRewardedEvents(ad);
                OnRewardedLoaded?.Invoke();
            }));
        }

        private void RegisterRewardedEvents(RewardedAd ad)
        {
            ad.OnAdFullScreenContentOpened += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Rewarded] ▶ Ad opened (fullscreen).");
                IsWatchingAd = true;
                OnFullscreenAdOpened?.Invoke();
            });

            ad.OnAdFullScreenContentClosed += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Rewarded] ■ Ad closed. Reloading next ad...");
                IsWatchingAd = false;
                OnFullscreenAdClosed?.Invoke();
                if (_rewardEarnedThisSession)
                {
                    _pendingRewardCallback?.Invoke();
                    OnRewardGranted?.Invoke();
                }

                _pendingRewardCallback = null;
                _rewardEarnedThisSession = false;
                OnRewardedClosed?.Invoke();
                LoadRewarded();
            });

            ad.OnAdFullScreenContentFailed += (error) => RunOnMainThread(() =>
            {
                Debug.LogWarning($"{TAG} [Rewarded] ✗ Display FAILED: {error.GetMessage()} (code={error.GetCode()})");
                IsWatchingAd = false;
                OnFullscreenAdClosed?.Invoke();
                _pendingRewardCallback = null;
                _rewardEarnedThisSession = false;
                LoadRewarded();
            });

            ad.OnAdImpressionRecorded += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Rewarded] Impression recorded.");
                OnImpression?.Invoke("rewarded", ad.GetResponseInfo()?.GetMediationAdapterClassName() ?? "");
            });

            ad.OnAdClicked += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Rewarded] Ad clicked.");
            });

            ad.OnAdPaid += (adValue) => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Rewarded] $ Revenue: {adValue.Value / 1_000_000d:F6} {adValue.CurrencyCode} (precision={adValue.Precision})");
                OnAdRevenuePaid?.Invoke(new AdRevenueData
                {
                    AdUnitId = RewardedId,
                    Revenue = adValue.Value / 1_000_000d,
                    NetworkName = ad.GetResponseInfo()?.GetMediationAdapterClassName() ?? "",
                    Placement = "",
                    RevenuePrecision = adValue.Precision.ToString()
                });
            });
        }

        public bool IsRewardedReady() =>
            enableRewarded && _rewardedAd != null && _rewardedAd.CanShowAd();

        public void ShowRewarded(Action onRewardGranted = null)
        {
            Debug.Log($"{TAG} [Rewarded] ShowRewarded() called. IsReady={IsRewardedReady()}");

            if (IsRewardedReady())
            {
                _pendingRewardCallback = onRewardGranted;
                _rewardEarnedThisSession = false;
                _rewardedAd.Show(reward => RunOnMainThread(() =>
                {
                    Debug.Log($"{TAG} [Rewarded] ★ REWARD GRANTED! Type={reward.Type}, Amount={reward.Amount}");
                    _rewardEarnedThisSession = true;
                }));
            }
            else
            {
                Debug.Log($"{TAG} [Rewarded] Not ready — triggering load...");
                LoadRewarded();
            }
        }
        #endregion

        #region Interstitial
        private void LoadInterstitial()
        {
            Debug.Log($"{TAG} [Interstitial] Loading... (attempt #{_interstitialRetryAttempt + 1})");

            var request = new AdRequest();
            InterstitialAd.Load(InterstitialId, request, (ad, error) => RunOnMainThread(() =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"{TAG} [Interstitial] ✗ Load FAILED: {error.GetMessage()} (code={error.GetCode()})");
                    _interstitialRetryAttempt++;
                    float delay = (float)Math.Pow(2, Math.Min(maxRetryExponent, _interstitialRetryAttempt));
                    Debug.Log($"{TAG} [Interstitial] Will retry in {delay}s (attempt #{_interstitialRetryAttempt})");
                    Invoke(nameof(LoadInterstitial), delay);
                    return;
                }

                Debug.Log($"{TAG} [Interstitial] ✓ Loaded successfully! CanShow={ad.CanShowAd()}");
                _interstitialAd = ad;
                _interstitialRetryAttempt = 0;
                RegisterInterstitialEvents(ad);
                OnInterstitialLoaded?.Invoke();
            }));
        }

        private void RegisterInterstitialEvents(InterstitialAd ad)
        {
            ad.OnAdFullScreenContentOpened += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Interstitial] ▶ Ad opened (fullscreen).");
                IsWatchingAd = true;
                OnFullscreenAdOpened?.Invoke();
            });

            ad.OnAdFullScreenContentClosed += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Interstitial] ■ Ad closed. Reloading next ad...");
                IsWatchingAd = false;
                OnFullscreenAdClosed?.Invoke();
                OnInterstitialClosed?.Invoke();
                LoadInterstitial();
            });

            ad.OnAdFullScreenContentFailed += (error) => RunOnMainThread(() =>
            {
                Debug.LogWarning($"{TAG} [Interstitial] ✗ Display FAILED: {error.GetMessage()} (code={error.GetCode()})");
                IsWatchingAd = false;
                OnFullscreenAdClosed?.Invoke();
                OnInterstitialClosed?.Invoke();
                LoadInterstitial();
            });

            ad.OnAdImpressionRecorded += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Interstitial] Impression recorded.");
                OnImpression?.Invoke("interstitial", ad.GetResponseInfo()?.GetMediationAdapterClassName() ?? "");
            });

            ad.OnAdClicked += () => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Interstitial] Ad clicked.");
            });

            ad.OnAdPaid += (adValue) => RunOnMainThread(() =>
            {
                Debug.Log($"{TAG} [Interstitial] $ Revenue: {adValue.Value / 1_000_000d:F6} {adValue.CurrencyCode} (precision={adValue.Precision})");
                OnAdRevenuePaid?.Invoke(new AdRevenueData
                {
                    AdUnitId = InterstitialId,
                    Revenue = adValue.Value / 1_000_000d,
                    NetworkName = ad.GetResponseInfo()?.GetMediationAdapterClassName() ?? "",
                    Placement = "",
                    RevenuePrecision = adValue.Precision.ToString()
                });
            });
        }

        public bool IsInterstitialReady() =>
            !AreInterruptiveAdsDisabled() &&
            enableInterstitial &&
            _interstitialAd != null &&
            _interstitialAd.CanShowAd();

        public void ShowInterstitial()
        {
            Debug.Log($"{TAG} [Interstitial] ShowInterstitial() called. IsReady={IsInterstitialReady()}");

            if (AreInterruptiveAdsDisabled())
            {
                Debug.Log($"{TAG} [Interstitial] Skipped because ads are removed for this player.");
                return;
            }

            if (IsInterstitialReady())
                _interstitialAd.Show();
            else
            {
                Debug.Log($"{TAG} [Interstitial] Not ready — triggering load...");
                LoadInterstitial();
            }
        }
        #endregion

        #region Banner
        private void InitializeBanner()
        {
            if (AreInterruptiveAdsDisabled())
            {
                Debug.Log($"{TAG} [Banner] Initialization skipped because ads are removed for this player.");
                return;
            }

            var adPosition = bannerPosition == BannerPos.Top
                ? AdPosition.Top
                : AdPosition.Bottom;

            _bannerView = new BannerView(BannerId, AdSize.Banner, adPosition);
            Debug.Log($"{TAG} [Banner] Created BannerView (pos={bannerPosition}).");

            _bannerView.OnBannerAdLoaded += () =>
            {
                Debug.Log($"{TAG} [Banner] ✓ Loaded successfully!");
            };

            _bannerView.OnBannerAdLoadFailed += (error) =>
            {
                Debug.LogWarning($"{TAG} [Banner] ✗ Load FAILED: {error.GetMessage()} (code={error.GetCode()})");
            };

            _bannerView.OnAdImpressionRecorded += () =>
            {
                Debug.Log($"{TAG} [Banner] Impression recorded.");
            };

            _bannerView.OnAdClicked += () =>
            {
                Debug.Log($"{TAG} [Banner] Ad clicked.");
            };

            _bannerView.OnAdPaid += (adValue) =>
            {
                Debug.Log($"{TAG} [Banner] $ Revenue: {adValue.Value / 1_000_000d:F6} {adValue.CurrencyCode} (precision={adValue.Precision})");
                OnAdRevenuePaid?.Invoke(new AdRevenueData
                {
                    AdUnitId = BannerId,
                    Revenue = adValue.Value / 1_000_000d,
                    NetworkName = _bannerView.GetResponseInfo()?.GetMediationAdapterClassName() ?? "",
                    Placement = "",
                    RevenuePrecision = adValue.Precision.ToString()
                });
            };

            var request = new AdRequest();
            _bannerView.LoadAd(request);
            Debug.Log($"{TAG} [Banner] LoadAd() called.");

            if (!showBannerOnInit)
            {
                _bannerView.Hide();
                Debug.Log($"{TAG} [Banner] Hidden (showBannerOnInit=false).");
            }
        }

        public void ShowBanner()
        {
            Debug.Log($"{TAG} [Banner] ShowBanner() called. hasView={_bannerView != null}");
            if (AreInterruptiveAdsDisabled())
            {
                HideBanner();
                return;
            }

            if (enableBanner && _bannerView != null)
                _bannerView.Show();
        }

        public void HideBanner()
        {
            Debug.Log($"{TAG} [Banner] HideBanner() called.");
            if (enableBanner && _bannerView != null)
                _bannerView.Hide();
        }
        #endregion

        #region Ad Button Management
        public void RegisterAdButton(IAdButton button)
        {
            if (!_adButtons.Contains(button))
                _adButtons.Add(button);

            button?.SetAvailable(true);
            Debug.Log($"{TAG} AdButton registered. Total={_adButtons.Count}");
        }

        public void UnregisterAdButton(IAdButton button)
        {
            _adButtons.Remove(button);
            Debug.Log($"{TAG} AdButton unregistered. Total={_adButtons.Count}");
        }

        private void SetAdButtonsLoading(bool isLoading)
        {
            for (int i = _adButtons.Count - 1; i >= 0; i--)
            {
                if (_adButtons[i] == null || _adButtons[i].GameObject == null)
                {
                    _adButtons.RemoveAt(i);
                    continue;
                }

                if (isLoading)
                {
                    _adButtons[i].GameObject.SetActive(true);
                    _adButtons[i].SetLoading(true);
                    _adButtons[i].GameObject.SetActive(false);
                }
                else
                {
                    _adButtons[i].GameObject.SetActive(true);
                    _adButtons[i].SetLoading(false);
                }
            }
        }

        private bool AreInterruptiveAdsDisabled()
        {
            return AreInterruptiveAdsDisabledOverride?.Invoke() == true;
        }
        #endregion

        #region Cleanup
        private void OnDestroy()
        {
            Debug.Log($"{TAG} OnDestroy — cleaning up ads.");
            _rewardedAd?.Destroy();
            _interstitialAd?.Destroy();
            _bannerView?.Destroy();
        }
        #endregion
    }
}
#endif
