using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Serhat.Forge.UI.Components
{
    public enum BackdropMode
    {
        Blur,
        SolidColor
    }

    /// <summary>
    /// Backdrop overlay behind popups. Two modes:
    /// - Blur: captures the screen and applies gaussian blur + tint.
    /// - SolidColor: simple semi-transparent color overlay (no capture).
    /// Automatically activates on SetActive(true), cleans up on SetActive(false).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class BlurBackdrop : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] private BackdropMode _mode = BackdropMode.Blur;

        [Header("Blur Settings")]
        [SerializeField] private Shader _blurShader;
        [SerializeField, Range(1, 8)] private int _downSample = 4;
        [SerializeField, Range(1, 4)] private int _blurIterations = 2;
        [SerializeField, Range(0f, 5f)] private float _blurSize = 2f;

        [Header("Tint (used in both modes)")]
        [SerializeField] private Color _tintColor = new Color(0f, 0f, 0f, 0.4f);

        [Header("Solid Color (only for SolidColor mode)")]
        [SerializeField] private Color _solidColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("Sorting")]
        [SerializeField] private int _sortingOrder = 100;

        [Header("Animation")]
        [SerializeField] private float _fadeDuration = 0.25f;

        private RawImage _rawImage;
        private CanvasGroup _canvasGroup;
        private Canvas _overrideCanvas;
        private Material _blurMaterial;
        private RenderTexture _blurredTexture;
        private float _fadeTarget;
        private float _fadeVelocity;
        private bool _initialized;

        private RenderTexture _rtPing;
        private RenderTexture _rtPong;
        private int _cachedWidth;
        private int _cachedHeight;

        // Solid color mode uses a 1x1 texture
        private Texture2D _solidTexture;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            _rawImage = GetComponent<RawImage>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _overrideCanvas = GetComponent<Canvas>();

            _overrideCanvas.overrideSorting = true;
            _overrideCanvas.sortingOrder = _sortingOrder;

            if (_blurShader == null)
                _blurShader = Shader.Find("UI/BlurBackdrop");

            _blurMaterial = new Material(_blurShader);
            _initialized = true;
        }

        private void OnEnable()
        {
            Initialize();

            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _rawImage.enabled = false;

            if (_mode == BackdropMode.Blur)
            {
                StartCoroutine(CaptureNextFrame());
            }
            else
            {
                ShowSolidColor();
            }
        }

        private void OnDisable()
        {
            if (!_initialized) return;

            StopAllCoroutines();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _rawImage.enabled = false;
            ReleaseBlurredTexture();
        }

        private void OnDestroy()
        {
            ReleaseBlurredTexture();
            ReleasePingPong();

            if (_blurMaterial != null)
                Destroy(_blurMaterial);

            if (_solidTexture != null)
                Destroy(_solidTexture);
        }

        private void Update()
        {
            if (Mathf.Abs(_canvasGroup.alpha - _fadeTarget) > 0.01f)
            {
                _canvasGroup.alpha = Mathf.SmoothDamp(
                    _canvasGroup.alpha, _fadeTarget, ref _fadeVelocity, _fadeDuration, Mathf.Infinity, Time.unscaledDeltaTime);
            }
            else if (_canvasGroup.alpha != _fadeTarget)
            {
                _canvasGroup.alpha = _fadeTarget;
            }
        }

        #region Solid Color Mode

        private void ShowSolidColor()
        {
            if (_solidTexture == null)
            {
                _solidTexture = new Texture2D(1, 1);
            }

            _solidTexture.SetPixel(0, 0, _solidColor);
            _solidTexture.Apply();

            _rawImage.texture = _solidTexture;
            _rawImage.material = null;
            _rawImage.enabled = true;

            _fadeTarget = 1f;
            _fadeVelocity = 0f;
        }

        #endregion

        #region Blur Mode

        private IEnumerator CaptureNextFrame()
        {
            yield return new WaitForEndOfFrame();

            CaptureAndBlur();

            _rawImage.enabled = true;
            _fadeTarget = 1f;
            _fadeVelocity = 0f;
        }

        private void CaptureAndBlur()
        {
            int screenW = Screen.width;
            int screenH = Screen.height;
            int w = screenW / _downSample;
            int h = screenH / _downSample;

            Texture2D screenTex = new Texture2D(screenW, screenH, TextureFormat.RGB24, false);
            screenTex.ReadPixels(new Rect(0, 0, screenW, screenH), 0, 0, false);
            screenTex.Apply(false);

            EnsurePingPong(w, h);

            Graphics.Blit(screenTex, _rtPing);
            Destroy(screenTex);

            _blurMaterial.SetFloat("_BlurSize", _blurSize);

            RenderTexture src = _rtPing;
            RenderTexture dst = _rtPong;

            for (int i = 0; i < _blurIterations; i++)
            {
                Graphics.Blit(src, dst, _blurMaterial, 0);
                SwapRT(ref src, ref dst);

                Graphics.Blit(src, dst, _blurMaterial, 1);
                SwapRT(ref src, ref dst);
            }

            _blurMaterial.SetColor("_TintColor", _tintColor);
            ReleaseBlurredTexture();
            _blurredTexture = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, _blurredTexture, _blurMaterial, 2);

            _rawImage.texture = _blurredTexture;
            _rawImage.material = null;
        }

        #endregion

        #region Texture Management

        private void EnsurePingPong(int w, int h)
        {
            if (_rtPing != null && _cachedWidth == w && _cachedHeight == h)
                return;

            ReleasePingPong();

            _rtPing = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear };
            _rtPong = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear };
            _cachedWidth = w;
            _cachedHeight = h;
        }

        private void SwapRT(ref RenderTexture a, ref RenderTexture b)
        {
            (a, b) = (b, a);
        }

        private void ReleaseBlurredTexture()
        {
            if (_blurredTexture != null)
            {
                RenderTexture.ReleaseTemporary(_blurredTexture);
                _blurredTexture = null;
            }

            if (_rawImage != null)
                _rawImage.texture = null;
        }

        private void ReleasePingPong()
        {
            if (_rtPing != null)
            {
                _rtPing.Release();
                Destroy(_rtPing);
                _rtPing = null;
            }

            if (_rtPong != null)
            {
                _rtPong.Release();
                Destroy(_rtPong);
                _rtPong = null;
            }

            _cachedWidth = 0;
            _cachedHeight = 0;
        }

        #endregion
    }
}
