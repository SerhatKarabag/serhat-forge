using System;
using System.Collections;
using System.Collections.Generic;
using Serhat.Forge.Ads;
using Zenject;
using UnityEngine;

namespace Serhat.Forge.Audio
{
    /// <summary>
    /// Manages all audio in the game including background music and sound effects.
    /// Supports different music tracks for different game states (menu, gameplay, etc.).
    /// Auto-pauses music while a fullscreen ad (interstitial/rewarded) is on screen.
    /// </summary>
    public class SoundManager : MonoBehaviour, IAudioService
    {
        #region Inspector Fields

        [Header("Audio Sources")]
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private AudioSource _sfxSource;

        [Header("Music Tracks")]
        [SerializeField] private MusicTrack[] _musicTracks;

        [Header("Common SFX")]
        [SerializeField] private AudioClip _buttonClickSFX;
        [SerializeField] private AudioClip _popupOpenSFX;

        [Header("Settings")]
        [Tooltip("Duration of crossfade between music tracks")]
        [SerializeField] private float _crossfadeDuration = 1.5f;

        [Range(0f, 1f)]
        [SerializeField] private float _musicVolume = 0.5f;

        [Range(0f, 1f)]
        [SerializeField] private float _sfxVolume = 1f;

        [Header("Startup")]
        [SerializeField] private MusicType _startupMusic = MusicType.None;

        #endregion

        #region Private Fields

        private MusicType _currentMusicType = MusicType.None;
        private Coroutine _crossfadeCoroutine;
        private bool _isMusicMuted;
        private bool _isSfxMuted;
        private Dictionary<MusicType, AudioClip> _musicDictionary;
        private IAdService _adService;
        private bool _isAdServiceSubscribed;
        private int _activeFullscreenAdCount;
        private bool _pausedByFullscreenAd;

        private const string MUSIC_VOLUME_KEY = "MusicVolume";
        private const string SFX_VOLUME_KEY = "SFXVolume";
        private const string MUSIC_MUTED_KEY = "MusicMuted";
        private const string SFX_MUTED_KEY = "SFXMuted";

        #endregion

        #region Unity Lifecycle

        [Inject]
        private void Construct(IAdService adService)
        {
            BindAdService(adService);
        }

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            if (_startupMusic != MusicType.None)
            {
                PlayMusic(_startupMusic);
            }

        }

