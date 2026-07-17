using Serhat.Forge.Ads;
using Serhat.Forge.Audio;
using Serhat.Forge.Content;
using Serhat.Forge.Startup;
using Serhat.Forge.UI.Components;
using UnityEngine;
using Zenject;

namespace Serhat.Forge.Composition
{
    /// <summary>
    /// Application-lifetime composition root. Lives on Resources/ProjectContext.prefab.
    /// </summary>
    public sealed class ForgeProjectInstaller : MonoInstaller
    {
        private const string ContentConfigurationResourcePath = "ContentConfiguration";
        private const string AdRuntimeSettingsResourcePath = "AdRuntimeSettings";

        [SerializeField] private bool _createDefaultAudioService = true;

        public override void InstallBindings()
        {
            InstallConfiguration();
            InstallContent();
            InstallAds();
            InstallAudio();
            InstallLoadingScreenFallback();
        }

        private void InstallConfiguration()
        {
            var configuration = Resources.Load<ContentConfiguration>(ContentConfigurationResourcePath);
            if (configuration == null)
            {
                configuration = ContentConfiguration.CreateDefault();
                Debug.LogWarning(
                    $"[{nameof(ForgeProjectInstaller)}] Resources/{ContentConfigurationResourcePath}.asset " +
                    "was not found. Runtime defaults will be used.",
                    this);
            }

            Container.Bind<ContentConfiguration>().FromInstance(configuration).AsSingle();
            Container.Bind<RetryPolicy>()
                .FromMethod(_ => RetryPolicy.FromConfiguration(configuration))
                .AsSingle();
            Container.Bind<StartupPipeline>().AsSingle();
        }

        private void InstallContent()
        {
            Container.Bind<AddressablesContentManager>()
                .FromNewComponentOnNewGameObject()
                .WithGameObjectName("[AddressablesContentManager]")
                .AsSingle()
                .NonLazy();
            Container.Bind<IContentManager>().To<AddressablesContentManager>().FromResolve();

            Container.Bind<PrefabLoaderService>().AsSingle();
            Container.Bind<IPrefabLoader>().To<PrefabLoaderService>().FromResolve();
        }

        private void InstallAds()
        {
            var settings = Resources.Load<AdRuntimeSettings>(AdRuntimeSettingsResourcePath);
            if (settings == null || !settings.EnableAds)
            {
                Container.Bind<IAdService>().FromInstance(NullAdService.Instance).AsSingle();
                return;
            }

#if GOOGLE_MOBILE_ADS
            Container.BindInterfacesAndSelfTo<GoogleAdManager>()
                .FromNewComponentOnNewGameObject()
                .WithGameObjectName("[GoogleAdManager]")
                .AsSingle()
                .NonLazy();
#else
            Debug.LogWarning(
                $"[{nameof(ForgeProjectInstaller)}] Ads are enabled, but GOOGLE_MOBILE_ADS " +
                "is not defined. NullAdService will be used.",
                this);
            Container.Bind<IAdService>().FromInstance(NullAdService.Instance).AsSingle();
#endif
        }

        private void InstallAudio()
        {
            var audioContracts = new[]
            {
                typeof(IAudioService),
                typeof(IMusicService),
                typeof(ISfxService),
                typeof(IAudioMuteService),
                typeof(IAudioVolumeService)
            };

            if (_createDefaultAudioService)
            {
                Container.Bind<SoundManager>()
                    .FromNewComponentOnNewGameObject()
                    .WithGameObjectName("[SoundManager]")
                    .AsSingle()
                    .NonLazy();
                Container.Bind(audioContracts).To<SoundManager>().FromResolve();
                return;
            }

            Container.Bind(audioContracts).To<NullAudioService>().AsSingle();
        }

        private void InstallLoadingScreenFallback()
        {
            Container.Bind<ILoadingScreen>().To<NullLoadingScreen>().AsSingle();
        }
    }
}