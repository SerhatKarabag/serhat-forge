using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    /// <summary>
    /// Applies the minimum identity and build settings a cloned template needs.
    /// The window never accepts or stores service credentials.
    /// </summary>
    public sealed class SerhatForgeProjectSetupWindow : EditorWindow
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MenuPath = "Tools/Serhat Forge/Setup/Project Settings";
        private const string UnityPurchasingDefine = "UNITY_PURCHASING";

        private static readonly Regex BundleIdentifierPattern = new(
            @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){2,}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex IosBuildNumberPattern = new(
            @"^[0-9]+(\.[0-9]+){0,2}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [SerializeField] private string _companyName = "Your Studio";
        [SerializeField] private string _productName = "New Game";
        [SerializeField] private string _bundleIdentifier = "com.yourstudio.newgame";
        [SerializeField] private string _bundleVersion = "0.1.0";
        [SerializeField] private int _androidVersionCode = 1;
        [SerializeField] private string _iosBuildNumber = "1";
        [SerializeField] private bool _configureTemplateScenes = true;
        [SerializeField] private bool _configureMobileIl2Cpp = true;
        [SerializeField] private bool _enableUnityIap;

        private Vector2 _scroll;

        [MenuItem(MenuPath, priority = 1)]
        public static void Open()
        {
            var window = GetWindow<SerhatForgeProjectSetupWindow>();
            window.titleContent = new GUIContent("Serhat Forge Setup");
            window.minSize = new Vector2(480f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadCurrentSettings();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Serhat Forge Project Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Run this once after cloning the template. It updates local project identity and build settings only; credentials and production service configuration are intentionally out of scope.",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            _companyName = EditorGUILayout.TextField("Company Name", _companyName);
            _productName = EditorGUILayout.TextField("Product Name", _productName);
            _bundleIdentifier = EditorGUILayout.TextField("Bundle Identifier", _bundleIdentifier);
            _bundleVersion = EditorGUILayout.TextField("Bundle Version", _bundleVersion);
            _androidVersionCode = EditorGUILayout.IntField("Android Version Code", _androidVersionCode);
            _iosBuildNumber = EditorGUILayout.TextField("iOS Build Number", _iosBuildNumber);

            EditorGUILayout.Space(8f);
            _configureTemplateScenes = EditorGUILayout.ToggleLeft(
                "Put the Serhat Forge sample scene first in Build Settings",
                _configureTemplateScenes);
            _configureMobileIl2Cpp = EditorGUILayout.ToggleLeft(
                "Configure Android and iOS for IL2CPP",
                _configureMobileIl2Cpp);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Optional Integrations", EditorStyles.boldLabel);
            _enableUnityIap = EditorGUILayout.ToggleLeft(
                "Enable Unity IAP client code (UNITY_PURCHASING)",
                _enableUnityIap);
            EditorGUILayout.HelpBox(
                "Enabling the client does not verify purchases. Before shipping, connect it to the hardened cloud backend and configure store credentials outside the repository.",
                MessageType.Warning);

            EditorGUILayout.Space(12f);
            DrawValidation();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload Current Settings", GUILayout.Height(30f)))
                {
                    LoadCurrentSettings();
                    GUI.FocusControl(null);
                }

                using (new EditorGUI.DisabledScope(!IsValid(out _)))
                {
                    if (GUILayout.Button("Apply Project Setup", GUILayout.Height(30f)))
                    {
                        ApplyProjectSetup();
                    }
                }
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "The public template contains no UGS environment, Firebase configuration, store credentials, signing keys, or production backend secrets. Configure those integrations separately per environment.",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawValidation()
        {
            if (IsValid(out var validationError))
            {
                EditorGUILayout.HelpBox("Settings are valid.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(validationError, MessageType.Error);
        }

        private bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(_companyName))
            {
                error = "Company Name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_productName))
            {
                error = "Product Name is required.";
                return false;
            }

            var normalizedBundleId = _bundleIdentifier?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!BundleIdentifierPattern.IsMatch(normalizedBundleId))
            {
                error = "Bundle Identifier must be a lowercase reverse-domain identifier, for example com.yourstudio.game.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_bundleVersion))
            {
                error = "Bundle Version is required.";
                return false;
            }

            if (_androidVersionCode < 1)
            {
                error = "Android Version Code must be at least 1.";
                return false;
            }

            if (!IosBuildNumberPattern.IsMatch(_iosBuildNumber?.Trim() ?? string.Empty))
            {
                error = "iOS Build Number must contain one to three numeric components.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void LoadCurrentSettings()
        {
            _companyName = string.IsNullOrWhiteSpace(PlayerSettings.companyName)
                ? "Your Studio"
                : PlayerSettings.companyName;
            _productName = string.IsNullOrWhiteSpace(PlayerSettings.productName)
                ? "New Game"
                : PlayerSettings.productName;

            var currentIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            _bundleIdentifier = string.IsNullOrWhiteSpace(currentIdentifier)
                ? "com.yourstudio.newgame"
                : currentIdentifier.ToLowerInvariant();
            _bundleVersion = string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion)
                ? "0.1.0"
                : PlayerSettings.bundleVersion;
            _androidVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode);
            _iosBuildNumber = string.IsNullOrWhiteSpace(PlayerSettings.iOS.buildNumber)
                ? "1"
                : PlayerSettings.iOS.buildNumber;
            _enableUnityIap = HasScriptingDefine(NamedBuildTarget.Android, UnityPurchasingDefine) ||
                              HasScriptingDefine(NamedBuildTarget.iOS, UnityPurchasingDefine) ||
                              HasScriptingDefine(NamedBuildTarget.Standalone, UnityPurchasingDefine);
        }

        private void ApplyProjectSetup()
        {
            if (!IsValid(out var validationError))
            {
                EditorUtility.DisplayDialog("Invalid project settings", validationError, "OK");
                return;
            }

            var confirmed = EditorUtility.DisplayDialog(
                "Apply Serhat Forge setup?",
                "This will update Player Settings and may reorder enabled Build Settings scenes. No credentials will be changed.",
                "Apply",
                "Cancel");

            if (!confirmed)
                return;

            try
            {
                var normalizedBundleId = _bundleIdentifier.Trim().ToLowerInvariant();
                PlayerSettings.companyName = _companyName.Trim();
                PlayerSettings.productName = _productName.Trim();
                PlayerSettings.bundleVersion = _bundleVersion.Trim();
                PlayerSettings.Android.bundleVersionCode = _androidVersionCode;
                PlayerSettings.iOS.buildNumber = _iosBuildNumber.Trim();

                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, normalizedBundleId);
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, normalizedBundleId);
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, normalizedBundleId);

                SetScriptingDefine(NamedBuildTarget.Android, UnityPurchasingDefine, _enableUnityIap);
                SetScriptingDefine(NamedBuildTarget.iOS, UnityPurchasingDefine, _enableUnityIap);
                SetScriptingDefine(NamedBuildTarget.Standalone, UnityPurchasingDefine, _enableUnityIap);

                if (_configureMobileIl2Cpp)
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
                    PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);
                    PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Medium);
                }

                if (_configureTemplateScenes)
                    ConfigureBuildScenes();

                AssetDatabase.SaveAssets();

                Debug.Log(
                    $"[Serhat Forge] Project setup applied: company='{PlayerSettings.companyName}', " +
                    $"product='{PlayerSettings.productName}', bundle='{normalizedBundleId}', " +
                    $"unityIap={_enableUnityIap}.");

                EditorUtility.DisplayDialog(
                    "Serhat Forge setup complete",
                    "Project identity and build settings were updated. Configure optional services from their environment-specific setup guides.",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Project setup failed",
                    "No credentials were changed. See the Console for the exact error.",
                    "OK");
            }
        }

        private static void ConfigureBuildScenes()
        {
            var preferredPaths = new[] { SampleScenePath }
                .Where(SceneExists)
                .ToArray();

            var preferredSet = new HashSet<string>(preferredPaths, StringComparer.Ordinal);
            var existingScenes = EditorBuildSettings.scenes
                .Where(scene => !string.IsNullOrWhiteSpace(scene.path) && !preferredSet.Contains(scene.path));

            var orderedScenes = preferredPaths
                .Select(path => new EditorBuildSettingsScene(path, true))
                .Concat(existingScenes)
                .ToArray();

            EditorBuildSettings.scenes = orderedScenes;
        }

        private static bool SceneExists(string path)
        {
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;
        }

        private static bool HasScriptingDefine(NamedBuildTarget target, string define)
        {
            return PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(symbol => string.Equals(symbol.Trim(), define, StringComparison.Ordinal));
        }

        private static void SetScriptingDefine(
            NamedBuildTarget target,
            string define,
            bool enabled)
        {
            var symbols = new HashSet<string>(
                PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(symbol => symbol.Trim())
                    .Where(symbol => !string.IsNullOrWhiteSpace(symbol)),
                StringComparer.Ordinal);

            if (enabled)
                symbols.Add(define);
            else
                symbols.Remove(define);

            PlayerSettings.SetScriptingDefineSymbols(
                target,
                string.Join(";", symbols.OrderBy(symbol => symbol, StringComparer.Ordinal)));
        }
    }
}
