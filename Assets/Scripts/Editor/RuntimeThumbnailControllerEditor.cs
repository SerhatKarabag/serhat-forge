using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    [CustomEditor(typeof(Serhat.Forge.RuntimeThumbnailController))]
    public class RuntimeThumbnailControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Drag & Drop Folder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag a folder here to add all prefabs inside it (including subfolders) to the Prefabs To Capture list.",
                MessageType.Info);

            var dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop Folder Here", EditorStyles.helpBox);

            HandleFolderDrop(dropRect);
        }

        private void HandleFolderDrop(Rect dropRect)
        {
            var evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition))
                return;

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            // Check if any dragged object is a folder
            bool hasFolder = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path))
                {
                    hasFolder = true;
                    break;
                }
            }

            if (!hasFolder)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddPrefabsFromDraggedFolders();
                evt.Use();
            }
        }

        private void AddPrefabsFromDraggedFolders()
        {
            var listProp = serializedObject.FindProperty("_prefabsToCapture");
            serializedObject.Update();

            // Collect existing entries to avoid duplicates
            var existing = new HashSet<string>();
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var obj = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (obj != null)
                    existing.Add(AssetDatabase.GetAssetPath(obj));
            }

            int added = 0;

            foreach (var obj in DragAndDrop.objectReferences)
            {
                var folderPath = AssetDatabase.GetAssetPath(obj);
                if (!AssetDatabase.IsValidFolder(folderPath))
                    continue;

                // Only search inside "Prefabs" subfolders (e.g. Objects/Bombs/Prefabs/)
                var prefabFolders = new List<string>();
                foreach (var subDir in System.IO.Directory.GetDirectories(folderPath, "Prefabs", System.IO.SearchOption.AllDirectories))
                {
                    prefabFolders.Add(subDir.Replace('\\', '/'));
                }

                // Fallback: if no Prefabs subfolder found, search the folder itself
                if (prefabFolders.Count == 0)
                    prefabFolders.Add(folderPath);

                var guids = AssetDatabase.FindAssets("t:GameObject", prefabFolders.ToArray());
                foreach (var guid in guids)
                {
                    var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (existing.Contains(prefabPath))
                        continue;

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                        continue;

                    listProp.InsertArrayElementAtIndex(listProp.arraySize);
                    listProp.GetArrayElementAtIndex(listProp.arraySize - 1).objectReferenceValue = prefab;
                    existing.Add(prefabPath);
                    added++;
                }
            }

            serializedObject.ApplyModifiedProperties();
            Debug.Log($"[ThumbnailController] Added {added} prefab(s) to Prefabs To Capture.");
        }
    }
}
