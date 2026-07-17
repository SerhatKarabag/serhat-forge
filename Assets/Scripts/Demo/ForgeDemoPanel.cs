using System;
using System.Threading.Tasks;
using Serhat.Forge.Ads;
using Serhat.Forge.Audio;
using Serhat.Forge.Content;
using Serhat.Forge.Startup;
using UnityEngine;
using Zenject;

namespace Serhat.Forge.Demo
{
    /// <summary>
    /// Lightweight, dependency-free smoke panel for the template scene.
    /// It proves project-level injection, startup, content, audio, and optional ads wiring.
    /// Replace or remove this component when starting a real game.
    /// </summary>
    public sealed class ForgeDemoPanel : MonoBehaviour
    {
        private const float PanelWidth = 460f;
        private const float PanelMargin = 24f;

        private IGameBootstrapper _bootstrapper;
        private IContentManager _contentManager;
        private IAudioMuteService _audioMuteService;
        private IAdService _adService;
        private Task<bool> _restartTask;
        private string _lastOperation = "Ready for validation.";

        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _statusStyle;

        [Inject]
        private void Construct(
            IGameBootstrapper bootstrapper,
            IContentManager contentManager,
            IAudioMuteService audioMuteService,
            IAdService adService)
        {
            _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
            _contentManager = contentManager ?? throw new ArgumentNullException(nameof(contentManager));
            _audioMuteService = audioMuteService ?? throw new ArgumentNullException(nameof(audioMuteService));
            _adService = adService ?? throw new ArgumentNullException(nameof(adService));
        }

        private void OnGUI()
        {
            EnsureStyles();

            var panelHeight = Mathf.Min(520f, Screen.height - (PanelMargin * 2f));
            var panelRect = new Rect(PanelMargin, PanelMargin, PanelWidth, panelHeight);

            GUILayout.BeginArea(panelRect, GUI.skin.box);
            try
            {
                GUILayout.Label("Serhat Forge", _titleStyle);
                GUILayout.Label(
                    "A production-minded Unity foundation. This panel is a removable composition smoke test.",
                    _bodyStyle);
                GUILayout.Space(10f);

                if (_bootstrapper == null)
                {
                    GUILayout.Label("Waiting for Zenject injection...", _statusStyle);
                    return;
                }

                DrawStatus();
                GUILayout.Space(12f);
                DrawActions();
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    $"Unity {Application.unityVersion}  |  {Application.platform}",
                    _bodyStyle);
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private void DrawStatus()
        {
            GUILayout.Label($"Boot state: {_bootstrapper.State}", _statusStyle);
            GUILayout.Label($"Content initialized: {_contentManager.IsInitialized}", _bodyStyle);
            GUILayout.Label($"Active content handles: {_contentManager.ActiveHandleCount}", _bodyStyle);
            GUILayout.Label($"Network reachable: {_contentManager.IsNetworkAvailable()}", _bodyStyle);
            GUILayout.Label(
                $"Audio muted: music={_audioMuteService.IsMusicMuted()}, sfx={_audioMuteService.IsSFXMuted()}",
                _bodyStyle);
            GUILayout.Label($"Rewarded ad ready: {_adService.IsRewardedReady()}", _bodyStyle);

            if (!string.IsNullOrWhiteSpace(_bootstrapper.ErrorMessage))
                GUILayout.Label($"Boot error: {_bootstrapper.ErrorMessage}", _statusStyle);

            GUILayout.Space(6f);
            GUILayout.Label(_lastOperation, _bodyStyle);
        }

        private void DrawActions()
        {
            var restartRunning = _restartTask != null && !_restartTask.IsCompleted;
            using (new GuiEnabledScope(!restartRunning))
            {
                if (GUILayout.Button(restartRunning ? "Restarting startup..." : "Restart startup", GUILayout.Height(34f)))
                    StartRestart();
            }

            if (GUILayout.Button("Toggle audio mute", GUILayout.Height(30f)))
            {
                var shouldMute = !_audioMuteService.IsMusicMuted() || !_audioMuteService.IsSFXMuted();
                if (shouldMute)
                    _audioMuteService.MuteAll();
                else
                    _audioMuteService.UnmuteAll();

                _lastOperation = shouldMute ? "Audio muted through IAudioMuteService." : "Audio unmuted.";
            }

            using (new GuiEnabledScope(_adService.IsRewardedReady()))
            {
                if (GUILayout.Button("Show rewarded ad (optional provider)", GUILayout.Height(30f)))
                {
                    _adService.ShowRewarded(
                        () => _lastOperation = "Reward callback received from the configured provider.");
                }
            }
        }

        private void StartRestart()
        {
            if (_restartTask != null && !_restartTask.IsCompleted)
                return;

            _lastOperation = "Restarting the startup pipeline...";
            _restartTask = _bootstrapper.RestartBootAsync();
            _ = ObserveRestartAsync(_restartTask);
        }

        private async Task ObserveRestartAsync(Task<bool> restartTask)
        {
            try
            {
                var succeeded = await restartTask;
                _lastOperation = succeeded
                    ? "Startup pipeline completed successfully."
                    : "Startup pipeline completed with a handled failure.";
            }
            catch (Exception exception)
            {
                _lastOperation = $"Startup pipeline faulted: {exception.Message}";
                Debug.LogException(exception, this);
            }
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };
            _statusStyle = new GUIStyle(_bodyStyle)
            {
                fontStyle = FontStyle.Bold
            };
        }

        private readonly struct GuiEnabledScope : IDisposable
        {
            private readonly bool _previous;

            public GuiEnabledScope(bool enabled)
            {
                _previous = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = _previous;
            }
        }
    }
}