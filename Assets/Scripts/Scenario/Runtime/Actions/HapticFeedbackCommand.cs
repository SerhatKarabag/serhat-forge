using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that triggers haptic feedback (vibration) on mobile devices.
    /// Uses FEEL/NiceVibrations when available, falls back to native platform APIs.
    /// </summary>
    [Serializable]
    public class HapticFeedbackCommand : BaseCommand
    {
        private const string HapticsEnabledKey = "HapticsEnabled";
        private const long NativeFallbackDurationMs = 50L;

        private static bool _feelLookupDone;
        private static Type _feelPresetType;
        private static MethodInfo _feelInitMethod;
        private static MethodInfo _feelPlayPresetMethod;
        private static readonly Dictionary<HapticType, float> LastTriggerTimes = new Dictionary<HapticType, float>();

        public enum HapticType
        {
            Light,
            Medium,
            Heavy,
            Selection,
            Success,
            Warning,
            Error,
            Soft,
            Rigid
        }

        [SerializeField]
        private HapticType _hapticType = HapticType.Medium;

        [SerializeField]
        [Range(0f, 0.2f)]
        [Tooltip("Minimum interval between same haptic type triggers. Useful for dense consume bursts.")]
        private float _minIntervalSeconds = 0.03f;

        public HapticType Type
        {
            get => _hapticType;
            set => _hapticType = value;
        }

        public HapticFeedbackCommand()
        {
        }

        public HapticFeedbackCommand(HapticType type)
        {
            _hapticType = type;
        }

        public override Task Execute()
        {
            if (PlayerPrefs.GetInt(HapticsEnabledKey, 1) != 1)
            {
                return Task.CompletedTask;
            }

            if (_minIntervalSeconds > 0f)
            {
                var now = Time.unscaledTime;
                if (LastTriggerTimes.TryGetValue(_hapticType, out var lastTime) &&
                    (now - lastTime) < _minIntervalSeconds)
                {
                    return Task.CompletedTask;
                }
                LastTriggerTimes[_hapticType] = now;
            }

            if (TryPlayFeelPreset(_hapticType))
            {
                return Task.CompletedTask;
            }

#if UNITY_IOS && !UNITY_EDITOR
            TriggerIOSHaptic();
#elif UNITY_ANDROID && !UNITY_EDITOR
            TriggerAndroidHaptic();
#else
            Debug.Log($"[HapticFeedbackCommand] Haptic triggered: {_hapticType}");
#endif
            return Task.CompletedTask;
        }

        private static bool TryPlayFeelPreset(HapticType type)
        {
            if (!EnsureFeelLookup())
            {
                return false;
            }

            try
            {
                _feelInitMethod?.Invoke(null, null);
                object presetValue = Enum.Parse(_feelPresetType, ToFeelPresetName(type));
                _feelPlayPresetMethod?.Invoke(null, new[] { presetValue });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HapticFeedbackCommand] FEEL haptic fallback to native: {e.Message}");
                return false;
            }
        }

        private static bool EnsureFeelLookup()
        {
            if (_feelLookupDone)
            {
                return _feelPresetType != null && _feelPlayPresetMethod != null;
            }

            _feelLookupDone = true;

            Type hapticControllerType = System.Type.GetType("Lofelt.NiceVibrations.HapticController, Lofelt.NiceVibrations");
            Type hapticPatternsType = System.Type.GetType("Lofelt.NiceVibrations.HapticPatterns, Lofelt.NiceVibrations");
            _feelPresetType = System.Type.GetType("Lofelt.NiceVibrations.HapticPatterns+PresetType, Lofelt.NiceVibrations");

            if (hapticControllerType == null || hapticPatternsType == null || _feelPresetType == null)
            {
                return false;
            }

            _feelInitMethod = hapticControllerType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
            _feelPlayPresetMethod = hapticPatternsType.GetMethod(
                "PlayPreset",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { _feelPresetType },
                modifiers: null);

            return _feelPlayPresetMethod != null;
        }

        private static string ToFeelPresetName(HapticType type)
        {
            switch (type)
            {
                case HapticType.Light:
                    return "LightImpact";
                case HapticType.Medium:
                    return "MediumImpact";
                case HapticType.Heavy:
                    return "HeavyImpact";
                case HapticType.Selection:
                    return "Selection";
                case HapticType.Success:
                    return "Success";
                case HapticType.Warning:
                    return "Warning";
                case HapticType.Error:
                    return "Failure";
                case HapticType.Soft:
                    return "SoftImpact";
                case HapticType.Rigid:
                    return "RigidImpact";
                default:
                    return "MediumImpact";
            }
        }

