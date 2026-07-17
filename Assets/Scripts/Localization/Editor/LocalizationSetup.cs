#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Serhat.Localization;
using System.IO;

namespace Serhat.Forge.Localization.Editor
{
    /// <summary>
    /// Editor utilities for setting up localization.
    /// </summary>
    public static class LocalizationSetup
    {
        private const string SettingsPath = "Assets/Resources/LocalizationSettings.asset";

        [MenuItem("Tools/Serhat Forge/Localization/Setup Localization", false, 100)]
        public static void SetupLocalization()
        {
            // Ensure Resources folder exists
            if (!Directory.Exists("Assets/Resources"))
            {
                Directory.CreateDirectory("Assets/Resources");
            }

            // Check if settings already exist
            var existing = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (existing != null)
            {
                Debug.Log("[Localization] Settings already exist. Selecting...");
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            // Create new settings
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();

            // Configure via SerializedObject
            AssetDatabase.CreateAsset(settings, SettingsPath);

            var serializedObject = new SerializedObject(settings);
            serializedObject.FindProperty("_defaultLocale").stringValue = "en";

            var supportedLocales = serializedObject.FindProperty("_supportedLocales");
            supportedLocales.ClearArray();
            supportedLocales.InsertArrayElementAtIndex(0);
            supportedLocales.GetArrayElementAtIndex(0).stringValue = "en";
            supportedLocales.InsertArrayElementAtIndex(1);
            supportedLocales.GetArrayElementAtIndex(1).stringValue = "tr";

            serializedObject.FindProperty("_providerType").enumValueIndex = 0; // StreamingAssets
            serializedObject.FindProperty("_dataPath").stringValue = "Localization/Locales";
            serializedObject.FindProperty("_autoInitialize").boolValue = true;
            serializedObject.FindProperty("_useSystemLanguage").boolValue = true;

            serializedObject.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);

            Debug.Log("[Localization] Settings created successfully at " + SettingsPath);
        }

        [MenuItem("Tools/Serhat Forge/Localization/Create LocalizationManager", false, 101)]
        public static void CreateManager()
        {
            // Check if already exists
            var existing = Object.FindFirstObjectByType<LocalizationManager>();
            if (existing != null)
            {
                Debug.Log("[Localization] LocalizationManager already exists in scene.");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            // Create new
            var go = new GameObject("LocalizationManager");
            go.AddComponent<LocalizationManager>();

            Selection.activeGameObject = go;

            Debug.Log("[Localization] LocalizationManager created. Don't forget to make it a prefab or add to your initialization scene!");
        }

        [MenuItem("Tools/Serhat Forge/Localization/Open Locales Folder", false, 200)]
        public static void OpenLocalesFolder()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Localization/Locales");
            if (Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                Debug.LogWarning($"[Localization] Folder does not exist: {path}");
            }
        }
    }
}
#endif
