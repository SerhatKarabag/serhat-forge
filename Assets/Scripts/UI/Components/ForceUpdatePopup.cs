using System;
using System.Collections;
using Serhat.Forge.Updates;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Popup controller for force-update UX.
    /// If view references are assigned, it drives that custom UI.
    /// Otherwise it falls back to a self-generated runtime popup.
    /// </summary>
    public class ForceUpdatePopup : MonoBehaviour
    {
        [Header("View Mode")]
        [SerializeField] private bool _hideOnAwake = true;
        [SerializeField] private bool _buildRuntimeUiIfMissing = true;
        [SerializeField] private GameObject _visibilityRoot;
        [SerializeField] private GameObject _overlayObject;
        [SerializeField] private CanvasGroup _overlayCanvasGroup;
        [SerializeField] private RectTransform _dialogRoot;
        [SerializeField] private CanvasGroup _dialogCanvasGroup;
        [SerializeField] private TextMeshProUGUI _popupTitleText;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private TextMeshProUGUI _currentVersionText;
        [SerializeField] private TextMeshProUGUI _minimumVersionText;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private Button _updateButton;
        [SerializeField] private Image _updateButtonBackground;
        [SerializeField] private TextMeshProUGUI _updateButtonText;

        [Header("Copy")]
        [SerializeField] private string _popupTitleLabel = "UPDATE";
        [SerializeField] private string _defaultTitle = "A new update is available!";
        [SerializeField] private string _defaultMessage = "Update now for the best experience.";
        [SerializeField] private string _updateButtonLabel = "Update Now";
        [SerializeField] private string _currentVersionLabel = "CURRENT";
        [SerializeField] private string _requiredVersionLabel = "REQUIRED";

        [Header("Runtime Fallback Colors")]
        [SerializeField] private Color _overlayColor = new Color(0.04f, 0.08f, 0.12f, 0.82f);
        [SerializeField] private Color _dialogColor = new Color(0.98f, 0.95f, 0.89f, 1f);
        [SerializeField] private Color _headerColor = new Color(0.98f, 0.63f, 0.25f, 1f);
        [SerializeField] private Color _headerAccentColor = new Color(0.89f, 0.34f, 0.18f, 1f);
        [SerializeField] private Color _chipColor = new Color(1f, 1f, 1f, 0.75f);
        [SerializeField] private Color _chipLabelColor = new Color(0.55f, 0.38f, 0.12f, 1f);
        [SerializeField] private Color _chipValueColor = new Color(0.10f, 0.14f, 0.20f, 1f);
        [SerializeField] private Color _buttonColor = new Color(0.15f, 0.60f, 0.28f, 1f);
        [SerializeField] private Color _buttonPressedColor = new Color(0.11f, 0.48f, 0.22f, 1f);
        [SerializeField] private Color _buttonDisabledColor = new Color(0.60f, 0.66f, 0.70f, 1f);
        [SerializeField] private Color _buttonTextColor = Color.white;
        [SerializeField] private Color _titleColor = new Color(0.10f, 0.14f, 0.20f, 1f);
        [SerializeField] private Color _messageColor = new Color(0.23f, 0.27f, 0.34f, 1f);
        [SerializeField] private Color _statusColor = new Color(0.38f, 0.44f, 0.50f, 1f);

        private const float ShowAnimationDuration = 0.18f;
        private const string MarketDetailsPrefix = "market://details?";

        private Canvas _canvas;
        private ForceUpdateRequirement _currentRequirement;
        private Coroutine _showAnimationCoroutine;
        private bool _viewInitialized;
        private bool _skipHideOnAwake;

        public void Show(ForceUpdateRequirement requirement)
        {
            if (requirement == null)
            {
                Debug.LogError("[ForceUpdatePopup] Requirement is null.");
                return;
            }

            EnsureUiBuilt();
            if (_updateButton == null)
            {
                Debug.LogError("[ForceUpdatePopup] Update button is not configured.");
                return;
            }

            EnsureEventSystemExists();

            _currentRequirement = requirement;
            ApplyRequirement(requirement);

            var visibilityRoot = GetVisibilityRoot();
            _skipHideOnAwake = true;
            if (visibilityRoot != gameObject)
            {
                gameObject.SetActive(true);
            }

            visibilityRoot.SetActive(true);
            _skipHideOnAwake = false;
            PlayShowAnimation();
        }

        public static ForceUpdatePopup CreateInstance()
        {
            var root = new GameObject(
                "ForceUpdatePopup",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(ForceUpdatePopup));

            var popup = root.GetComponent<ForceUpdatePopup>();
            popup.InitializeCanvas();
            root.SetActive(false);
            return popup;
        }

        private void Awake()
        {
            if (HasMinimumCustomView())
            {
                BindCustomView();
            }
            else if (!HasAnyCustomViewReference())
            {
                InitializeCanvas();
            }

            if (_hideOnAwake && !_skipHideOnAwake)
            {
                HideImmediate();
            }
        }

        private void InitializeCanvas()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
            }

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10000;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void EnsureUiBuilt()
        {
            if (_viewInitialized)
            {
                return;
            }

            if (HasMinimumCustomView())
            {
                BindCustomView();
                _viewInitialized = true;
                return;
            }

            if (HasAnyCustomViewReference())
            {
                Debug.LogError("[ForceUpdatePopup] Custom popup references are incomplete. Assign title, message, button and button label.");
                return;
            }

            if (!_buildRuntimeUiIfMissing)
            {
                Debug.LogError("[ForceUpdatePopup] No popup view is assigned and runtime fallback is disabled.");
                return;
            }

            InitializeCanvas();
            BuildRuntimeUi();
            _viewInitialized = true;
        }

        private void BindCustomView()
        {
            _visibilityRoot ??= gameObject;
            _overlayObject ??= _visibilityRoot;
            _dialogRoot ??= GetComponent<RectTransform>();
            _overlayCanvasGroup ??= _overlayObject != null ? _overlayObject.GetComponent<CanvasGroup>() : null;
            _dialogCanvasGroup ??= _dialogRoot != null ? _dialogRoot.GetComponent<CanvasGroup>() : null;
            _updateButtonBackground ??= _updateButton != null
                ? _updateButton.targetGraphic as Image ?? _updateButton.GetComponent<Image>()
                : null;

            _updateButton.onClick.RemoveListener(OnUpdateClicked);
            _updateButton.onClick.AddListener(OnUpdateClicked);
        }

        private void BuildRuntimeUi()
        {
            var titleFont = LoadFont();
            var bodyFont = LoadFont();
            var panelSprite = LoadPanelSprite();

            _visibilityRoot = gameObject;

            _overlayObject = CreateUiObject("Overlay", transform);
            var overlayRect = _overlayObject.GetComponent<RectTransform>();
            StretchFullScreen(overlayRect);

            var overlayImage = _overlayObject.AddComponent<Image>();
            ApplyPanelStyle(overlayImage, _overlayColor, null);
            _overlayCanvasGroup = _overlayObject.AddComponent<CanvasGroup>();

            var shadowRect = CreateUiObject("DialogShadow", _overlayObject.transform).GetComponent<RectTransform>();
            shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRect.pivot = new Vector2(0.5f, 0.5f);
            shadowRect.sizeDelta = new Vector2(904f, 736f);
            shadowRect.anchoredPosition = new Vector2(0f, -20f);
            var shadowImage = shadowRect.gameObject.AddComponent<Image>();
            ApplyPanelStyle(shadowImage, new Color(0f, 0f, 0f, 0.18f), panelSprite);

            _dialogRoot = CreateUiObject("Dialog", _overlayObject.transform).GetComponent<RectTransform>();
            _dialogRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _dialogRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _dialogRoot.pivot = new Vector2(0.5f, 0.5f);
            _dialogRoot.sizeDelta = new Vector2(880f, 712f);
            _dialogRoot.anchoredPosition = Vector2.zero;
            _dialogCanvasGroup = _dialogRoot.gameObject.AddComponent<CanvasGroup>();

            var dialogImage = _dialogRoot.gameObject.AddComponent<Image>();
            ApplyPanelStyle(dialogImage, _dialogColor, panelSprite);
            var dialogOutline = _dialogRoot.gameObject.AddComponent<Outline>();
            dialogOutline.effectColor = new Color(1f, 1f, 1f, 0.34f);
            dialogOutline.effectDistance = new Vector2(2f, -2f);

            var headerRect = CreateUiObject("Header", _dialogRoot).GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 178f);
            headerRect.anchoredPosition = Vector2.zero;
            var headerImage = headerRect.gameObject.AddComponent<Image>();
            ApplyPanelStyle(headerImage, _headerColor, panelSprite);

            var accentRect = CreateUiObject("HeaderAccent", headerRect).GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(1f, 0f);
            accentRect.pivot = new Vector2(0.5f, 0f);
            accentRect.sizeDelta = new Vector2(0f, 18f);
            accentRect.anchoredPosition = Vector2.zero;
            var accentImage = accentRect.gameObject.AddComponent<Image>();
            ApplyPanelStyle(accentImage, _headerAccentColor, null);

            var popupTitleRect = CreateText(
                "PopupTitle",
                headerRect,
                titleFont,
                42f,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center,
                out _popupTitleText);
            popupTitleRect.anchorMin = new Vector2(0f, 0f);
            popupTitleRect.anchorMax = new Vector2(1f, 1f);
            popupTitleRect.pivot = new Vector2(0.5f, 0.5f);
            popupTitleRect.offsetMin = new Vector2(120f, 20f);
            popupTitleRect.offsetMax = new Vector2(-120f, -20f);

            var badgeRect = CreateUiObject("Badge", _dialogRoot).GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0.5f, 1f);
            badgeRect.anchorMax = new Vector2(0.5f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(128f, 128f);
            badgeRect.anchoredPosition = new Vector2(0f, -72f);
            var badgeImage = badgeRect.gameObject.AddComponent<Image>();
            ApplyPanelStyle(badgeImage, _headerAccentColor, panelSprite);

            var badgeInsetRect = CreateUiObject("BadgeInset", badgeRect).GetComponent<RectTransform>();
            StretchWithPadding(badgeInsetRect, 10f);
            var badgeInsetImage = badgeInsetRect.gameObject.AddComponent<Image>();
            ApplyPanelStyle(badgeInsetImage, _dialogColor, panelSprite);

            var badgeTextRect = CreateText(
                "BadgeText",
                badgeInsetRect,
                titleFont,
                70f,
                FontStyles.Bold,
                _headerAccentColor,
                TextAlignmentOptions.Center,
                out var badgeText);
            StretchFullScreen(badgeTextRect);
            badgeText.text = "!";

            var titleRect = CreateText(
                "Title",
                _dialogRoot,
                titleFont,
                54f,
                FontStyles.Bold,
                _titleColor,
                TextAlignmentOptions.Center,
                out _titleText);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(68f, -300f);
            titleRect.offsetMax = new Vector2(-68f, -172f);

            var messageRect = CreateText(
                "Message",
                _dialogRoot,
                bodyFont,
                30f,
                FontStyles.Normal,
                _messageColor,
                TextAlignmentOptions.Center,
                out _messageText);
            messageRect.anchorMin = new Vector2(0f, 1f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.offsetMin = new Vector2(84f, -422f);
            messageRect.offsetMax = new Vector2(-84f, -280f);

            CreateVersionChip(
                "CurrentVersionChip",
                _dialogRoot,
                panelSprite,
                bodyFont,
                new Vector2(-158f, 198f),
                _currentVersionLabel,
                out _currentVersionText);

            CreateVersionChip(
                "RequiredVersionChip",
                _dialogRoot,
                panelSprite,
                bodyFont,
                new Vector2(158f, 198f),
                _requiredVersionLabel,
                out _minimumVersionText);

            var statusRect = CreateText(
                "Status",
                _dialogRoot,
                bodyFont,
                22f,
                FontStyles.Normal,
                _statusColor,
                TextAlignmentOptions.Center,
                out _statusText);
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.offsetMin = new Vector2(80f, 150f);
            statusRect.offsetMax = new Vector2(-80f, 212f);

            var buttonObject = CreateUiObject("UpdateButton", _dialogRoot);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(430f, 112f);
            buttonRect.anchoredPosition = new Vector2(0f, 42f);

            _updateButtonBackground = buttonObject.AddComponent<Image>();
            ApplyPanelStyle(_updateButtonBackground, _buttonColor, panelSprite);
            var buttonShadow = buttonObject.AddComponent<Shadow>();
            buttonShadow.effectColor = new Color(0f, 0f, 0f, 0.18f);
            buttonShadow.effectDistance = new Vector2(0f, -8f);
            buttonShadow.useGraphicAlpha = false;

            _updateButton = buttonObject.AddComponent<Button>();
            _updateButton.transition = Selectable.Transition.ColorTint;
            _updateButton.targetGraphic = _updateButtonBackground;
            var navigation = _updateButton.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            _updateButton.navigation = navigation;
            _updateButton.colors = CreateButtonColors();
            _updateButton.onClick.AddListener(OnUpdateClicked);

            var buttonTextRect = CreateText(
                "Label",
                buttonRect,
                bodyFont,
                34f,
                FontStyles.Bold,
                _buttonTextColor,
                TextAlignmentOptions.Center,
                out _updateButtonText);
            StretchFullScreen(buttonTextRect);
        }

        private void ApplyRequirement(ForceUpdateRequirement requirement)
        {
            if (_popupTitleText != null)
            {
                _popupTitleText.text = _popupTitleLabel;
            }

            if (_titleText != null)
            {
                _titleText.text = string.IsNullOrWhiteSpace(requirement.Title) ? _defaultTitle : requirement.Title;
            }

            if (_messageText != null)
            {
                _messageText.text = string.IsNullOrWhiteSpace(requirement.Message) ? _defaultMessage : requirement.Message;
            }

            if (_updateButtonText != null)
            {
                _updateButtonText.text = _updateButtonLabel;
            }

            var currentVersion = string.IsNullOrWhiteSpace(requirement.CurrentVersion)
                ? Application.version
                : requirement.CurrentVersion;
            var minimumVersion = string.IsNullOrWhiteSpace(requirement.MinimumSupportedVersion)
                ? "Unknown"
                : requirement.MinimumSupportedVersion;

            if (_currentVersionText != null)
            {
                _currentVersionText.text = currentVersion;
            }

            if (_minimumVersionText != null)
            {
                _minimumVersionText.text = minimumVersion;
            }

            var resolvedStoreUrl = requirement.ResolveStoreUrl();
            var hasStoreUrl = !string.IsNullOrWhiteSpace(resolvedStoreUrl);
            _updateButton.interactable = hasStoreUrl;

            if (_statusText != null)
            {
                _statusText.text = hasStoreUrl
                    ? string.Format(
                        "This build ({0}) can no longer connect. Update to {1} or newer to continue.",
                        currentVersion,
                        minimumVersion)
                    : "Store link is unavailable for this build. Check the version policy configuration.";
            }
        }

        private void OnUpdateClicked()
        {
            var url = _currentRequirement?.ResolveStoreUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogError("[ForceUpdatePopup] Store URL is empty.");
                return;
            }

            if (TryOpenStore(url))
            {
                return;
            }

            Application.OpenURL(NormalizeBrowserUrl(url));
        }

        private void HideImmediate()
        {
            if (_showAnimationCoroutine != null)
            {
                StopCoroutine(_showAnimationCoroutine);
                _showAnimationCoroutine = null;
            }

            if (_overlayCanvasGroup != null)
            {
                _overlayCanvasGroup.alpha = 1f;
            }

            if (_dialogCanvasGroup != null)
            {
                _dialogCanvasGroup.alpha = 1f;
            }

            if (_dialogRoot != null)
            {
                _dialogRoot.localScale = Vector3.one;
            }

            var visibilityRoot = GetVisibilityRoot();
            visibilityRoot.SetActive(false);
        }

        private GameObject GetVisibilityRoot()
        {
            return _visibilityRoot != null ? _visibilityRoot : gameObject;
        }

        private bool HasMinimumCustomView()
        {
            return _titleText != null &&
                   _messageText != null &&
                   _updateButton != null &&
                   _updateButtonText != null;
        }

        private bool HasAnyCustomViewReference()
        {
            return _visibilityRoot != null ||
                   _overlayObject != null ||
                   _overlayCanvasGroup != null ||
                   _dialogRoot != null ||
                   _dialogCanvasGroup != null ||
                   _popupTitleText != null ||
                   _titleText != null ||
                   _messageText != null ||
                   _currentVersionText != null ||
                   _minimumVersionText != null ||
                   _statusText != null ||
                   _updateButton != null ||
                   _updateButtonBackground != null ||
                   _updateButtonText != null;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private void CreateVersionChip(
            string name,
            Transform parent,
            Sprite panelSprite,
            TMP_FontAsset font,
            Vector2 anchoredPosition,
            string label,
            out TextMeshProUGUI valueText)
        {
            var chipRect = CreateUiObject(name, parent).GetComponent<RectTransform>();
            chipRect.anchorMin = new Vector2(0.5f, 0f);
            chipRect.anchorMax = new Vector2(0.5f, 0f);
            chipRect.pivot = new Vector2(0.5f, 0f);
            chipRect.sizeDelta = new Vector2(276f, 118f);
            chipRect.anchoredPosition = anchoredPosition;

            var chipImage = chipRect.gameObject.AddComponent<Image>();
            ApplyPanelStyle(chipImage, _chipColor, panelSprite);

            var labelRect = CreateText(
                "Label",
                chipRect,
                font,
                18f,
                FontStyles.Bold,
                _chipLabelColor,
                TextAlignmentOptions.Center,
                out var labelText);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(18f, -40f);
            labelRect.offsetMax = new Vector2(-18f, -8f);
            labelText.text = label;

            var valueRect = CreateText(
                "Value",
                chipRect,
                font,
                34f,
                FontStyles.Bold,
                _chipValueColor,
                TextAlignmentOptions.Center,
                out valueText);
            valueRect.anchorMin = new Vector2(0f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.offsetMin = new Vector2(18f, 12f);
            valueRect.offsetMax = new Vector2(-18f, -30f);
        }

        private static RectTransform CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            float fontSize,
            FontStyles fontStyle,
            Color color,
            TextAlignmentOptions alignment,
            out TextMeshProUGUI text)
        {
            var go = CreateUiObject(name, parent);
            text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        private static void StretchFullScreen(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void StretchWithPadding(RectTransform rectTransform, float padding)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(padding, padding);
            rectTransform.offsetMax = new Vector2(-padding, -padding);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        private static TMP_FontAsset LoadFont()
        {
            var font = TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                return font;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        private static Sprite LoadPanelSprite()
        {
            var sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite != null)
            {
                return sprite;
            }

            return Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd");
        }

        private static void ApplyPanelStyle(Image image, Color color, Sprite sprite)
        {
            image.color = color;
            image.sprite = sprite;
            if (sprite != null)
            {
                image.type = Image.Type.Sliced;
            }
        }

        private ColorBlock CreateButtonColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = _buttonColor;
            colors.highlightedColor = Color.Lerp(_buttonColor, Color.white, 0.08f);
            colors.pressedColor = _buttonPressedColor;
            colors.selectedColor = _buttonColor;
            colors.disabledColor = _buttonDisabledColor;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        private void PlayShowAnimation()
        {
            if (_overlayCanvasGroup == null || _dialogCanvasGroup == null || _dialogRoot == null)
            {
                return;
            }

            if (_showAnimationCoroutine != null)
            {
                StopCoroutine(_showAnimationCoroutine);
            }

            _showAnimationCoroutine = StartCoroutine(AnimateShow());
        }

        private IEnumerator AnimateShow()
        {
            _overlayCanvasGroup.alpha = 0f;
            _dialogCanvasGroup.alpha = 0f;
            _dialogRoot.localScale = new Vector3(0.94f, 0.94f, 1f);

            var elapsed = 0f;
            while (elapsed < ShowAnimationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / ShowAnimationDuration);
                var eased = Mathf.SmoothStep(0f, 1f, t);

                _overlayCanvasGroup.alpha = eased;
                _dialogCanvasGroup.alpha = eased;
                _dialogRoot.localScale = Vector3.LerpUnclamped(
                    new Vector3(0.94f, 0.94f, 1f),
                    Vector3.one,
                    eased);

                yield return null;
            }

            _overlayCanvasGroup.alpha = 1f;
            _dialogCanvasGroup.alpha = 1f;
            _dialogRoot.localScale = Vector3.one;
            _showAnimationCoroutine = null;
        }

        private static void EnsureEventSystemExists()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            }

            GameObject eventSystemObject;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            }
            else
            {
                eventSystemObject = eventSystem.gameObject;
            }

