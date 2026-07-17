using Serhat.Localization.Editor.Import;
using UnityEditor;
using UnityEngine;

namespace Serhat.Localization.Editor.Windows
{
    /// <summary>
    /// Window to display import results.
    /// </summary>
    public class ImportResultWindow : EditorWindow
    {
        private ImportResult _result;
        private Vector2 _scrollPosition;

        public static void ShowResult(ImportResult result)
        {
            var window = GetWindow<ImportResultWindow>("Import Result");
            window._result = result;
            window.minSize = new Vector2(400, 300);
            window.Show();

            // Also log to console
            LogResultToConsole(result);
        }

        private static void LogResultToConsole(ImportResult result)
        {
            if (result.Success)
            {
                Debug.Log($"[Localization] Import successful!\n" +
                         $"Source: {result.SourceFile}\n" +
                         $"Keys: {result.KeyCount}\n" +
                         $"Locales: {string.Join(", ", result.Locales)}\n" +
                         $"Generated files: {result.GeneratedFiles.Count}");
            }
            else
            {
                Debug.LogError($"[Localization] Import failed!\n" +
                              $"Errors: {string.Join("\n", result.Errors)}");
            }

            foreach (var warning in result.Warnings)
            {
                Debug.LogWarning($"[Localization] {warning}");
            }
        }

        private void OnGUI()
        {
            if (_result == null)
            {
                EditorGUILayout.HelpBox("No import result to display.", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Status
            var statusStyle = new GUIStyle(EditorStyles.boldLabel);
            statusStyle.normal.textColor = _result.Success ? Color.green : Color.red;
            EditorGUILayout.LabelField(_result.Success ? "Import Successful" : "Import Failed", statusStyle);

            EditorGUILayout.Space();

            // Summary
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Source: {_result.SourceFile}");
            EditorGUILayout.LabelField($"Keys: {_result.KeyCount}");
            EditorGUILayout.LabelField($"Locales: {_result.LocaleCount}");

            if (_result.Locales.Count > 0)
            {
                EditorGUILayout.LabelField($"Languages: {string.Join(", ", _result.Locales)}");
            }

            // Generated files
            if (_result.GeneratedFiles.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Generated Files", EditorStyles.boldLabel);
                foreach (var file in _result.GeneratedFiles)
                {
                    EditorGUILayout.LabelField($"  - {file}");
                }
            }

            // Errors
            if (_result.Errors.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Errors", EditorStyles.boldLabel);
                var errorStyle = new GUIStyle(EditorStyles.label);
                errorStyle.normal.textColor = Color.red;

                foreach (var error in _result.Errors)
                {
                    EditorGUILayout.LabelField($"  X {error}", errorStyle);
                }
            }

            // Warnings
            if (_result.Warnings.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Warnings ({_result.Warnings.Count})", EditorStyles.boldLabel);
                var warningStyle = new GUIStyle(EditorStyles.label);
                warningStyle.normal.textColor = new Color(1f, 0.7f, 0f);

                foreach (var warning in _result.Warnings)
                {
                    EditorGUILayout.LabelField($"  ! {warning}", warningStyle);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("Close"))
            {
                Close();
            }
        }
    }
}
