using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    public static class MaterialCreationModeNone
    {
        [MenuItem("Assets/Set Material Creation Mode → None", true)]
        private static bool Validate()
        {
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (AssetImporter.GetAtPath(path) is ModelImporter)
                    return true;
            }
            return false;
        }

        [MenuItem("Assets/Set Material Creation Mode → None", false, 30)]
        private static void Execute()
        {
            var selected = Selection.objects;
            int changed = 0;
            int skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < selected.Length; i++)
                {
                    var path = AssetDatabase.GetAssetPath(selected[i]);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null)
                    {
                        skipped++;
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Material Creation Mode → None",
                        $"({i + 1}/{selected.Length}) {System.IO.Path.GetFileName(path)}",
                        (float)i / selected.Length);

                    if (importer.materialImportMode == ModelImporterMaterialImportMode.None)
                    {
                        skipped++;
                        continue;
                    }

                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[MaterialCreationMode] Done — {changed} changed, {skipped} skipped (already None or not a model).");
        }
    }
}
