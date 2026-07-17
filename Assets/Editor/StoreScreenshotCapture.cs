using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    internal static class StoreScreenshotCapture
    {
        private const string MenuPath = "Tools/Serhat Forge/Capture Store Screenshot";

        [MenuItem(MenuPath, priority = 200)]
        private static void Capture()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                Debug.LogError("[Serhat Forge] Could not resolve the project root.");
                return;
            }

            string outputDirectory = Path.Combine(projectRoot, "Screenshots");
            Directory.CreateDirectory(outputDirectory);

            string fileName = $"store-shot-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png";
            string outputPath = Path.Combine(outputDirectory, fileName);
            ScreenCapture.CaptureScreenshot(outputPath);

            Debug.Log($"[Serhat Forge] Screenshot queued: {outputPath}");
        }
    }
}