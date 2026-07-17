using System;
using UnityEngine;

namespace Serhat.Forge.Tutorial
{
    /// <summary>
    /// ScriptableObject containing an ordered list of tutorial steps.
    /// Drop one of these into a TutorialRunner and the runner will play steps in order.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialConfig", menuName = "Serhat Forge/Tutorial/Tutorial Config")]
    public sealed class TutorialConfig : ScriptableObject
    {
        [Tooltip("Set to false to skip the entire tutorial (e.g. for QA builds).")]
        public bool Enabled = true;

        public TutorialStep[] Steps = Array.Empty<TutorialStep>();
    }
}
