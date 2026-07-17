using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that stops an AudioSource.
    /// </summary>
    [Serializable]
    public class StopSoundCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<AudioSource> _audioSource = new ProxyObjectSaver<AudioSource>();

        public AudioSource AudioSource
        {
            get => _audioSource.Value;
            set => _audioSource.Value = value;
        }

        public StopSoundCommand()
        {
        }

        public StopSoundCommand(AudioSource audioSource)
        {
            _audioSource = new ProxyObjectSaver<AudioSource>(true) { Value = audioSource };
        }

        public override Task Execute()
        {
            if (_audioSource.Value != null)
            {
                _audioSource.Value.Stop();
            }
            else
            {
                Debug.LogWarning("[StopSoundCommand] AudioSource is null.");
            }

            return Task.CompletedTask;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _audioSource.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _audioSource.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _audioSource.ReleaseResources(saver);
        }
    }
}
