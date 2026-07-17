using UnityEditor;
using UnityEngine;

namespace Serhat.Localization.Editor
{
    /// <summary>
    /// Custom editor for LocalizationSettings.
    /// </summary>
    [CustomEditor(typeof(LocalizationSettings))]
    public class LocalizationSettingsEditor : UnityEditor.Editor
    {
        private SerializedProperty _defaultLocale;
        private SerializedProperty _supportedLocales;
        private SerializedProperty _providerType;
        private SerializedProperty _dataPath;
        private SerializedProperty _autoInitialize;
        private SerializedProperty _useSystemLanguage;

        private void OnEnable()
        {
            _defaultLocale = serializedObject.FindProperty("_defaultLocale");
            _supportedLocales = serializedObject.FindProperty("_supportedLocales");
            _providerType = serializedObject.FindProperty("_providerType");
            _dataPath = serializedObject.FindProperty("_dataPath");
            _autoInitialize = serializedObject.FindProperty("_autoInitialize");
            _useSystemLanguage = serializedObject.FindProperty("_useSystemLanguage");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Locale Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_defaultLocale, new GUIContent("Default Locale", "The default locale to use when no preference is set."));
            EditorGUILayout.PropertyField(_supportedLocales, new GUIContent("Supported Locales", "List of supported locale codes."), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Data Loading", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_providerType, new GUIContent("Provider Type", "Where to load localization data from."));
            EditorGUILayout.PropertyField(_dataPath, new GUIContent("Data Path", "Path relative to StreamingAssets or Resources folder."));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Initialization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_autoInitialize, new GUIContent("Auto Initialize", "Whether to auto-initialize on application start."));
            EditorGUILayout.PropertyField(_useSystemLanguage, new GUIContent("Use System Language", "Whether to use system language as initial locale if supported."));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Import CSV"))
            {
                LocalizationMenuItems.ImportCsv();
            }

            // Show current data path info
            var settings = (LocalizationSettings)target;
            string fullPath = settings.ProviderType == ProviderType.StreamingAssets
                ? $"StreamingAssets/{settings.DataPath}"
                : $"Resources/{settings.DataPath}";

            EditorGUILayout.HelpBox($"Localization files should be placed in:\n{fullPath}/[locale].json", MessageType.Info);
        }
    }
}
