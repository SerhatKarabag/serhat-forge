using System;
using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>
    /// Serializable entry for storing Unity Object references with a string key.
    /// Used by Scenario to maintain a dictionary-like structure in the inspector.
    /// </summary>
    [Serializable]
    public class SavedObjectEntry
    {
        public string key;
        public UnityEngine.Object savedObject;

        public SavedObjectEntry()
        {
        }

        public SavedObjectEntry(string key, UnityEngine.Object savedObject)
        {
            this.key = key;
            this.savedObject = savedObject;
        }
    }
}