#if ENABLE_INPUT_SYSTEM
            var inputSystemModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            if (inputSystemModule == null)
            {
                inputSystemModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            }

            if (inputSystemModule.actionsAsset == null)
            {
                inputSystemModule.AssignDefaultActions();
            }

            var legacyModule = eventSystemObject.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                legacyModule.enabled = false;
            }
#else
            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private static bool TryOpenStore(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url);

                intent.Call<AndroidJavaObject>("setData", uri);
                intent.Call<AndroidJavaObject>("addFlags", 0x10000000);

                if (url.StartsWith("market://", StringComparison.OrdinalIgnoreCase))
                {
                    intent.Call<AndroidJavaObject>("setPackage", "com.android.vending");
                }

                activity.Call("startActivity", intent);
                return true;
            }
            catch
            {
                // Browser fallback below will handle the open.
            }
#endif
            return false;
        }

        private static string NormalizeBrowserUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            if (url.StartsWith(MarketDetailsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return "https://play.google.com/store/apps/details?" + url.Substring(MarketDetailsPrefix.Length);
            }

            if (url.StartsWith("itms-apps://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + url.Substring("itms-apps://".Length);
            }

            if (url.StartsWith("itms://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + url.Substring("itms://".Length);
            }

            return url;
        }
    }
}
