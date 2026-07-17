using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Serhat.Forge.UI.Components;

namespace Serhat.Forge.Editor
{
    /// <summary>
    /// Editor tool that builds the entire LoadingScreen hierarchy in the active scene.
    /// Menu: Serhat Forge → Create Loading Screen
    ///
    /// Creates:
    ///   LoadingScreenCanvas (Canvas + CanvasScaler + DynamicCanvasScaler + LoadingScreen)
    ///     ├── BG_Gradient    (Image – shift_screen_bg, stretched)
    ///     ├── PatternOverlay (RawImage – bg_pattern, tiled via UV + scrolled by script)
    ///     └── Logo           (Image – logo, centred)
    ///
    /// Assets are searched first in Assets/NewSprites/LoadingPanel/, then project-wide.
    /// Pattern texture must be a seamless square tile from the designer.
    /// </summary>
    public static class LoadingScreenCreator
    {
        // ── Asset folder & search names ─────────────────────────────────
        // Drop your loading-panel art into this folder, with these filenames,
        // or change the constants to match your project's asset layout.
        private const string ASSET_FOLDER         = "Assets/Sprites/LoadingPanel";
        private const string PATTERN_TEXTURE_NAME = "bg_pattern";
        private const string LOGO_SPRITE_NAME     = "logo";
        private const string GRADIENT_SPRITE_NAME = "loading_bg";

        // ── Design constants (1080×2160 reference) ──────────────────────
        private static readonly Vector2 ReferenceResolution = new Vector2(1080, 2160);
        private const float PATTERN_TILE_X = 2f;
        private const float PATTERN_TILE_Y = 3.5f;
        private const byte  PATTERN_ALPHA  = 40;   // ~15 % opacity
        private const float LOGO_WIDTH     = 700f;
        private const float LOGO_HEIGHT    = 350f;
        private const float LOGO_Y_OFFSET  = 80f;

        // ================================================================
        [MenuItem("Serhat Forge/Create Loading Screen")]
        public static void CreateLoadingScreen()
        {
            // ── 1. Canvas ───────────────────────────────────────────────
            var canvasGO = new GameObject("LoadingScreenCanvas");
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Loading Screen");

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.AddComponent<DynamicCanvasScaler>();

            // ── 2. BG Gradient (bottom layer) ───────────────────────────
            var bgGO = CreateStretchedChild(canvasGO.transform, "BG_Gradient");
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.raycastTarget = false;

            var gradientSprite = FindSpriteInFolder(GRADIENT_SPRITE_NAME);
            if (gradientSprite != null)
            {
                bgImage.sprite = gradientSprite;
                bgImage.type = Image.Type.Simple;
                bgImage.preserveAspect = false;
            }
            else
            {
                bgImage.color = new Color32(45, 40, 100, 255);
                LogMissing(GRADIENT_SPRITE_NAME);
            }

            // ── 3. Pattern Overlay (RawImage, UV tiling) ────────────────
            var patternGO = CreateStretchedChild(canvasGO.transform, "PatternOverlay");
            var patternRaw = patternGO.AddComponent<RawImage>();
            patternRaw.raycastTarget = false;
            patternRaw.color = new Color32(255, 255, 255, PATTERN_ALPHA);
            patternRaw.uvRect = new Rect(0, 0, PATTERN_TILE_X, PATTERN_TILE_Y);

            var patternTex = FindTextureInFolder(PATTERN_TEXTURE_NAME);
            if (patternTex != null)
            {
                EnsureTextureWrapRepeat(patternTex);
                patternRaw.texture = patternTex;
            }
            else
            {
                LogMissing(PATTERN_TEXTURE_NAME);
            }

            // ── 4. Logo (top layer) ────────────────────────────────────
            var logoGO = new GameObject("Logo");
            logoGO.transform.SetParent(canvasGO.transform, false);

            var logoRect = logoGO.AddComponent<RectTransform>();
            logoRect.anchorMin = new Vector2(0.5f, 0.5f);
            logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            logoRect.pivot = new Vector2(0.5f, 0.5f);
            logoRect.sizeDelta = new Vector2(LOGO_WIDTH, LOGO_HEIGHT);
            logoRect.anchoredPosition = new Vector2(0, LOGO_Y_OFFSET);

            var logoImage = logoGO.AddComponent<Image>();
            logoImage.raycastTarget = false;
            logoImage.preserveAspect = true;

            var logoSprite = FindSpriteInFolder(LOGO_SPRITE_NAME);
            if (logoSprite != null)
            {
                logoImage.sprite = logoSprite;
                logoImage.SetNativeSize();
                logoRect.anchoredPosition = new Vector2(0, LOGO_Y_OFFSET);
            }
            else
            {
                LogMissing(LOGO_SPRITE_NAME);
            }

            // ── 5. CanvasGroup (required for fade) ──────────────────────
            canvasGO.AddComponent<CanvasGroup>();

            // ── 6. LoadingScreen component ──────────────────────────────
            var loadingScreen = canvasGO.AddComponent<LoadingScreen>();

            var so = new SerializedObject(loadingScreen);
            so.FindProperty("_patternImage").objectReferenceValue = patternRaw;
            so.FindProperty("_targetTileSize").floatValue = 900f;
            so.FindProperty("_scrollSpeed").vector2Value = new Vector2(0.03f, 0.06f);
            so.FindProperty("_logo").objectReferenceValue = logoRect;
            so.FindProperty("_logoFloatAmount").floatValue = 30f;
            so.FindProperty("_logoFloatSpeed").floatValue = 1.5f;
            so.FindProperty("_fadeInDuration").floatValue = 0.35f;
            so.FindProperty("_fadeOutDuration").floatValue = 0.25f;
            so.FindProperty("_minimumDisplayTime").floatValue = 2f;
            so.ApplyModifiedProperties();

            // ── Done ────────────────────────────────────────────────────
            Selection.activeGameObject = canvasGO;
            EditorUtility.SetDirty(canvasGO);

            Debug.Log("<color=#44cc88>[LoadingScreen]</color> Loading screen created! " +
                      "Hit <b>Play</b> to see the diagonal pattern scroll animation.");
        }

