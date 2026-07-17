using UnityEngine;

namespace Serhat.Forge.Audio
{
    /// <summary>
    /// Interface for music playback operations only.
    /// Clients that only need music control should depend on this.
    /// </summary>
    public interface IMusicService
    {
        MusicType CurrentMusicType { get; }
        bool IsMusicPlaying { get; }
        void PlayMusic(MusicType musicType, bool crossfade = true);
        void StopMusic(bool fadeOut = true);
        void PauseMusic();
        void ResumeMusic();
    }

    /// <summary>
    /// Interface for sound effects playback only.
    /// Clients that only need SFX should depend on this.
    /// </summary>
    public interface ISfxService
    {
        void PlayButtonClick();
        void PlayPopupOpen();
        void PlaySFX(AudioClip clip);
        void PlaySFX(AudioClip clip, float volumeScale);
        void PlayRandomSFX(AudioClip[] clips);
    }

    /// <summary>
    /// Interface for audio volume control only.
    /// </summary>
    public interface IAudioVolumeService
    {
        void SetMusicVolume(float volume);
        void SetSFXVolume(float volume);
        float GetMusicVolume();
        float GetSFXVolume();
    }

    /// <summary>
    /// Interface for audio mute control only.
    /// </summary>
    public interface IAudioMuteService
    {
        void ToggleMusicMute();
        void ToggleSFXMute();
        void SetMusicMuted(bool muted);
        void SetSFXMuted(bool muted);
        bool IsMusicMuted();
        bool IsSFXMuted();
        void MuteAll();
        void UnmuteAll();
    }

    /// <summary>
    /// Composite interface for full audio management.
    /// </summary>
    public interface IAudioService : IMusicService, ISfxService, IAudioVolumeService, IAudioMuteService
    {
    }
}
