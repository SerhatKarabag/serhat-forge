using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    /// <summary>
    /// Editor window to set up and manage the Runtime Thumbnail Generator scene.
    /// This creates a dedicated scene for high-quality thumbnail generation using actual URP rendering.
    /// </summary>
    public class RuntimeThumbnailGeneratorEditor : EditorWindow
    {
        [MenuItem("Serhat Forge/Runtime Thumbnail Generator (Setup)")]
        public static void ShowWindow()
        {
            GetWindow<RuntimeThumbnailGeneratorEditor>("RT Thumbnail Setup");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Runtime Thumbnail Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "This system uses a real scene with proper URP rendering for high-quality thumbnails.\n\n" +
                "Steps:\n" +
                "1. Click 'Create Thumbnail Scene' to set up the scene\n" +
                "2. Open the created scene\n" +
                "3. Enter Play Mode\n" +
                "4. Use the in-game UI to generate thumbnails",
                MessageType.Info);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Create Thumbnail Scene", GUILayout.Height(40)))
            {
                CreateThumbnailScene();
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Open Thumbnail Scene", GUILayout.Height(30)))
            {
                OpenThumbnailScene();
            }
        }

        private void CreateThumbnailScene()
        {
            // Create scene folder if needed
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            string scenePath = "Assets/Scenes/ThumbnailGenerator.unity";

            // Create new scene
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // Create root object for organization
            var root = new GameObject("=== THUMBNAIL GENERATOR ===");

            // Create Camera
            var cameraObj = new GameObject("ThumbnailCamera");
            cameraObj.transform.SetParent(root.transform);
            var camera = cameraObj.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(1f, 0f, 1f, 1f); // Magenta for easy removal
            camera.transform.position = new Vector3(0, 2, -5);
            camera.transform.LookAt(Vector3.zero);

            // Create Main Light (Key Light)
            var keyLightObj = new GameObject("KeyLight");
            keyLightObj.transform.SetParent(root.transform);
            var keyLight = keyLightObj.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.2f;
            keyLight.color = Color.white;
            keyLight.shadows = LightShadows.Soft;
            keyLightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Create Fill Light
            var fillLightObj = new GameObject("FillLight");
            fillLightObj.transform.SetParent(root.transform);
            var fillLight = fillLightObj.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.5f;
            fillLight.color = new Color(0.9f, 0.95f, 1f);
            fillLight.shadows = LightShadows.None;
            fillLightObj.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            // Create Rim Light
            var rimLightObj = new GameObject("RimLight");
            rimLightObj.transform.SetParent(root.transform);
            var rimLight = rimLightObj.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.intensity = 0.3f;
            rimLight.color = Color.white;
            rimLight.shadows = LightShadows.None;
            rimLightObj.transform.rotation = Quaternion.Euler(-10f, 180f, 0f);

            // Create spawn point for prefabs
            var spawnPoint = new GameObject("PrefabSpawnPoint");
            spawnPoint.transform.SetParent(root.transform);
            spawnPoint.transform.position = Vector3.zero;

            // Create the runtime controller
            var controllerObj = new GameObject("ThumbnailController");
            controllerObj.transform.SetParent(root.transform);
            controllerObj.AddComponent<RuntimeThumbnailController>();

            // Save scene
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            Debug.Log($"Thumbnail scene created at: {scenePath}");
            EditorUtility.DisplayDialog("Success", $"Thumbnail scene created!\n\nPath: {scenePath}\n\nOpen the scene and enter Play Mode to generate thumbnails.", "OK");
        }

        private void OpenThumbnailScene()
        {
            string scenePath = "Assets/Scenes/ThumbnailGenerator.unity";

            if (System.IO.File.Exists(scenePath))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
            }
            else
            {
                EditorUtility.DisplayDialog("Scene Not Found", "Thumbnail scene doesn't exist. Please create it first.", "OK");
            }
        }
    }
}
