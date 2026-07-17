using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using ScenarioSystem;
using ScenarioSystem.Actions;

namespace ScenarioSystem.Editor
{
    [CustomEditor(typeof(Scenario))]
    public class ScenarioEditor : UnityEditor.Editor
    {
        private SerializedProperty _commandsProp;
        private SerializedProperty _savedObjectsProp;
        private SerializedProperty _playOnStartProp;
        private SerializedProperty _stopOnDisableProp;
        private SerializedProperty _uncancelTokenOnPlayProp;
        private SerializedProperty _loopScenarioProp;

        private ReorderableList _commandList;
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        // Command type cache
        private static Type[] _commandTypes;
        private static string[] _commandNames;

        private void OnEnable()
        {
            _commandsProp = serializedObject.FindProperty("_commands");
            _savedObjectsProp = serializedObject.FindProperty("savedObjects");
            _playOnStartProp = serializedObject.FindProperty("playOnStart");
            _stopOnDisableProp = serializedObject.FindProperty("stopOnDisable");
            _uncancelTokenOnPlayProp = serializedObject.FindProperty("uncancelTokenOnPlay");
            _loopScenarioProp = serializedObject.FindProperty("loopScenario");

            CacheCommandTypes();
            SetupCommandList();
        }

        private void CacheCommandTypes()
        {
            if (_commandTypes != null) return;

            var baseType = typeof(BaseCommand);
            _commandTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return new Type[0]; }
                })
                .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToArray();

            _commandNames = _commandTypes.Select(t => FormatCommandName(t.Name)).ToArray();
        }

        private string FormatCommandName(string typeName)
        {
            var name = typeName.Replace("Command", "");
            var result = "";
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    result += " ";
                }
                result += name[i];
            }
            return result;
        }

        private void SetupCommandList()
        {
            _commandList = new ReorderableList(serializedObject, _commandsProp, true, true, true, true);

            _commandList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, $"Commands ({_commandsProp.arraySize})");
            };

            _commandList.drawElementCallback = DrawCommandElement;
            _commandList.elementHeightCallback = GetCommandElementHeight;
            _commandList.onAddDropdownCallback = ShowAddCommandMenu;
            _commandList.onRemoveCallback = RemoveCommand;
        }

        private void DrawCommandElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index >= _commandsProp.arraySize) return;

            var commandProp = _commandsProp.GetArrayElementAtIndex(index);
            var command = commandProp.managedReferenceValue as BaseCommand;

            if (command == null)
            {
                EditorGUI.LabelField(rect, "(null command)");
                return;
            }

            var commandName = FormatCommandName(command.GetType().Name);
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            // Foldout key
            var foldoutKey = $"{target.GetInstanceID()}_{index}";
            if (!_foldoutStates.ContainsKey(foldoutKey))
                _foldoutStates[foldoutKey] = false;

            // Muted toggle
            var mutedRect = new Rect(rect.x, rect.y, 20, rect.height);
            EditorGUI.BeginChangeCheck();
            var newMuted = !EditorGUI.Toggle(mutedRect, !command.Muted);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Toggle Command Muted");
                command.Muted = newMuted;
                EditorUtility.SetDirty(target);
            }

            // Command name (foldout)
            var foldoutRect = new Rect(rect.x + 25, rect.y, rect.width - 100, rect.height);
            var style = new GUIStyle(EditorStyles.foldout);
            if (command.Muted)
            {
                style.normal.textColor = Color.gray;
            }

            _foldoutStates[foldoutKey] = EditorGUI.Foldout(foldoutRect, _foldoutStates[foldoutKey], commandName, true, style);

            // Wait for complete toggle
            var waitRect = new Rect(rect.x + rect.width - 70, rect.y, 70, rect.height);
            EditorGUI.BeginChangeCheck();
            var newWait = EditorGUI.ToggleLeft(waitRect, "Wait", command.WaitForComplete);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Toggle Wait For Complete");
                command.WaitForComplete = newWait;
                EditorUtility.SetDirty(target);
            }

            // Draw command properties if expanded
            if (_foldoutStates[foldoutKey])
            {
                rect.y += EditorGUIUtility.singleLineHeight + 2;
                DrawCommandProperties(rect, commandProp, command);
            }
        }

        private void DrawCommandProperties(Rect rect, SerializedProperty commandProp, BaseCommand command)
        {
            rect.x += 20;
            rect.width -= 20;

            var scenario = (Scenario)target;

            // Iterate through serialized properties of the command
            var iterator = commandProp.Copy();
            var endProp = iterator.GetEndProperty();

            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProp))
            {
                enterChildren = false;

                // Skip base class properties (including [field: SerializeField] backing fields)
                if (iterator.name == "ExecutingInProgress" ||
                    iterator.name == "Muted" ||
                    iterator.name == "WaitForComplete" ||
                    iterator.name == "InjectCommand" ||
                    iterator.name.Contains("ExecutingInProgress") ||
                    iterator.name.Contains("Muted") ||
                    iterator.name.Contains("WaitForComplete") ||
                    iterator.name.Contains("InjectCommand"))
                    continue;

                rect.height = EditorGUI.GetPropertyHeight(iterator, true);

                // Handle ProxyObjectSaver specially
                if (iterator.type.Contains("ProxyObjectSaver"))
                {
                    DrawProxyObjectSaver(rect, iterator, scenario, command);
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.PropertyField(rect, iterator, true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                    }
                }

                rect.y += rect.height + 2;
            }
        }

        private void DrawProxyObjectSaver(Rect rect, SerializedProperty proxyProp, Scenario scenario, BaseCommand command)
        {
            var guidProp = proxyProp.FindPropertyRelative("_guid");
            if (guidProp == null)
            {
                EditorGUI.LabelField(rect, ObjectNames.NicifyVariableName(proxyProp.name), "Missing guid");
                return;
            }
            var guid = guidProp.stringValue;

            // Get the generic type from the property
            System.Reflection.FieldInfo fieldInfo = null;
            if (command != null)
            {
                fieldInfo = command.GetType().GetField(proxyProp.name,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);
            }

            if (fieldInfo == null)
            {
                fieldInfo = GetFieldInfoFromProperty(proxyProp);
            }
            if (fieldInfo == null) return;

            var fieldType = fieldInfo.FieldType;
            if (!fieldType.IsGenericType) return;

            var innerType = fieldType.GetGenericArguments()[0];

            // Get current value from SavedObjects
            UnityEngine.Object currentValue = null;
            if (!string.IsNullOrEmpty(guid))
            {
                currentValue = GetSavedObjectByGuid(guid, scenario);
            }

            // Draw object field
            var label = ObjectNames.NicifyVariableName(proxyProp.name.TrimStart('_'));
            EditorGUI.BeginChangeCheck();
            var newValue = EditorGUI.ObjectField(rect, label, currentValue, innerType, true);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Change Object Reference");

                // Generate GUID if needed
                if (string.IsNullOrEmpty(guid))
                {
                    guid = System.Guid.NewGuid().ToString();
                    guidProp.stringValue = guid;
                }

                // Save to SavedObjects (via SerializedProperty to avoid overwrite)
                if (newValue != null)
                {
                    SetSavedObject(guid, newValue, scenario);
                }
                else
                {
                    RemoveSavedObject(guid, scenario);
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private UnityEngine.Object GetSavedObjectByGuid(string guid, Scenario scenario)
        {
            if (_savedObjectsProp == null)
            {
                return scenario.Restore(guid);
            }

            for (int i = 0; i < _savedObjectsProp.arraySize; i++)
            {
                var entry = _savedObjectsProp.GetArrayElementAtIndex(i);
                var keyProp = entry.FindPropertyRelative("key");
                if (keyProp != null && keyProp.stringValue == guid)
                {
                    var objProp = entry.FindPropertyRelative("savedObject");
                    if (objProp != null)
                    {
                        return objProp.objectReferenceValue;
                    }
                }
            }

            return null;
        }

        private void SetSavedObject(string guid, UnityEngine.Object obj, Scenario scenario)
        {
            if (_savedObjectsProp == null)
            {
                scenario.Save(guid, obj);
                return;
            }

            var index = FindSavedObjectIndex(guid);
            if (index < 0)
            {
                _savedObjectsProp.InsertArrayElementAtIndex(_savedObjectsProp.arraySize);
                index = _savedObjectsProp.arraySize - 1;
            }

            var entry = _savedObjectsProp.GetArrayElementAtIndex(index);
            var keyProp = entry.FindPropertyRelative("key");
            var objProp = entry.FindPropertyRelative("savedObject");
            if (keyProp != null) keyProp.stringValue = guid;
            if (objProp != null) objProp.objectReferenceValue = obj;
        }

        private void RemoveSavedObject(string guid, Scenario scenario)
        {
            if (_savedObjectsProp == null)
            {
                scenario.RemoveStoredObjectByKey(guid);
                return;
            }

            var index = FindSavedObjectIndex(guid);
            if (index < 0) return;

            var entry = _savedObjectsProp.GetArrayElementAtIndex(index);
            var objProp = entry.FindPropertyRelative("savedObject");
            if (objProp != null && objProp.objectReferenceValue != null)
            {
                objProp.objectReferenceValue = null;
            }

            _savedObjectsProp.DeleteArrayElementAtIndex(index);
        }

        private int FindSavedObjectIndex(string guid)
        {
            if (_savedObjectsProp == null) return -1;

            for (int i = 0; i < _savedObjectsProp.arraySize; i++)
            {
                var entry = _savedObjectsProp.GetArrayElementAtIndex(i);
                var keyProp = entry.FindPropertyRelative("key");
                if (keyProp != null && keyProp.stringValue == guid)
                {
                    return i;
                }
            }

            return -1;
        }

        private System.Reflection.FieldInfo GetFieldInfoFromProperty(SerializedProperty prop)
        {
            var parentType = prop.serializedObject.targetObject.GetType();
            var path = prop.propertyPath.Split('.');

            System.Reflection.FieldInfo field = null;
            var currentType = parentType;

            foreach (var pathPart in path)
            {
                if (pathPart == "Array") continue;
                if (pathPart.StartsWith("data[")) continue;

                field = currentType.GetField(pathPart,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    currentType = field.FieldType;
                    if (currentType.IsArray)
                        currentType = currentType.GetElementType();
                    else if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(List<>))
                        currentType = currentType.GetGenericArguments()[0];
                }
            }

            return field;
        }

        private float GetCommandElementHeight(int index)
        {
            if (index >= _commandsProp.arraySize) return EditorGUIUtility.singleLineHeight;

            var commandProp = _commandsProp.GetArrayElementAtIndex(index);
            var command = commandProp.managedReferenceValue as BaseCommand;
            if (command == null) return EditorGUIUtility.singleLineHeight;

            var foldoutKey = $"{target.GetInstanceID()}_{index}";
            if (!_foldoutStates.ContainsKey(foldoutKey) || !_foldoutStates[foldoutKey])
            {
                return EditorGUIUtility.singleLineHeight + 4;
            }

            // Count visible properties
            float height = EditorGUIUtility.singleLineHeight + 4;

            var iterator = commandProp.Copy();
            var endProp = iterator.GetEndProperty();

            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProp))
            {
                enterChildren = false;

                if (iterator.name == "ExecutingInProgress" ||
                    iterator.name == "Muted" ||
                    iterator.name == "WaitForComplete" ||
                    iterator.name == "InjectCommand" ||
                    iterator.name.Contains("ExecutingInProgress") ||
                    iterator.name.Contains("Muted") ||
                    iterator.name.Contains("WaitForComplete") ||
                    iterator.name.Contains("InjectCommand"))
                    continue;

                height += EditorGUI.GetPropertyHeight(iterator, true) + 2;
            }

            return height;
        }

        private void ShowAddCommandMenu(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();

            for (int i = 0; i < _commandTypes.Length; i++)
            {
                var type = _commandTypes[i];
                var name = _commandNames[i];

                var category = GetCommandCategory(type);
                var menuPath = string.IsNullOrEmpty(category) ? name : $"{category}/{name}";

                menu.AddItem(new GUIContent(menuPath), false, () => AddCommand(type));
            }

            menu.ShowAsContext();
        }

        private string GetCommandCategory(Type commandType)
        {
            var name = commandType.Name;

            if (name.Contains("Tween")) return "Tween";
            if (name.Contains("Sound") || name.Contains("Audio")) return "Audio";
            if (name.Contains("Particle") || name.Contains("Emit")) return "VFX";
            if (name.Contains("Animator") || name.Contains("Animation")) return "Animation";
            if (name.Contains("GameObject") || name.Contains("Component") || name.Contains("Position")) return "Object";
            if (name.Contains("Haptic")) return "Feedback";
            if (name.Contains("Scenario")) return "Flow";

            return "";
        }

        private void AddCommand(Type commandType)
        {
            Undo.RecordObject(target, "Add Command");

            var command = (BaseCommand)Activator.CreateInstance(commandType);
            command.WaitForComplete = true;

            var index = _commandsProp.arraySize;
            _commandsProp.InsertArrayElementAtIndex(index);
            _commandsProp.GetArrayElementAtIndex(index).managedReferenceValue = command;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void RemoveCommand(ReorderableList list)
        {
            if (list.index >= 0 && list.index < _commandsProp.arraySize)
            {
                Undo.RecordObject(target, "Remove Command");

                // Clean up SavedObjects
                var commandProp = _commandsProp.GetArrayElementAtIndex(list.index);
                var command = commandProp.managedReferenceValue as BaseCommand;
                if (command != null)
                {
                    command.ReleaseResources((Scenario)target);
                }

                _commandsProp.DeleteArrayElementAtIndex(list.index);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Settings
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_playOnStartProp);
            EditorGUILayout.PropertyField(_stopOnDisableProp);
            EditorGUILayout.PropertyField(_uncancelTokenOnPlayProp);
            EditorGUILayout.PropertyField(_loopScenarioProp);

            EditorGUILayout.Space(10);

            // Commands
            _commandList.DoLayoutList();

            EditorGUILayout.Space(5);

            // Play/Stop buttons (only in play mode)
            if (Application.isPlaying)
            {
                var scenario = (Scenario)target;

                EditorGUILayout.BeginHorizontal();

                GUI.enabled = !scenario.IsInExecution;
                if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                {
                    scenario.Execute();
                }

                GUI.enabled = scenario.IsInExecution;
                if (GUILayout.Button("■ Stop", GUILayout.Height(30)))
                {
                    scenario.CancelExecution();
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (scenario.IsInExecution)
                {
                    EditorGUILayout.HelpBox("Scenario is playing...", MessageType.Info);
                    Repaint();
                }
            }

            EditorGUILayout.Space(5);

            // Saved Objects (collapsed by default)
            _savedObjectsProp.isExpanded = EditorGUILayout.Foldout(_savedObjectsProp.isExpanded, "Saved Objects (Debug)");
            if (_savedObjectsProp.isExpanded)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _savedObjectsProp.arraySize; i++)
                {
                    var entry = _savedObjectsProp.GetArrayElementAtIndex(i);
                    var key = entry.FindPropertyRelative("key");
                    var obj = entry.FindPropertyRelative("savedObject");

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(key.stringValue, GUILayout.Width(200));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(obj.objectReferenceValue, typeof(UnityEngine.Object), true);
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
