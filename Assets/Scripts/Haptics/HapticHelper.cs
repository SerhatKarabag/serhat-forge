using System.Collections.Generic;
using UnityEngine;

#if NICE_VIBRATIONS || MOREMOUNTAINS_NICEVIBRATIONS_INSTALLED
using Lofelt.NiceVibrations;
#endif

namespace Serhat.Forge.Haptics
{
    /// <summary>
    /// Centralized haptic helper. When the FEEL/NiceVibrations package is installed,
    /// define <c>NICE_VIBRATIONS</c> in Player Settings -&gt; Scripting Define Symbols
    /// to route through Lofelt presets. Otherwise falls back to <see cref="Handheld.Vibrate"/>.
    ///
    /// Respects PlayerPrefs key <see cref="HapticEnabledKey"/> (default 1 = enabled).
    /// Toggle via <see cref="SetEnabled(bool)"/> from your Settings UI.
    /// </summary>
    public static class HapticHelper
    {
        public const string HapticEnabledKey = "HapticsEnabled";

        public enum Preset
        {
            None = -1,
            Selection = 0,
            Success = 1,
            Warning = 2,
            Failure = 3,
            Light = 4,
            Medium = 5,
            Heavy = 6,
            Rigid = 7,
            Soft = 8
        }

        private static readonly Dictionary<Preset, float> LastTriggerTimes = new();
        private static bool _initialized;

        public static bool IsEnabled() => PlayerPrefs.GetInt(HapticEnabledKey, 1) == 1;

        public static void SetEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(HapticEnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void Selection() => Trigger(Preset.Selection, 0.02f);
        public static void Light() => Trigger(Preset.Light, 0.03f);
        public static void Medium() => Trigger(Preset.Medium, 0.035f);
        public static void Heavy() => Trigger(Preset.Heavy, 0.04f);
        public static void Success() => Trigger(Preset.Success);
        public static void Warning() => Trigger(Preset.Warning);
        public static void Error() => Trigger(Preset.Failure);
        public static void Soft() => Trigger(Preset.Soft, 0.04f);
        public static void Rigid() => Trigger(Preset.Rigid, 0.04f);

        public static void PlayPreset(Preset preset, float minIntervalSeconds = 0f)
        {
            Trigger(preset, minIntervalSeconds);
        }

        private static void Trigger(Preset preset, float minIntervalSeconds = 0f)
        {
            if (preset == Preset.None || !IsEnabled())
                return;

            if (minIntervalSeconds > 0f)
            {
                float now = Time.unscaledTime;
                if (LastTriggerTimes.TryGetValue(preset, out float lastTime) && (now - lastTime) < minIntervalSeconds)
                    return;
                LastTriggerTimes[preset] = now;
            }

#if NICE_VIBRATIONS || MOREMOUNTAINS_NICEVIBRATIONS_INSTALLED
            if (!_initialized)
            {
                HapticController.Init();
                _initialized = true;
            }

            HapticPatterns.PlayPreset(MapToNicePreset(preset));
#else
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
#endif
        }

#if NICE_VIBRATIONS || MOREMOUNTAINS_NICEVIBRATIONS_INSTALLED
        private static HapticPatterns.PresetType MapToNicePreset(Preset preset)
        {
            return preset switch
            {
                Preset.Selection => HapticPatterns.PresetType.Selection,
                Preset.Light => HapticPatterns.PresetType.LightImpact,
                Preset.Medium => HapticPatterns.PresetType.MediumImpact,
                Preset.Heavy => HapticPatterns.PresetType.HeavyImpact,
                Preset.Success => HapticPatterns.PresetType.Success,
                Preset.Warning => HapticPatterns.PresetType.Warning,
                Preset.Failure => HapticPatterns.PresetType.Failure,
                Preset.Soft => HapticPatterns.PresetType.SoftImpact,
                Preset.Rigid => HapticPatterns.PresetType.RigidImpact,
                _ => HapticPatterns.PresetType.Selection
            };
        }
#endif
    }
}