#if UNITY_IOS
        private void TriggerIOSHaptic()
        {
            switch (_hapticType)
            {
                case HapticType.Light:
                    IOSHapticFeedback.ImpactLight();
                    break;
                case HapticType.Medium:
                    IOSHapticFeedback.ImpactMedium();
                    break;
                case HapticType.Heavy:
                    IOSHapticFeedback.ImpactHeavy();
                    break;
                case HapticType.Selection:
                    IOSHapticFeedback.Selection();
                    break;
                case HapticType.Success:
                    IOSHapticFeedback.NotificationSuccess();
                    break;
                case HapticType.Warning:
                    IOSHapticFeedback.NotificationWarning();
                    break;
                case HapticType.Error:
                    IOSHapticFeedback.NotificationError();
                    break;
                case HapticType.Soft:
                    IOSHapticFeedback.ImpactLight();
                    break;
                case HapticType.Rigid:
                    IOSHapticFeedback.ImpactMedium();
                    break;
            }
        }
#endif

#if UNITY_ANDROID
        private void TriggerAndroidHaptic()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator == null)
                    {
                        return;
                    }

                    if (GetSDKLevel() >= 26)
                    {
                        int amplitude = GetAmplitudeForType(_hapticType);
                        using (var vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        {
                            var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                                "createOneShot", NativeFallbackDurationMs, amplitude);
                            vibrator.Call("vibrate", effect);
                        }
                    }
                    else
                    {
                        vibrator.Call("vibrate", NativeFallbackDurationMs);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HapticFeedbackCommand] Android haptic failed: {e.Message}");
            }
        }

        private static int GetSDKLevel()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                return version.GetStatic<int>("SDK_INT");
            }
        }

        private static int GetAmplitudeForType(HapticType type)
        {
            switch (type)
            {
                case HapticType.Light:
                    return 50;
                case HapticType.Medium:
                    return 128;
                case HapticType.Heavy:
                    return 255;
                case HapticType.Selection:
                    return 30;
                case HapticType.Success:
                    return 150;
                case HapticType.Warning:
                    return 180;
                case HapticType.Error:
                    return 220;
                case HapticType.Soft:
                    return 80;
                case HapticType.Rigid:
                    return 170;
                default:
                    return 128;
            }
        }
#endif
    }

#if UNITY_IOS
    /// <summary>
    /// iOS Haptic Feedback wrapper using native plugin calls.
    /// Uses the native HapticFeedback.mm plugin.
    /// </summary>
    public static class IOSHapticFeedback
    {
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ImpactFeedback(int style);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _SelectionFeedback();

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _NotificationFeedback(int type);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _PrepareHaptics();

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern bool _HapticsSupported();

        public static void Prepare()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _PrepareHaptics();
            }
        }

        public static bool IsSupported()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                return _HapticsSupported();
            }

            return false;
        }

        public static void ImpactLight()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _ImpactFeedback(0);
            }
        }

        public static void ImpactMedium()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _ImpactFeedback(1);
            }
        }

        public static void ImpactHeavy()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _ImpactFeedback(2);
            }
        }

        public static void Selection()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _SelectionFeedback();
            }
        }

        public static void NotificationSuccess()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _NotificationFeedback(0);
            }
        }

        public static void NotificationWarning()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _NotificationFeedback(1);
            }
        }

        public static void NotificationError()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                _NotificationFeedback(2);
            }
        }
    }
#endif
}
