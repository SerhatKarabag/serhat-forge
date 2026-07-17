using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Serhat.Forge.Tests.EditMode
{
    public sealed class CompositionAssetTests
    {
        private const string ProjectContextPath = "Assets/Resources/ProjectContext.prefab";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void ProjectContextPrefab_HasRequiredInstallersAndBootstrapperReference()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectContextPath);
            Assert.That(prefab, Is.Not.Null, $"Missing {ProjectContextPath}.");

            var components = GetComponentMap(prefab);
            Assert.That(components, Contains.Key("Zenject.ProjectContext"));
            Assert.That(components, Contains.Key("Serhat.Forge.Composition.ForgeProjectInstaller"));
            Assert.That(components, Contains.Key("Serhat.Forge.Composition.ForgeBootstrapInstaller"));
            Assert.That(components, Contains.Key("Serhat.Forge.Startup.GameBootstrapper"));

            var projectContext = components["Zenject.ProjectContext"];
            var serializedContext = new SerializedObject(projectContext);
            var installers = serializedContext.FindProperty("_monoInstallers");
            Assert.That(installers, Is.Not.Null, "ProjectContext installer list is not serialized.");
            Assert.That(installers.arraySize, Is.EqualTo(2),
                "ProjectContext must register exactly the project and bootstrap installers.");

            var installerTypes = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < installers.arraySize; index++)
            {
                var installer = installers.GetArrayElementAtIndex(index).objectReferenceValue as MonoBehaviour;
                Assert.That(installer, Is.Not.Null, $"ProjectContext installer at index {index} is missing.");
                installerTypes.Add(installer.GetType().FullName);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Serhat.Forge.Composition.ForgeProjectInstaller",
                    "Serhat.Forge.Composition.ForgeBootstrapInstaller"
                },
                installerTypes);

            var bootstrapInstaller = components["Serhat.Forge.Composition.ForgeBootstrapInstaller"];
            var bootstrapper = new SerializedObject(bootstrapInstaller).FindProperty("_bootstrapper");
            Assert.That(bootstrapper, Is.Not.Null);
            Assert.That(bootstrapper.objectReferenceValue, Is.SameAs(
                components["Serhat.Forge.Startup.GameBootstrapper"]));
        }

        [Test]
        public void SampleScene_HasSingleSceneContextAndDemoPanel()
        {
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
                Assert.That(scene.IsValid(), Is.True);

                var behaviours = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
                    .Where(behaviour => behaviour != null)
                    .ToArray();

                Assert.That(behaviours.Count(IsType("Zenject.SceneContext")), Is.EqualTo(1));
                Assert.That(behaviours.Count(IsType("Serhat.Forge.Demo.ForgeDemoPanel")), Is.EqualTo(1));
            }
            finally
            {
                if (previousSetup.Any(setup => setup.isLoaded))
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void ZenjectContainer_InjectsPrivateMethod()
        {
            var container = new DiContainer();
            var dependency = new InjectionDependency();
            var target = new PrivateInjectionTarget();

            container.Bind<InjectionDependency>().FromInstance(dependency);
            container.Inject(target);

            Assert.That(target.WasInjected, Is.True);
            Assert.That(target.Dependency, Is.SameAs(dependency));
        }

        [Test]
        public void DefaultContentConfiguration_DoesNotContactRemoteCatalog()
        {
            var configurationType = Type.GetType(
                "Serhat.Forge.Content.ContentConfiguration, Assembly-CSharp",
                throwOnError: false);
            Assert.That(configurationType, Is.Not.Null, "ContentConfiguration type is unavailable.");

            var createDefault = configurationType.GetMethod(
                "CreateDefault",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(createDefault, Is.Not.Null, "ContentConfiguration.CreateDefault is unavailable.");

            var configuration = createDefault.Invoke(null, null) as ScriptableObject;
            Assert.That(configuration, Is.Not.Null);

            try
            {
                var property = configurationType.GetProperty(
                    "CheckForCatalogUpdates",
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.That(property, Is.Not.Null);
                Assert.That(property.GetValue(configuration), Is.EqualTo(false),
                    "The template must not contact a remote catalog without explicit opt-in.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        private static Dictionary<string, MonoBehaviour> GetComponentMap(GameObject gameObject)
        {
            return gameObject.GetComponents<MonoBehaviour>()
                .Where(component => component != null)
                .ToDictionary(component => component.GetType().FullName, StringComparer.Ordinal);
        }

        private static Func<MonoBehaviour, bool> IsType(string fullName)
        {
            return behaviour => string.Equals(
                behaviour.GetType().FullName,
                fullName,
                StringComparison.Ordinal);
        }

        private sealed class InjectionDependency
        {
        }

        private sealed class PrivateInjectionTarget
        {
            public bool WasInjected { get; private set; }
            public InjectionDependency Dependency { get; private set; }

            [Inject]
            private void Construct(InjectionDependency dependency)
            {
                Dependency = dependency;
                WasInjected = true;
            }
        }
    }
}
