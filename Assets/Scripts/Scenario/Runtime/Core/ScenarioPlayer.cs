using System.Collections.Generic;
using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>
    /// Helper component for managing multiple scenarios with key-based access.
    /// Attach this to a GameObject and assign scenarios in the inspector.
    /// </summary>
    public class ScenarioPlayer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("List of scenarios mapped to keys")]
        private List<KeyScenarioMap> _scenarioMap = new List<KeyScenarioMap>();

        private Dictionary<string, ScenarioBase> _scenarioDict;

        private void Awake()
        {
            BuildDictionary();
        }

        private void BuildDictionary()
        {
            _scenarioDict = new Dictionary<string, ScenarioBase>();
            foreach (var entry in _scenarioMap)
            {
                if (!string.IsNullOrEmpty(entry.ScenarioKey) && entry.Scenario != null)
                {
                    _scenarioDict[entry.ScenarioKey] = entry.Scenario;
                }
            }
        }

        private void EnsureDictionary()
        {
            if (_scenarioDict == null)
            {
                BuildDictionary();
            }
        }

        /// <summary>
        /// Plays a scenario by its key.
        /// </summary>
        public void PlayScenarioByKey(string key)
        {
            EnsureDictionary();

            if (_scenarioDict.TryGetValue(key, out var scenario))
            {
                scenario.Execute();
            }
            else
            {
                Debug.LogWarning($"[ScenarioPlayer] Scenario with key '{key}' not found.");
            }
        }

        /// <summary>
        /// Plays a scenario by key with a parameter.
        /// </summary>
        public void PlayScenarioByKey<T>(string key, T parameter)
        {
            EnsureDictionary();

            if (_scenarioDict.TryGetValue(key, out var scenario))
            {
                scenario.SetParameter(parameter);
                scenario.Execute();
            }
            else
            {
                Debug.LogWarning($"[ScenarioPlayer] Scenario with key '{key}' not found.");
            }
        }

        /// <summary>
        /// Stops a scenario by its key.
        /// </summary>
        public void StopScenarioByKey(string key)
        {
            EnsureDictionary();

            if (_scenarioDict.TryGetValue(key, out var scenario))
            {
                scenario.CancelExecution();
            }
        }

        /// <summary>
        /// Stops all currently executing scenarios.
        /// </summary>
        public void StopAllScenarios()
        {
            EnsureDictionary();

            foreach (var scenario in _scenarioDict.Values)
            {
                if (scenario.IsInExecution)
                {
                    scenario.CancelExecution();
                }
            }
        }

        /// <summary>
        /// Gets a scenario by its key.
        /// </summary>
        public ScenarioBase GetScenario(string key)
        {
            EnsureDictionary();
            _scenarioDict.TryGetValue(key, out var scenario);
            return scenario;
        }

        /// <summary>
        /// Checks if a scenario is currently executing.
        /// </summary>
        public bool IsScenarioPlaying(string key)
        {
            EnsureDictionary();

            if (_scenarioDict.TryGetValue(key, out var scenario))
            {
                return scenario.IsInExecution;
            }
            return false;
        }

        /// <summary>
        /// Adds a scenario mapping at runtime.
        /// </summary>
        public void AddScenario(string key, ScenarioBase scenario)
        {
            EnsureDictionary();
            _scenarioDict[key] = scenario;
        }

        /// <summary>
        /// Removes a scenario mapping at runtime.
        /// </summary>
        public void RemoveScenario(string key)
        {
            EnsureDictionary();
            _scenarioDict.Remove(key);
        }
    }
}
