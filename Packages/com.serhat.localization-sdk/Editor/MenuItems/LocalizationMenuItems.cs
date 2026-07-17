using System.IO;
using Serhat.Localization.Editor.Import;
using Serhat.Localization.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace Serhat.Localization.Editor
{
    /// <summary>
    /// Editor menu items for localization tools.
    /// </summary>
    public static class LocalizationMenuItems
    {
        private const string SettingsPath = "Assets/Resources/LocalizationSettings.asset";

        [MenuItem("Tools/Serhat/Localization/Create Settings", false, 100)]
        public static void CreateSettings()
        {
            // Ensure Resources folder exists
            var resourcesPath = "Assets/Resources";
            if (!Directory.Exists(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
            }

            // Check if already exists
            var existing = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (existing != null)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = existing;
                Debug.Log("[Localization] Settings asset already exists.");
                return;
            }

            // Create new settings
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = settings;

            Debug.Log($"[Localization] Created settings asset at {SettingsPath}");
        }

        [MenuItem("Tools/Serhat/Localization/Import CSV", false, 200)]
        public static void ImportCsv()
        {
            var csvPath = EditorUtility.OpenFilePanel("Select CSV File", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(csvPath))
                return;

            var settings = LocalizationSettings.Instance;
            var outputPath = GetOutputPath(settings);

            var result = CsvImporter.Import(csvPath, outputPath, settings.DefaultLocale.Code);

            AssetDatabase.Refresh();

            ImportResultWindow.ShowResult(result);
        }

        [MenuItem("Tools/Serhat/Localization/Open Settings", false, 300)]
        public static void OpenSettings()
        {
            var settings = LocalizationSettings.Instance;
            if (settings != null)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = settings;
            }
            else
            {
                CreateSettings();
            }
        }

        [MenuItem("Tools/Serhat/Localization/Open Project Settings", false, 301)]
        public static void OpenProjectSettings()
        {
            SettingsService.OpenProjectSettings("Project/Serhat Localization");
        }

        private static string GetOutputPath(LocalizationSettings settings)
        {
            string basePath;

            if (settings.ProviderType == ProviderType.StreamingAssets)
            {
                basePath = Path.Combine(Application.streamingAssetsPath, settings.DataPath);
            }
            else
            {
                basePath = Path.Combine(Application.dataPath, "Resources", settings.DataPath);
            }

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            return basePath;
        }
    }
}
