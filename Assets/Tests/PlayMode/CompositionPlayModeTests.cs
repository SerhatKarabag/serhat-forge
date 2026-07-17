using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;

namespace Serhat.Forge.Tests.PlayMode
{
    public sealed class CompositionPlayModeTests
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [UnityTest]
        public IEnumerator SampleScene_CompositionIsAliveAndPrivateDependenciesAreInjected()
        {
            var loadOperation = SceneManager.LoadSceneAsync(SampleScenePath, LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null, $"Could not load {SampleScenePath}.");
            yield return loadOperation;
            yield return null;

            var projectContext = ProjectContext.Instance;
            Assert.That(projectContext, Is.Not.Null, "ProjectContext was not initialized.");

            var sceneContexts = UnityEngine.Object
                .FindObjectsByType<SceneContext>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(context => context.gameObject.scene == SceneManager.GetActiveScene())
                .ToArray();
            Assert.That(sceneContexts, Has.Length.EqualTo(1));

            var behaviours = UnityEngine.Object
                .FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrapper = FindSingle(behaviours, "Serhat.Forge.Startup.GameBootstrapper");
            var demoPanel = FindSingle(behaviours, "Serhat.Forge.Demo.ForgeDemoPanel");

            AssertPrivateBoolean(bootstrapper, "_dependenciesInjected", expected: true);
            AssertPrivateReferenceAssigned(demoPanel, "_bootstrapper");
            AssertPrivateReferenceAssigned(demoPanel, "_contentManager");
            AssertPrivateReferenceAssigned(demoPanel, "_audioMuteService");
            AssertPrivateReferenceAssigned(demoPanel, "_adService");
        }

        private static MonoBehaviour FindSingle(MonoBehaviour[] behaviours, string fullName)
        {
            var matches = behaviours
                .Where(behaviour => behaviour != null)
                .Where(behaviour => string.Equals(
                    behaviour.GetType().FullName,
                    fullName,
                    StringComparison.Ordinal))
                .ToArray();

            Assert.That(matches, Has.Length.EqualTo(1),
                $"Expected exactly one active-scene component of type {fullName}.");
            return matches[0];
        }

        private static void AssertPrivateBoolean(object instance, string fieldName, bool expected)
        {
            var field = GetPrivateField(instance, fieldName);
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));
            Assert.That((bool)field.GetValue(instance), Is.EqualTo(expected));
        }

        private static void AssertPrivateReferenceAssigned(object instance, string fieldName)
        {
            var field = GetPrivateField(instance, fieldName);
            Assert.That(field.GetValue(instance), Is.Not.Null,
                $"{instance.GetType().FullName}.{fieldName} was not injected.");
        }

        private static FieldInfo GetPrivateField(object instance, string fieldName)
        {
            Assert.That(instance, Is.Not.Null);
            var field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"Missing private field {instance.GetType().FullName}.{fieldName}.");
            return field;
        }
    }
}
