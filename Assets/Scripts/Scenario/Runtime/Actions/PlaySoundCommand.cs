using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays an audio clip.
    /// </summary>
    [Serializable]
    public class PlaySoundCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<AudioClip> _audioClip = new ProxyObjectSaver<AudioClip>();

        [SerializeField]
        private ProxyObjectSaver<AudioSource> _audioSource = new ProxyObjectSaver<AudioSource>();

        [SerializeField]
        [Range(0f, 1f)]
        private float _volume = 1f;

        [SerializeField]
        private bool _loop = false;

        [SerializeField]
        private bool _waitForCompletion = false;

        private bool _isCancelled;
        private AudioSource _runtimeAudioSource;

        public AudioClip AudioClip
        {
            get => _audioClip.Value;
            set => _audioClip.Value = value;
        }

        public AudioSource AudioSource
        {
            get => _audioSource.Value;
            set => _audioSource.Value = value;
        }

        public float Volume
        {
            get => _volume;
            set => _volume = Mathf.Clamp01(value);
        }

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public bool WaitForCompletion
        {
            get => _waitForCompletion;
            set => _waitForCompletion = value;
        }

        public PlaySoundCommand()
        {
        }

        public PlaySoundCommand(AudioClip clip, float volume = 1f, bool loop = false)
        {
            _audioClip = new ProxyObjectSaver<AudioClip>(true) { Value = clip };
            _volume = volume;
            _loop = loop;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_audioClip.Value == null)
            {
                Debug.LogWarning("[PlaySoundCommand] AudioClip is null.");
                return;
            }

            // Respect global SFX mute setting
            if (PlayerPrefs.GetInt("SFXMuted", 0) == 1)
            {
                return;
            }

            // Use provided AudioSource or play via AudioSource.PlayClipAtPoint
            if (_audioSource.Value != null)
            {
                _runtimeAudioSource = _audioSource.Value;

                if (_loop)
                {
                    _runtimeAudioSource.clip = _audioClip.Value;
                    _runtimeAudioSource.volume = _volume;
                    _runtimeAudioSource.loop = true;
                    _runtimeAudioSource.Play();
                }
                else
                {
                    // Use PlayOneShot to avoid interrupting other sounds on the same AudioSource
                    _runtimeAudioSource.PlayOneShot(_audioClip.Value, _volume);
                }
            }
            else
            {
                // Create temporary AudioSource for one-shot playback
                var tempGO = new GameObject("TempAudio_" + _audioClip.Value.name);
                _runtimeAudioSource = tempGO.AddComponent<AudioSource>();
                _runtimeAudioSource.clip = _audioClip.Value;
                _runtimeAudioSource.volume = _volume;
                _runtimeAudioSource.loop = _loop;
                _runtimeAudioSource.Play();

                if (!_loop && !_waitForCompletion)
                {
                    UnityEngine.Object.Destroy(tempGO, _audioClip.Value.length);
                }
            }

            if (_waitForCompletion && !_loop)
            {
                float elapsed = 0f;
                float duration = _audioClip.Value.length;

                while (elapsed < duration && !_isCancelled)
                {
                    await Task.Yield();
                    elapsed += Time.deltaTime;
                }

                // Clean up temp object if we created one
                if (_audioSource.Value == null && _runtimeAudioSource != null)
                {
                    UnityEngine.Object.Destroy(_runtimeAudioSource.gameObject);
                }
            }
        }

        public override void Stop()
        {
            _isCancelled = true;

            if (_runtimeAudioSource != null)
            {
                _runtimeAudioSource.Stop();

                // Clean up temp object if we created one
                if (_audioSource.Value == null)
                {
                    UnityEngine.Object.Destroy(_runtimeAudioSource.gameObject);
                }
            }
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _audioClip.Save(saver);
            _audioSource.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _audioClip.Restore(saver);
            _audioSource.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _audioClip.ReleaseResources(saver);
            _audioSource.ReleaseResources(saver);
        }
    }
}
