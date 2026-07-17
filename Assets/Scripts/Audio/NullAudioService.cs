using UnityEngine;

namespace Serhat.Forge.Audio
{
    /// <summary>
    /// Stateful no-op audio service used when a project has not configured audio yet.
    /// </summary>
    public sealed class NullAudioService : IAudioService
    {
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;
        private bool _isMusicMuted;
        private bool _isSfxMuted;

        public MusicType CurrentMusicType => MusicType.None;
        public bool IsMusicPlaying => false;

        public void PlayMusic(MusicType musicType, bool crossfade = true) { }
        public void StopMusic(bool fadeOut = true) { }
        public void PauseMusic() { }
        public void ResumeMusic() { }
        public void PlayButtonClick() { }
        public void PlayPopupOpen() { }
        public void PlaySFX(AudioClip clip) { }
        public void PlaySFX(AudioClip clip, float volumeScale) { }
        public void PlayRandomSFX(AudioClip[] clips) { }

        public void SetMusicVolume(float volume) => _musicVolume = Mathf.Clamp01(volume);
        public void SetSFXVolume(float volume) => _sfxVolume = Mathf.Clamp01(volume);
        public float GetMusicVolume() => _musicVolume;
        public float GetSFXVolume() => _sfxVolume;

        public void ToggleMusicMute() => _isMusicMuted = !_isMusicMuted;
        public void ToggleSFXMute() => _isSfxMuted = !_isSfxMuted;
        public void SetMusicMuted(bool muted) => _isMusicMuted = muted;
        public void SetSFXMuted(bool muted) => _isSfxMuted = muted;
        public bool IsMusicMuted() => _isMusicMuted;
        public bool IsSFXMuted() => _isSfxMuted;

        public void MuteAll()
        {
            _isMusicMuted = true;
            _isSfxMuted = true;
        }

        public void UnmuteAll()
        {
            _isMusicMuted = false;
            _isSfxMuted = false;
        }
    }
}