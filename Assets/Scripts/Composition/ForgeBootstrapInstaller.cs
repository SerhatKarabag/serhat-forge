using System;
using Serhat.Forge.Startup;
using UnityEngine;
using Zenject;

namespace Serhat.Forge.Composition
{
    /// <summary>
    /// Registers the persistent boot orchestrator after project services are installed.
    /// </summary>
    public sealed class ForgeBootstrapInstaller : MonoInstaller
    {
        [SerializeField] private GameBootstrapper _bootstrapper;

        public override void InstallBindings()
        {
            var bootstrapper = _bootstrapper != null
                ? _bootstrapper
                : GetComponent<GameBootstrapper>();
            if (bootstrapper == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ForgeBootstrapInstaller)} requires a {nameof(GameBootstrapper)} component.");
            }

            Container.Bind<GameBootstrapper>().FromInstance(bootstrapper).AsSingle();
            Container.Bind<IGameBootstrapper>().To<GameBootstrapper>().FromResolve();
        }

#if UNITY_EDITOR
        public void SetBootstrapper(GameBootstrapper bootstrapper)
        {
            _bootstrapper = bootstrapper;
        }

        private void Reset()
        {
            _bootstrapper = GetComponent<GameBootstrapper>();
        }
#endif
    }
}