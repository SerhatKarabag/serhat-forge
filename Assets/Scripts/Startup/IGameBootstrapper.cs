using System;
using System.Threading.Tasks;
using Serhat.Forge.Content;

namespace Serhat.Forge.Startup
{
    /// <summary>
    /// Observable application boot lifecycle exposed to UI and scene systems.
    /// </summary>
    public interface IGameBootstrapper
    {
        GameBootState State { get; }
        bool IsDone { get; }
        bool IsReady { get; }
        string ErrorMessage { get; }
        ContentConfiguration Configuration { get; }

        event Action<GameBootState> OnStateChanged;
        event Action<bool, string> OnBootComplete;
        event DownloadProgressHandler OnDownloadProgress;
        event Action<int, int, StartupStep> OnStartupStepStarted;

        Task<bool> BootAsync();
        Task<bool> BootAndLoadSceneAsync();
        Task<bool> RestartBootAsync();
        Task LoadSceneAsync(string sceneName);
        void CancelBoot();
    }
}