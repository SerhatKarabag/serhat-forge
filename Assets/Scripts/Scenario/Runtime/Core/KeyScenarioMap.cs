using System;
using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>
    /// Maps a string key to a scenario.
    /// Used to trigger scenarios by name from code.
    /// </summary>
    [Serializable]
    public class KeyScenarioMap
    {
        [Tooltip("Unique key to identify this scenario")]
        public string ScenarioKey;

        [Tooltip("Reference to the scenario")]
        public ScenarioBase Scenario;

        public KeyScenarioMap()
        {
        }

        public KeyScenarioMap(string key, ScenarioBase scenario)
        {
            ScenarioKey = key;
            Scenario = scenario;
        }
    }
}
