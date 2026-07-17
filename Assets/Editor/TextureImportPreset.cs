using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    /// <summary>
    /// Applies safe Sprite defaults only to new textures under Assets/Art/Sprites/.
    /// </summary>
    public class TextureImportPreset : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Art/Sprites/", System.StringComparison.OrdinalIgnoreCase))
                return;

            TextureImporter importer = (TextureImporter)assetImporter;

            // Only apply to new imports (not reimports)
            if (importer.importSettingsMissing)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
            }
        }
    }
}
