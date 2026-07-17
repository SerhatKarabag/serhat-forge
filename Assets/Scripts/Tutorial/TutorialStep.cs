using System;
using UnityEngine;

namespace Serhat.Forge.Tutorial
{
    /// <summary>
    /// One step of a tutorial sequence. A step is gated by an optional condition,
    /// triggers a Scenario by key, and completes when an external signal is raised.
    ///
    /// Define steps in <see cref="TutorialConfig"/> and run them with <see cref="TutorialRunner"/>.
    /// </summary>
    [Serializable]
    public sealed class TutorialStep
    {
        [Tooltip("Stable id used to mark the step completed. Persisted in PlayerPrefs.")]
        public string Id = "step_1";

        [Tooltip("Scenario key to play when the step starts. Must exist in ScenarioPlayer.")]
        public string ScenarioKey;

        [Tooltip("External signal that completes the step. Raise via TutorialRunner.RaiseSignal(...).")]
        public string CompleteOnSignal;

        [Tooltip("If true, only fires once per player (persists across sessions). If false, fires every level/session.")]
        public bool OneShot = true;

        [Tooltip("Optional pre-condition signal that must have fired in this session before the step is eligible.")]
        public string GateOnSignal;

        [Tooltip("Optional delay (seconds) before starting the step's scenario.")]
        [Min(0f)] public float StartDelaySeconds;
    }
}
