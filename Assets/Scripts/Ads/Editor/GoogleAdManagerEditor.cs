#if GOOGLE_MOBILE_ADS
using Serhat.Forge.Ads;
using UnityEditor;
using UnityEngine;

namespace Serhat.Forge.Editor
{
    [CustomEditor(typeof(GoogleAdManager))]
    public class GoogleAdManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty _enableRewarded;
        private SerializedProperty _enableInterstitial;
        private SerializedProperty _enableBanner;

        private SerializedProperty _androidRewardedId;
        private SerializedProperty _iosRewardedId;

        private SerializedProperty _androidInterstitialId;
        private SerializedProperty _iosInterstitialId;

        private SerializedProperty _androidBannerId;
        private SerializedProperty _iosBannerId;
        private SerializedProperty _bannerPosition;
        private SerializedProperty _showBannerOnInit;

        private SerializedProperty _maxRetryExponent;

        private void OnEnable()
        {
            _enableRewarded = serializedObject.FindProperty("enableRewarded");
            _enableInterstitial = serializedObject.FindProperty("enableInterstitial");
            _enableBanner = serializedObject.FindProperty("enableBanner");

            _androidRewardedId = serializedObject.FindProperty("androidRewardedId");
            _iosRewardedId = serializedObject.FindProperty("iosRewardedId");

            _androidInterstitialId = serializedObject.FindProperty("androidInterstitialId");
            _iosInterstitialId = serializedObject.FindProperty("iosInterstitialId");

            _androidBannerId = serializedObject.FindProperty("androidBannerId");
            _iosBannerId = serializedObject.FindProperty("iosBannerId");
            _bannerPosition = serializedObject.FindProperty("bannerPosition");
            _showBannerOnInit = serializedObject.FindProperty("showBannerOnInit");

            _maxRetryExponent = serializedObject.FindProperty("maxRetryExponent");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Google AdMob", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Enabled Ad Types", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableRewarded);
            EditorGUILayout.PropertyField(_enableInterstitial);
            EditorGUILayout.PropertyField(_enableBanner);

            EditorGUILayout.Space(8);

            if (_enableRewarded.boolValue)
            {
                EditorGUILayout.LabelField("Rewarded Ad Unit IDs", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_androidRewardedId, new GUIContent("Android"));
                EditorGUILayout.PropertyField(_iosRewardedId, new GUIContent("iOS"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            if (_enableInterstitial.boolValue)
            {
                EditorGUILayout.LabelField("Interstitial Ad Unit IDs", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_androidInterstitialId, new GUIContent("Android"));
                EditorGUILayout.PropertyField(_iosInterstitialId, new GUIContent("iOS"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            if (_enableBanner.boolValue)
            {
                EditorGUILayout.LabelField("Banner Ad Unit IDs", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_androidBannerId, new GUIContent("Android"));
                EditorGUILayout.PropertyField(_iosBannerId, new GUIContent("iOS"));
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_bannerPosition);
                EditorGUILayout.PropertyField(_showBannerOnInit);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.LabelField("Retry Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_maxRetryExponent);
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