        // ================================================================
        //  Hierarchy helpers
        // ================================================================

        private static GameObject CreateStretchedChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return go;
        }

        // ================================================================
        //  Asset lookup – ASSET_FOLDER first, then project-wide
        // ================================================================

        private static Sprite FindSpriteInFolder(string name)
        {
            return FindSpriteInPath(name, ASSET_FOLDER)
                ?? FindSpriteInPath(name, null);
        }

        private static Texture2D FindTextureInFolder(string name)
        {
            return FindTextureInPath(name, ASSET_FOLDER)
                ?? FindTextureInPath(name, null);
        }

        private static Sprite FindSpriteInPath(string name, string searchFolder)
        {
            string filter = $"{name} t:Sprite";
            string[] folders = string.IsNullOrEmpty(searchFolder) ? null : new[] { searchFolder };

            var guids = folders != null
                ? AssetDatabase.FindAssets(filter, folders)
                : AssetDatabase.FindAssets(filter);

            if (guids.Length == 0)
            {
                filter = $"{name} t:Texture2D";
                guids = folders != null
                    ? AssetDatabase.FindAssets(filter, folders)
                    : AssetDatabase.FindAssets(filter);
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(fileName, name, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) return sprite;

                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (a is Sprite s) return s;
            }

            return null;
        }

        private static Texture2D FindTextureInPath(string name, string searchFolder)
        {
            string filter = $"{name} t:Texture2D";
            string[] folders = string.IsNullOrEmpty(searchFolder) ? null : new[] { searchFolder };

            var guids = folders != null
                ? AssetDatabase.FindAssets(filter, folders)
                : AssetDatabase.FindAssets(filter);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(fileName, name, System.StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return null;
        }

        // ================================================================
        //  Texture helpers
        // ================================================================

        private static void EnsureTextureWrapRepeat(Texture2D tex)
        {
            var path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
                Debug.Log($"<color=#ffcc00>[LoadingScreen]</color> " +
                          $"Changed Wrap Mode to <b>Repeat</b> for: {path}");
            }
        }

        private static void LogMissing(string assetName)
        {
            Debug.LogWarning($"<color=#ff8844>[LoadingScreen]</color> " +
                             $"<b>{assetName}</b> not found! " +
                             $"Import it to <b>{ASSET_FOLDER}/{assetName}.png</b> " +
                             $"then re-run this tool, or drag it manually into the Inspector.");
        }
    }
}
