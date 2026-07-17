using System.Collections;
using System.Collections.Generic;
using ScenarioSystem;
using UnityEngine;

namespace Serhat.Forge.Tutorial
{
    /// <summary>
    /// Generic tutorial runner backed by the ScenarioSystem package.
    ///
    /// Wire-up:
    ///   1. Create a <see cref="TutorialConfig"/> asset and fill in the steps.
    ///   2. Add a <see cref="ScenarioPlayer"/> to the scene with one Scenario per <c>ScenarioKey</c>.
    ///   3. Add this component to a scene GameObject, drag the config + scenario player.
    ///   4. From gameplay, call <see cref="TutorialSignalBus.Raise"/> when an objective completes.
    ///
    /// Persisted completion is stored in PlayerPrefs under <see cref="OneShotPrefKey"/>.
    /// </summary>
    public sealed class TutorialRunner : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private TutorialConfig _config;
        [SerializeField] private ScenarioPlayer _scenarioPlayer;

        [Header("Behavior")]
        [Tooltip("Auto-start the runner on enable. Disable to start manually via StartRunner().")]
        [SerializeField] private bool _autoStart = true;

        [Tooltip("Reset session signals when the runner enables (useful per-level).")]
        [SerializeField] private bool _resetSessionOnEnable;

        public const string OneShotPrefKey = "Tutorial.Completed.";

        private int _currentStepIndex = -1;
        private TutorialStep _currentStep;
        private Coroutine _stepRoutine;
        private bool _running;

        private void OnEnable()
        {
            if (_resetSessionOnEnable)
                TutorialSignalBus.ClearSession();

            TutorialSignalBus.OnSignal += HandleSignal;

            if (_autoStart)
                StartRunner();
        }

        private void OnDisable()
        {
            TutorialSignalBus.OnSignal -= HandleSignal;
            StopRunner();
        }

        public void StartRunner()
        {
            if (_running)
                return;

            if (_config == null || !_config.Enabled || _config.Steps == null || _config.Steps.Length == 0)
                return;

            _running = true;
            _currentStepIndex = -1;
            AdvanceToNextStep();
        }

        public void StopRunner()
        {
            _running = false;
            if (_stepRoutine != null)
            {
                StopCoroutine(_stepRoutine);
                _stepRoutine = null;
            }
            _currentStep = null;
        }

        private void AdvanceToNextStep()
        {
            _currentStep = null;

            for (int i = _currentStepIndex + 1; i < _config.Steps.Length; i++)
            {
                var step = _config.Steps[i];
                if (step == null)
                    continue;

                if (step.OneShot && IsCompletedPersisted(step.Id))
                    continue;

                _currentStepIndex = i;
                _currentStep = step;
                _stepRoutine = StartCoroutine(RunStep(step));
                return;
            }

            // Out of steps -> done.
            StopRunner();
        }

        private IEnumerator RunStep(TutorialStep step)
        {
            // Wait for gating signal if any.
            if (!string.IsNullOrEmpty(step.GateOnSignal))
            {
                while (!TutorialSignalBus.HasFired(step.GateOnSignal))
                    yield return null;
            }

            if (step.StartDelaySeconds > 0f)
                yield return new WaitForSeconds(step.StartDelaySeconds);

            if (!string.IsNullOrEmpty(step.ScenarioKey) && _scenarioPlayer != null)
                _scenarioPlayer.PlayScenarioByKey(step.ScenarioKey);

            // Wait for the completion signal. If none specified, the step finishes immediately.
            if (string.IsNullOrEmpty(step.CompleteOnSignal))
            {
                CompleteCurrentStep();
            }
            // else: HandleSignal will trigger CompleteCurrentStep when the matching signal fires.
        }

        private void HandleSignal(string signal)
        {
            if (!_running || _currentStep == null)
                return;

            if (signal == _currentStep.CompleteOnSignal)
                CompleteCurrentStep();
        }

        private void CompleteCurrentStep()
        {
            if (_currentStep == null)
                return;

            if (_currentStep.OneShot)
                MarkCompletedPersisted(_currentStep.Id);

            if (_stepRoutine != null)
            {
                StopCoroutine(_stepRoutine);
                _stepRoutine = null;
            }

            AdvanceToNextStep();
        }

        private static bool IsCompletedPersisted(string id) =>
            !string.IsNullOrEmpty(id) && PlayerPrefs.GetInt(OneShotPrefKey + id, 0) == 1;

        private static void MarkCompletedPersisted(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            PlayerPrefs.SetInt(OneShotPrefKey + id, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Editor/debug helper: clear all persisted "completed" flags for this config.</summary>
        public void ResetPersistedProgress()
        {
            if (_config == null || _config.Steps == null)
                return;
            foreach (var s in _config.Steps)
            {
                if (s == null || string.IsNullOrEmpty(s.Id))
                    continue;
                PlayerPrefs.DeleteKey(OneShotPrefKey + s.Id);
            }
            PlayerPrefs.Save();
        }
    }
}
