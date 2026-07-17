using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    public static class OpenWithDefaultImageEditor
    {
        [MenuItem("Assets/Open with Default Image Editor", false, 20)]
        private static void Open()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[ImageEditor] Asset file does not exist: {fullPath}");
                return;
            }

            try
            {
                EditorUtility.OpenWithDefaultApp(fullPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[ImageEditor] Could not open the system default image editor for '{fullPath}': " +
                    exception.Message);
            }
        }

        [MenuItem("Assets/Open with Default Image Editor", true)]
        private static bool Validate()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            return Path.GetExtension(assetPath).ToLowerInvariant() switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".psd" or
                    ".tif" or ".tiff" or ".gif" or ".exr" or ".hdr" => true,
                _ => false
            };
        }
    }
}
