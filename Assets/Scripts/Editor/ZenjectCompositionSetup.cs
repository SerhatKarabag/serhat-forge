using System;
using System.Collections.Generic;
using System.Linq;
using Serhat.Forge.Composition;
using Serhat.Forge.Demo;
using Serhat.Forge.Startup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zenject;

namespace Serhat.Forge.Editor
{
    /// <summary>
    /// Creates or repairs the minimal Zenject composition assets shipped with the template.
    /// Existing user installers are preserved.
    /// </summary>
    public static class ZenjectCompositionSetup
    {
        private const string ProjectContextPath = "Assets/Resources/ProjectContext.prefab";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Tools/Serhat Forge/Setup/Repair Zenject Composition")]
        public static void RepairFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            RepairComposition();
            Debug.Log("[Serhat Forge] Zenject composition is ready.");
        }

        /// <summary>Batch-mode entry point used by CI and repository setup.</summary>
        public static void RepairComposition()
        {
            EnsureProjectContext();
            EnsureSampleSceneContext();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureProjectContext()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectContextPath);
            if (prefab == null)
            {
                CreateProjectContext();
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(ProjectContextPath);
            try
            {
                ConfigureProjectContext(root);
                PrefabUtility.SaveAsPrefabAsset(root, ProjectContextPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateProjectContext()
        {
            var root = new GameObject("ProjectContext");
            try
            {
                ConfigureProjectContext(root);
                PrefabUtility.SaveAsPrefabAsset(root, ProjectContextPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureProjectContext(GameObject root)
        {
            root.name = "ProjectContext";
            var context = GetOrAdd<ProjectContext>(root);
            var projectInstaller = GetOrAdd<ForgeProjectInstaller>(root);
            var bootstrapper = GetOrAdd<GameBootstrapper>(root);
            var bootstrapInstaller = GetOrAdd<ForgeBootstrapInstaller>(root);

            bootstrapInstaller.SetBootstrapper(bootstrapper);
            context.ParentNewObjectsUnderContext = true;

            var installers = new List<MonoInstaller> { projectInstaller, bootstrapInstaller };
            installers.AddRange(context.Installers.Where(
                installer => installer != null &&
                             installer != projectInstaller &&
                             installer != bootstrapInstaller));
            context.Installers = installers;

            EditorUtility.SetDirty(context);
            EditorUtility.SetDirty(projectInstaller);
            EditorUtility.SetDirty(bootstrapper);
            EditorUtility.SetDirty(bootstrapInstaller);
        }

        private static void EnsureSampleSceneContext()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) == null)
            {
                throw new InvalidOperationException($"Scene not found: {SampleScenePath}");
            }

            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
                var contexts = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<SceneContext>(true))
                    .ToArray();
                if (contexts.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"{SampleScenePath} contains multiple SceneContext components.");
                }

                if (contexts.Length == 0)
                {
                    new GameObject("SceneContext").AddComponent<SceneContext>();
                    EditorSceneManager.MarkSceneDirty(scene);
                }

                EnsureDemoPanel(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (!Application.isBatchMode && previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        private static void EnsureDemoPanel(UnityEngine.SceneManagement.Scene scene)
        {
            var panels = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ForgeDemoPanel>(true))
                .ToArray();
            if (panels.Length > 1)
            {
                throw new InvalidOperationException(
                    $"{SampleScenePath} contains multiple {nameof(ForgeDemoPanel)} components.");
            }

            if (panels.Length != 0)
                return;

            new GameObject("Serhat Forge Demo").AddComponent<ForgeDemoPanel>();
            EditorSceneManager.MarkSceneDirty(scene);
        }
        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            return gameObject.TryGetComponent<T>(out var component)
                ? component
                : gameObject.AddComponent<T>();
        }
    }
}