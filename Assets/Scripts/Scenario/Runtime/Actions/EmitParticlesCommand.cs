using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays a particle system.
    /// </summary>
    [Serializable]
    public class EmitParticlesCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<ParticleSystem> _particleSystem = new ProxyObjectSaver<ParticleSystem>();

        [SerializeField]
        private bool _waitForCompletion = false;

        [SerializeField]
        private bool _stopOnCancel = true;

        private bool _isCancelled;

        public ParticleSystem ParticleSystem
        {
            get => _particleSystem.Value;
            set => _particleSystem.Value = value;
        }

        public bool WaitForCompletion
        {
            get => _waitForCompletion;
            set => _waitForCompletion = value;
        }

        public EmitParticlesCommand()
        {
        }

        public EmitParticlesCommand(ParticleSystem particleSystem, bool waitForCompletion = false)
        {
            _particleSystem = new ProxyObjectSaver<ParticleSystem>(true) { Value = particleSystem };
            _waitForCompletion = waitForCompletion;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_particleSystem.Value == null)
            {
                Debug.LogWarning("[EmitParticlesCommand] ParticleSystem is null.");
                return;
            }

            _particleSystem.Value.Play();

            if (_waitForCompletion)
            {
                while (_particleSystem.Value.isPlaying && !_isCancelled)
                {
                    await Task.Yield();
                }
            }
        }

        public override void Stop()
        {
            _isCancelled = true;

            if (_stopOnCancel && _particleSystem.Value != null)
            {
                _particleSystem.Value.Stop();
            }
        }

        public override void Dispose()
        {
            if (_particleSystem.Value != null && _particleSystem.Value.isPlaying)
            {
                _particleSystem.Value.Stop();
            }
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _particleSystem.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _particleSystem.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _particleSystem.ReleaseResources(saver);
        }
    }
}
