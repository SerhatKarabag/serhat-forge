using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    /// <summary>
    /// Deterministic command-line entry points for mobile IL2CPP smoke builds.
    /// Set SERHAT_FORGE_BUILD_PATH to override the default ignored Builds/Validation output.
    /// </summary>
    public static class SerhatForgeBatchBuild
    {
        private const string BuildPathEnvironmentVariable = "SERHAT_FORGE_BUILD_PATH";

        public static void BuildAndroidDevelopment()
        {
            EnsureIl2Cpp(NamedBuildTarget.Android);
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                throw new BuildFailedException("Android ARM64 must be enabled for this validation build.");

            bool previousBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                Build(
                    BuildTarget.Android,
                    ResolveOutputPath("SerhatForge.apk"),
                    BuildOptions.Development);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
            }
        }

        public static void BuildIosDevelopment()
        {
            EnsureIl2Cpp(NamedBuildTarget.iOS);
            Build(
                BuildTarget.iOS,
                ResolveOutputPath("iOS"),
                BuildOptions.Development);
        }

        private static void EnsureIl2Cpp(NamedBuildTarget target)
        {
            var backend = PlayerSettings.GetScriptingBackend(target);
            if (backend != ScriptingImplementation.IL2CPP)
            {
                throw new BuildFailedException(
                    $"{target.TargetName} must use IL2CPP for this validation build; current backend is {backend}.");
            }
        }

        private static void Build(
            BuildTarget target,
            string outputPath,
            BuildOptions options)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                .ToArray();

            if (scenes.Length == 0)
                throw new BuildFailedException("No enabled, valid scenes exist in Build Settings.");

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new BuildFailedException($"Could not resolve output directory for '{outputPath}'.");

            Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = options
            });

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"{target} build failed with {summary.totalErrors} error(s) and " +
                    $"{summary.totalWarnings} warning(s). See the Unity build log.");
            }

            Debug.Log(
                $"[Serhat Forge] {target} validation build succeeded: " +
                $"{summary.totalSize} bytes in {summary.totalTime}. Output: {outputPath}");
        }

        private static string ResolveOutputPath(string defaultName)
        {
            string configuredPath = Environment.GetEnvironmentVariable(BuildPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(configuredPath.Trim());

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new BuildFailedException("Could not resolve the Unity project root.");

            return Path.Combine(projectRoot, "Builds", "Validation", defaultName);
        }
    }
}