        private void OnDestroy()
        {
            UnbindAdServiceEvents();
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_musicSource == null)
            {
                _musicSource = gameObject.AddComponent<AudioSource>();
                _musicSource.loop = true;
                _musicSource.playOnAwake = false;
            }

            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
                _sfxSource.loop = false;
                _sfxSource.playOnAwake = false;
            }

            _musicDictionary = new Dictionary<MusicType, AudioClip>();
            if (_musicTracks != null)
            {
                foreach (var track in _musicTracks)
                {
                    if (track.clip != null && !_musicDictionary.ContainsKey(track.type))
                        _musicDictionary[track.type] = track.clip;
                }
            }

            LoadSettings();
            ApplyMusicVolume();
            ApplySfxVolume();
        }

        private void LoadSettings()
        {
            _musicVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, _musicVolume);
            _sfxVolume = PlayerPrefs.GetFloat(SFX_VOLUME_KEY, _sfxVolume);
            _isMusicMuted = PlayerPrefs.GetInt(MUSIC_MUTED_KEY, 0) == 1;
            _isSfxMuted = PlayerPrefs.GetInt(SFX_MUTED_KEY, 0) == 1;
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, _musicVolume);
            PlayerPrefs.SetFloat(SFX_VOLUME_KEY, _sfxVolume);
            PlayerPrefs.SetInt(MUSIC_MUTED_KEY, _isMusicMuted ? 1 : 0);
            PlayerPrefs.SetInt(SFX_MUTED_KEY, _isSfxMuted ? 1 : 0);
            PlayerPrefs.Save();
        }

        #endregion

        #region Music Control

        public void PlayMusic(MusicType musicType, bool crossfade = true)
        {
            if (_isMusicMuted)
            {
                _currentMusicType = musicType;
                return;
            }

            if (musicType == _currentMusicType && _musicSource.isPlaying)
                return;

            if (!_musicDictionary.TryGetValue(musicType, out AudioClip clip))
            {
                Debug.LogWarning($"[SoundManager] Music track not found for type: {musicType}");
                return;
            }

            _currentMusicType = musicType;

            if (crossfade && _musicSource.isPlaying)
            {
                if (_crossfadeCoroutine != null)
                    StopCoroutine(_crossfadeCoroutine);
                _crossfadeCoroutine = StartCoroutine(CrossfadeMusic(clip));
            }
            else
            {
                _musicSource.clip = clip;
                _musicSource.Play();
            }
        }

        public void StopMusic(bool fadeOut = true)
        {
            if (!_musicSource.isPlaying)
                return;

            _currentMusicType = MusicType.None;

            if (fadeOut)
            {
                if (_crossfadeCoroutine != null)
                    StopCoroutine(_crossfadeCoroutine);
                _crossfadeCoroutine = StartCoroutine(FadeOutMusic());
            }
            else
            {
                _musicSource.Stop();
            }
        }

        public void PauseMusic() => _musicSource.Pause();
        public void ResumeMusic() => _musicSource.UnPause();

        private IEnumerator CrossfadeMusic(AudioClip newClip)
        {
            float timer = 0f;
            float startVolume = _musicSource.volume;
            float targetVolume = _isMusicMuted ? 0f : _musicVolume;

            while (timer < _crossfadeDuration / 2f)
            {
                timer += Time.unscaledDeltaTime;
                _musicSource.volume = Mathf.Lerp(startVolume, 0f, timer / (_crossfadeDuration / 2f));
                yield return null;
            }

            _musicSource.clip = newClip;
            _musicSource.Play();

            timer = 0f;
            while (timer < _crossfadeDuration / 2f)
            {
                timer += Time.unscaledDeltaTime;
                _musicSource.volume = Mathf.Lerp(0f, targetVolume, timer / (_crossfadeDuration / 2f));
                yield return null;
            }

            _musicSource.volume = targetVolume;
            _crossfadeCoroutine = null;
        }

        private IEnumerator FadeOutMusic()
        {
            float timer = 0f;
            float startVolume = _musicSource.volume;

            while (timer < _crossfadeDuration)
            {
                timer += Time.unscaledDeltaTime;
                _musicSource.volume = Mathf.Lerp(startVolume, 0f, timer / _crossfadeDuration);
                yield return null;
            }

            _musicSource.Stop();
            _musicSource.volume = _isMusicMuted ? 0f : _musicVolume;
            _crossfadeCoroutine = null;
        }

        #endregion

        #region SFX Control

        public void PlaySFX(AudioClip clip)
        {
            if (clip == null || _isSfxMuted)
                return;
            _sfxSource.PlayOneShot(clip, _sfxVolume);
        }

        public void PlaySFX(AudioClip clip, float volumeScale)
        {
            if (clip == null || _isSfxMuted)
                return;
            _sfxSource.PlayOneShot(clip, _sfxVolume * volumeScale);
        }

        public void PlayButtonClick()
        {
            PlaySFX(_buttonClickSFX);
            Haptics.HapticHelper.Selection();
        }

        public void PlayPopupOpen()
        {
            PlaySFX(_popupOpenSFX);
        }

        public void PlayRandomSFX(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0 || _isSfxMuted)
                return;
            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            PlaySFX(clip);
        }

        #endregion

        #region Volume Control

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            ApplyMusicVolume();
            SaveSettings();
        }

        public void SetSFXVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            ApplySfxVolume();
            SaveSettings();
        }

        public float GetMusicVolume() => _musicVolume;
        public float GetSFXVolume() => _sfxVolume;

        private void ApplyMusicVolume() => _musicSource.volume = _isMusicMuted ? 0f : _musicVolume;
        private void ApplySfxVolume() => _sfxSource.volume = _isSfxMuted ? 0f : _sfxVolume;

        #endregion

        #region Mute Control

        public void ToggleMusicMute() => SetMusicMuted(!_isMusicMuted);
        public void ToggleSFXMute() => SetSFXMuted(!_isSfxMuted);

        public void SetMusicMuted(bool muted)
        {
            _isMusicMuted = muted;

            if (muted)
            {
                if (_musicSource.isPlaying)
                    _musicSource.Stop();
            }
            else
            {
                if (_currentMusicType != MusicType.None)
                    PlayMusic(_currentMusicType, false);
            }

            ApplyMusicVolume();
            SaveSettings();
        }

        public void SetSFXMuted(bool muted)
        {
            _isSfxMuted = muted;
            ApplySfxVolume();
            SaveSettings();
        }

        public bool IsMusicMuted() => _isMusicMuted;
        public bool IsSFXMuted() => _isSfxMuted;

        public void MuteAll()
        {
            SetMusicMuted(true);
            SetSFXMuted(true);
        }

        public void UnmuteAll()
        {
            SetMusicMuted(false);
            SetSFXMuted(false);
        }

        #endregion

        #region Ad Integration

        private void BindAdService(IAdService adService)
        {
            if (_isAdServiceSubscribed && ReferenceEquals(_adService, adService))
            {
                return;
            }

            UnbindAdServiceEvents();
            _adService = adService ?? throw new ArgumentNullException(nameof(adService));
            _adService.OnFullscreenAdOpened += HandleFullscreenAdOpened;
            _adService.OnFullscreenAdClosed += HandleFullscreenAdClosed;
            _isAdServiceSubscribed = true;
        }

        private void UnbindAdServiceEvents()
        {
            if (!_isAdServiceSubscribed || _adService == null)
                return;

            _adService.OnFullscreenAdOpened -= HandleFullscreenAdOpened;
            _adService.OnFullscreenAdClosed -= HandleFullscreenAdClosed;
            _adService = null;
            _isAdServiceSubscribed = false;
            _activeFullscreenAdCount = 0;
            _pausedByFullscreenAd = false;
        }

        private void HandleFullscreenAdOpened()
        {
            _activeFullscreenAdCount++;
            if (_activeFullscreenAdCount > 1)
                return;

            if (_musicSource == null || !_musicSource.isPlaying || _isMusicMuted)
                return;

            if (_crossfadeCoroutine != null)
            {
                StopCoroutine(_crossfadeCoroutine);
                _crossfadeCoroutine = null;
            }

            PauseMusic();
            _pausedByFullscreenAd = true;
        }

        private void HandleFullscreenAdClosed()
        {
            if (_activeFullscreenAdCount > 0)
                _activeFullscreenAdCount--;

            if (_activeFullscreenAdCount > 0 || !_pausedByFullscreenAd)
                return;

            _pausedByFullscreenAd = false;
            if (_isMusicMuted || _musicSource == null)
                return;

            ResumeMusic();
            ApplyMusicVolume();
        }

        #endregion

        public MusicType CurrentMusicType => _currentMusicType;
        public bool IsMusicPlaying => _musicSource != null && _musicSource.isPlaying;
    }

    public enum MusicType
    {
        None,
        Menu,
        Gameplay
    }

    [Serializable]
    public class MusicTrack
    {
        public MusicType type;
        public AudioClip clip;
    }
}
