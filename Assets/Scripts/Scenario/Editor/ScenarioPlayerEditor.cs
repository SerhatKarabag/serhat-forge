using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using ScenarioSystem;

namespace ScenarioSystem.Editor
{
    [CustomEditor(typeof(ScenarioPlayer))]
    public class ScenarioPlayerEditor : UnityEditor.Editor
    {
        private SerializedProperty _scenarioMapProp;
        private ReorderableList _scenarioList;

        private void OnEnable()
        {
            _scenarioMapProp = serializedObject.FindProperty("_scenarioMap");
            SetupScenarioList();
        }

        private void SetupScenarioList()
        {
            _scenarioList = new ReorderableList(serializedObject, _scenarioMapProp, true, true, true, true);

            _scenarioList.drawHeaderCallback = (Rect rect) =>
            {
                var keyRect = new Rect(rect.x, rect.y, rect.width * 0.4f, rect.height);
                var scenarioRect = new Rect(rect.x + rect.width * 0.42f, rect.y, rect.width * 0.58f, rect.height);

                EditorGUI.LabelField(keyRect, "Key");
                EditorGUI.LabelField(scenarioRect, "Scenario");
            };

            _scenarioList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _scenarioMapProp.GetArrayElementAtIndex(index);
                var keyProp = element.FindPropertyRelative("ScenarioKey");
                var scenarioProp = element.FindPropertyRelative("Scenario");

                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                var keyRect = new Rect(rect.x, rect.y, rect.width * 0.4f - 5, rect.height);
                var scenarioRect = new Rect(rect.x + rect.width * 0.4f, rect.y, rect.width * 0.6f, rect.height);

                EditorGUI.PropertyField(keyRect, keyProp, GUIContent.none);
                EditorGUI.PropertyField(scenarioRect, scenarioProp, GUIContent.none);
            };

            _scenarioList.elementHeight = EditorGUIUtility.singleLineHeight + 4;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Scenario Mappings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _scenarioList.DoLayoutList();

            EditorGUILayout.Space(10);

            // Quick play buttons in play mode
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Quick Play", EditorStyles.boldLabel);

                var player = (ScenarioPlayer)target;

                for (int i = 0; i < _scenarioMapProp.arraySize; i++)
                {
                    var element = _scenarioMapProp.GetArrayElementAtIndex(i);
                    var key = element.FindPropertyRelative("ScenarioKey").stringValue;

                    if (string.IsNullOrEmpty(key)) continue;

                    EditorGUILayout.BeginHorizontal();

                    var isPlaying = player.IsScenarioPlaying(key);
                    var statusIcon = isPlaying ? "▶" : "○";

                    GUI.enabled = !isPlaying;
                    if (GUILayout.Button($"{statusIcon} {key}", GUILayout.Height(25)))
                    {
                        player.PlayScenarioByKey(key);
                    }

                    GUI.enabled = isPlaying;
                    if (GUILayout.Button("Stop", GUILayout.Width(50), GUILayout.Height(25)))
                    {
                        player.StopScenarioByKey(key);
                    }

                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(5);

                if (GUILayout.Button("Stop All", GUILayout.Height(25)))
                {
                    player.StopAllScenarios();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
