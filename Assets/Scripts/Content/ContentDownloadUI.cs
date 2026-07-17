using Serhat.Forge.Startup;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Serhat.Forge.Content
{
    /// <summary>
    /// Displays boot-time content download progress.
    /// </summary>
    public sealed class ContentDownloadUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Slider progressSlider;

        [Header("Text References")]
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text sizeText;

        private IGameBootstrapper _bootstrapper;
        private bool _isSubscribed;

        [Inject]
        private void Construct(IGameBootstrapper bootstrapper)
        {
            _bootstrapper = bootstrapper;
            Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_isSubscribed || !isActiveAndEnabled || _bootstrapper == null)
            {
                return;
            }

            _bootstrapper.OnDownloadProgress += HandleProgress;
            _bootstrapper.OnBootComplete += HandleBootComplete;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed || _bootstrapper == null)
            {
                return;
            }

            _bootstrapper.OnDownloadProgress -= HandleProgress;
            _bootstrapper.OnBootComplete -= HandleBootComplete;
            _isSubscribed = false;
        }

        private void HandleProgress(DownloadProgress progress)
        {
            if (progressSlider != null)
            {
                progressSlider.value = progress.Progress;
            }

            if (progressText != null)
            {
                progressText.text = $"{progress.ProgressPercent}/100";
            }

            if (statusText != null)
            {
                statusText.text = GetStatusMessage(progress);
            }

            if (sizeText != null)
            {
                sizeText.text = progress.TotalBytes > 0
                    ? $"{progress.FormattedDownloadedSize} / {progress.FormattedTotalSize}"
                    : string.Empty;
            }
        }

        private void HandleBootComplete(bool success, string errorMessage)
        {
            if (!success && statusText != null)
            {
                statusText.text = $"Error: {errorMessage}";
                statusText.color = Color.red;
            }
        }

        private static string GetStatusMessage(DownloadProgress progress)
        {
            switch (progress.Phase)
            {
                case DownloadPhase.Idle:
                    return "Ready";
                case DownloadPhase.CheckingCatalog:
                    return "Checking for updates...";
                case DownloadPhase.UpdatingCatalog:
                    return "Updating catalog...";
                case DownloadPhase.CalculatingSize:
                    return $"Calculating: {progress.CurrentLabel}";
                case DownloadPhase.Downloading:
                    return $"Downloading: {progress.CurrentLabel}";
                case DownloadPhase.Completed:
                    return "Download complete";
                case DownloadPhase.Failed:
                    return $"Failed: {progress.ErrorMessage}";
                default:
                    return string.Empty;
            }
        }
    }
}