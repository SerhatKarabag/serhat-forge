using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Serhat.Forge
{
    /// <summary>
    /// Runtime controller for generating high-quality thumbnails using actual URP rendering.
    /// Place prefabs in the scene, adjust lighting/camera as needed, then capture.
    /// </summary>
    public class RuntimeThumbnailController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _thumbnailCamera;
        [SerializeField] private Transform _spawnPoint;

        [Header("Prefab Settings")]
        [SerializeField] private List<GameObject> _prefabsToCapture = new List<GameObject>();
        [SerializeField] private int _currentPrefabIndex = 0;

        [Header("Output Settings")]
        [SerializeField] private string _outputFolder = "Assets/Resources/ItemIcons";
        [SerializeField] private int _iconSize = 256;
        [SerializeField] private int _renderScale = 2;  // Render at higher resolution then downscale (1=1x, 2=2x, 4=4x)

        [Header("Object Transform")]
        [SerializeField] private Vector3 _objectRotation = Vector3.zero;
        [SerializeField] private float _objectScale = 1f;
        [SerializeField] private Vector3 _objectOffset = Vector3.zero;

        [Header("Camera Settings")]
        [SerializeField] private float _cameraDistance = 5f;
        [SerializeField] private float _cameraYaw = 35f;
        [SerializeField] private float _cameraPitch = 25f;
        [SerializeField] private float _cameraZoom = 1f;
        [SerializeField] private float _fieldOfView = 30f;  // Lower = flatter/orthographic look, Higher = more perspective

        [Header("Post Processing")]
        [SerializeField] private bool _transparentBackground = true;
        [SerializeField] private Color _backgroundColor = new Color(1f, 0f, 1f, 1f);
        [SerializeField] private bool _addOutline = true;
        [SerializeField] private Color _outlineColor = Color.white;
        [SerializeField] private int _outlineWidth = 6;  // Thicker outline for visibility

        [Header("Runtime State")]
        [SerializeField] private GameObject _currentInstance;

        // UI State
        private bool _showUI = true;
        private Vector2 _scrollPosition;
        private Texture2D _lastCapture;
        private string _statusMessage = "";
        private bool _isBatchCapturing = false;
        private int _batchProgress = 0;
        private int _batchTotal = 0;

        // Canvas preview
        private Texture2D _dashedLineTexture;
        private bool _showCanvasPreview = true;
        private RenderTexture _previewRenderTexture;
        private int _lastPreviewSize = 0;

        // UI Styles
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _selectedButtonStyle;
        private GUIStyle _captureButtonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _statusStyle;
        private bool _stylesInitialized = false;
        private int _selectedTab = 0;  // 0=Transform, 1=Camera, 2=Output

        private void Start()
        {
            // Camera must be assigned in Inspector or use Camera.main
            if (_thumbnailCamera == null)
            {
                _thumbnailCamera = Camera.main;
                if (_thumbnailCamera == null)
                {
                    Debug.LogError("[RuntimeThumbnailController] No camera assigned and Camera.main is null. Please assign a camera in the Inspector.");
                    enabled = false;
                    return;
                }
            }

            // Spawn point must be assigned in Inspector or use this transform
            if (_spawnPoint == null)
            {
                _spawnPoint = transform;
                Debug.LogWarning("[RuntimeThumbnailController] No spawn point assigned, using this transform.");
            }

            // Set camera background
            if (_thumbnailCamera != null)
            {
                _thumbnailCamera.backgroundColor = _backgroundColor;
            }

            // Create dashed line texture for canvas preview
            CreateDashedLineTexture();
        }

        private void CreateDashedLineTexture()
        {
            _dashedLineTexture = new Texture2D(16, 1);
            Color[] pixels = new Color[16];
            for (int i = 0; i < 16; i++)
            {
                // Create dash pattern: 8 pixels on, 8 pixels off
                pixels[i] = (i < 8) ? Color.black : Color.clear;
            }
            _dashedLineTexture.SetPixels(pixels);
            _dashedLineTexture.wrapMode = TextureWrapMode.Repeat;
            _dashedLineTexture.filterMode = FilterMode.Point;
            _dashedLineTexture.Apply();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Toggle UI with Tab
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                _showUI = !_showUI;
            }

            // Quick capture with Space
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                CaptureCurrentPrefab();
            }

            // Navigate prefabs with arrow keys
            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                PreviousPrefab();
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                NextPrefab();
            }

            // Rotate object with Q/E
            if (keyboard.qKey.isPressed)
            {
                _objectRotation.y -= 90f * Time.deltaTime;
                UpdateCurrentInstance();
            }
            if (keyboard.eKey.isPressed)
            {
                _objectRotation.y += 90f * Time.deltaTime;
                UpdateCurrentInstance();
            }

            // Update camera position
            UpdateCameraPosition();

            // Update preview render texture for accurate 1:1 preview
            UpdatePreviewRenderTexture();
        }

        private void UpdatePreviewRenderTexture()
        {
            if (_thumbnailCamera == null) return;

            // Calculate preview size (square, fitting in screen)
            int previewSize = Mathf.Min(Screen.width, Screen.height);

            // Recreate render texture if size changed
            if (_previewRenderTexture == null || _lastPreviewSize != previewSize)
            {
                if (_previewRenderTexture != null)
                {
                    _thumbnailCamera.targetTexture = null;
                    _previewRenderTexture.Release();
                    Destroy(_previewRenderTexture);
                }

                _previewRenderTexture = new RenderTexture(previewSize, previewSize, 24, RenderTextureFormat.ARGB32);
                _previewRenderTexture.antiAliasing = 4;
                _lastPreviewSize = previewSize;
            }

            // Set camera to render to square texture for accurate preview
            _thumbnailCamera.targetTexture = _previewRenderTexture;
        }

        private void UpdateCameraPosition()
        {
            if (_thumbnailCamera == null || _spawnPoint == null) return;

            float yawRad = _cameraYaw * Mathf.Deg2Rad;
            float pitchRad = _cameraPitch * Mathf.Deg2Rad;

            Vector3 direction = new Vector3(
                Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                Mathf.Sin(pitchRad),
                -Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
            );

            Vector3 targetPos = _spawnPoint.position + _objectOffset;
            float distance = _cameraDistance;

            if (_currentInstance != null)
            {
                var bounds = CalculateBounds(_currentInstance);
                targetPos = bounds.center + _objectOffset;

                // Calculate distance to fit object in view (for perspective camera)
                float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                float fov = _thumbnailCamera.fieldOfView * Mathf.Deg2Rad;
                // Distance needed to fit object + padding
                distance = (maxDim * 1.2f) / (2f * Mathf.Tan(fov / 2f));
                distance /= _cameraZoom;
            }

            // Use perspective camera for natural 3D look
            _thumbnailCamera.orthographic = false;
            _thumbnailCamera.fieldOfView = _fieldOfView;
            _thumbnailCamera.transform.position = targetPos + direction * distance;
            _thumbnailCamera.transform.LookAt(targetPos);
            _thumbnailCamera.backgroundColor = _backgroundColor;
        }

        private Bounds CalculateBounds(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(obj.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private void SpawnPrefab(GameObject prefab)
        {
            // Destroy current instance
            if (_currentInstance != null)
            {
                Destroy(_currentInstance);
            }

            if (prefab == null) return;

            // Spawn new instance
            _currentInstance = Instantiate(prefab, _spawnPoint.position, Quaternion.identity);
            _currentInstance.name = $"[PREVIEW] {prefab.name}";

            // Disable scripts
            foreach (var mb in _currentInstance.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mb != null && mb != this)
                {
                    mb.enabled = false;
                }
            }

            // Disable colliders
            foreach (var col in _currentInstance.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            // Disable rigidbodies
            foreach (var rb in _currentInstance.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
            }

            UpdateCurrentInstance();
            _statusMessage = $"Loaded: {prefab.name}";
        }

        private void UpdateCurrentInstance()
        {
            if (_currentInstance == null) return;

            _currentInstance.transform.position = _spawnPoint.position + _objectOffset;
            _currentInstance.transform.rotation = Quaternion.Euler(_objectRotation);
            _currentInstance.transform.localScale = Vector3.one * _objectScale;
        }

        private void NextPrefab()
        {
            if (_prefabsToCapture.Count == 0) return;
            _currentPrefabIndex = (_currentPrefabIndex + 1) % _prefabsToCapture.Count;
            SpawnPrefab(_prefabsToCapture[_currentPrefabIndex]);
        }

        private void PreviousPrefab()
        {
            if (_prefabsToCapture.Count == 0) return;
            _currentPrefabIndex = (_currentPrefabIndex - 1 + _prefabsToCapture.Count) % _prefabsToCapture.Count;
            SpawnPrefab(_prefabsToCapture[_currentPrefabIndex]);
        }

        private void CaptureCurrentPrefab()
        {
            if (_thumbnailCamera == null)
            {
                _statusMessage = "Error: No camera assigned!";
                return;
            }

            if (_currentInstance == null)
            {
                _statusMessage = "Error: No prefab loaded!";
                return;
            }

            // Get the prefab name
            string prefabName = _currentInstance.name.Replace("[PREVIEW] ", "");

            // Capture
            var texture = CaptureScreenshot();

            if (texture == null)
            {
                _statusMessage = "Error: Capture failed!";
                return;
            }

            // Process texture - outline only (transparency is handled by camera)
            if (_addOutline)
            {
                texture = AddOutline(texture, _outlineColor, _outlineWidth);
            }

            // Save
            SaveTexture(texture, prefabName);

            // Store for preview
            if (_lastCapture != null)
            {
                Destroy(_lastCapture);
            }
            _lastCapture = texture;

            _statusMessage = $"Saved: {prefabName}_icon.png";
        }

        private Texture2D CaptureScreenshot()
        {
            // Render at higher resolution for better quality
            int renderSize = _iconSize * _renderScale;

            // Store original camera settings
            RenderTexture prevTarget = _thumbnailCamera.targetTexture;
            Color prevBgColor = _thumbnailCamera.backgroundColor;
            CameraClearFlags prevClearFlags = _thumbnailCamera.clearFlags;

            Texture2D result;

            if (_transparentBackground)
            {
                // Two-pass render for proper alpha extraction
                result = CaptureWithTransparency(renderSize);
            }
            else
            {
                // Simple render with solid background
                RenderTexture rt = new RenderTexture(renderSize, renderSize, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 8;

                _thumbnailCamera.targetTexture = rt;
                _thumbnailCamera.clearFlags = CameraClearFlags.SolidColor;
                _thumbnailCamera.backgroundColor = _backgroundColor;
                _thumbnailCamera.Render();

                RenderTexture.active = rt;
                result = new Texture2D(renderSize, renderSize, TextureFormat.ARGB32, false);
                result.ReadPixels(new Rect(0, 0, renderSize, renderSize), 0, 0);
                result.Apply();
                RenderTexture.active = null;
                Destroy(rt);
            }

            // Restore camera settings
            _thumbnailCamera.targetTexture = prevTarget;
            _thumbnailCamera.backgroundColor = prevBgColor;
            _thumbnailCamera.clearFlags = prevClearFlags;

            // Downscale if needed
            if (_renderScale > 1)
            {
                Texture2D downscaled = DownscaleTexture(result, _iconSize);
                Destroy(result);
                return downscaled;
            }

            return result;
        }

        private Texture2D CaptureWithTransparency(int size)
        {
            // Technique: Render twice with different backgrounds, compare to extract alpha
            // This works with any shader, even those that don't write alpha

            // Use highest quality render texture settings
            RenderTextureDescriptor desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 24);
            desc.msaaSamples = 8;  // Maximum MSAA
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.sRGB = true;  // Proper color space

            RenderTexture rt = new RenderTexture(desc);

            _thumbnailCamera.targetTexture = rt;
            _thumbnailCamera.clearFlags = CameraClearFlags.SolidColor;

            // First pass: White background
            _thumbnailCamera.backgroundColor = Color.white;
            _thumbnailCamera.Render();

            RenderTexture.active = rt;
            Texture2D whitePass = new Texture2D(size, size, TextureFormat.ARGB32, false);
            whitePass.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            whitePass.Apply();

            // Second pass: Black background
            _thumbnailCamera.backgroundColor = Color.black;
            _thumbnailCamera.Render();

            Texture2D blackPass = new Texture2D(size, size, TextureFormat.ARGB32, false);
            blackPass.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            blackPass.Apply();

            RenderTexture.active = null;
            Destroy(rt);

            // Extract alpha by comparing the two renders
            // Where they differ = background, where they're same = object
            Color[] whitePixels = whitePass.GetPixels();
            Color[] blackPixels = blackPass.GetPixels();
            Color[] resultPixels = new Color[whitePixels.Length];

            for (int i = 0; i < whitePixels.Length; i++)
            {
                Color w = whitePixels[i];
                Color b = blackPixels[i];

                // Calculate alpha from difference
                // If pixel is opaque object: white and black renders are identical
                // If pixel is background: white render is white, black render is black
                float diffR = w.r - b.r;
                float diffG = w.g - b.g;
                float diffB = w.b - b.b;
                float maxDiff = Mathf.Max(diffR, Mathf.Max(diffG, diffB));

                // Alpha = 1 - maxDiff (where diff is 0, alpha is 1; where diff is 1, alpha is 0)
                float alpha = 1f - maxDiff;

                if (alpha < 0.01f)
                {
                    // Fully transparent
                    resultPixels[i] = new Color(0, 0, 0, 0);
                }
                else
                {
                    // Recover original color (use black pass, it has less contamination)
                    // For semi-transparent pixels, we need to un-premultiply
                    float r = alpha > 0.001f ? b.r / alpha : 0;
                    float g = alpha > 0.001f ? b.g / alpha : 0;
                    float b_val = alpha > 0.001f ? b.b / alpha : 0;

                    resultPixels[i] = new Color(
                        Mathf.Clamp01(r),
                        Mathf.Clamp01(g),
                        Mathf.Clamp01(b_val),
                        alpha
                    );
                }
            }

            Destroy(whitePass);
            Destroy(blackPass);

            Texture2D result = new Texture2D(size, size, TextureFormat.ARGB32, false);
            result.SetPixels(resultPixels);
            result.Apply();

            return result;
        }

        private Texture2D DownscaleTexture(Texture2D source, int targetSize)
        {
            // High quality downscale using trilinear filtering
            source.filterMode = FilterMode.Trilinear;
            source.anisoLevel = 16;

            RenderTexture rt = RenderTexture.GetTemporary(targetSize, targetSize, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Trilinear;

            // Use point filtering on destination for sharp pixels
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(targetSize, targetSize, TextureFormat.ARGB32, false);
            result.filterMode = FilterMode.Bilinear;
            result.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
            result.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        private Texture2D AddOutline(Texture2D source, Color outlineColor, int width)
        {
            int w = source.width;
            int h = source.height;
            var sourcePixels = source.GetPixels();
            var resultPixels = new Color[sourcePixels.Length];

            System.Array.Copy(sourcePixels, resultPixels, sourcePixels.Length);

            int r = Mathf.Clamp(width, 1, 16);
            float alphaThreshold = 0.05f;
            int radiusSquared = r * r;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (sourcePixels[idx].a > alphaThreshold)
                        continue;

                    bool hit = false;
                    for (int dy = -r; dy <= r && !hit; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy > radiusSquared)
                                continue;

                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                                continue;

                            int nidx = ny * w + nx;
                            if (sourcePixels[nidx].a > alphaThreshold)
                            {
                                hit = true;
                                break;
                            }
                        }
                    }

                    if (hit)
                    {
                        resultPixels[idx] = outlineColor;
                    }
                }
            }

            var result = new Texture2D(w, h, TextureFormat.ARGB32, false);
            result.SetPixels(resultPixels);
            result.Apply();

            Destroy(source);
            return result;
        }

        private void SaveTexture(Texture2D texture, string prefabName)
        {
            // Create output folder if needed
            if (!Directory.Exists(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
            }

            // Save PNG
            byte[] pngData = texture.EncodeToPNG();
            string filePath = Path.Combine(_outputFolder, $"{prefabName}_icon.png");
            File.WriteAllBytes(filePath, pngData);

            Debug.Log($"Thumbnail saved: {filePath}");

#if UNITY_EDITOR
            // During batch capture, skip per-file refresh (done once at the end)
            if (!_isBatchCapturing)
            {
                UnityEditor.AssetDatabase.Refresh();

                var importer = UnityEditor.AssetImporter.GetAtPath(filePath) as UnityEditor.TextureImporter;
                if (importer != null)
                {
                    importer.textureType = UnityEditor.TextureImporterType.Sprite;
                    importer.spriteImportMode = UnityEditor.SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
#endif
        }

        private void CaptureAllPrefabs()
        {
            if (_isBatchCapturing) return;
            if (_prefabsToCapture.Count == 0)
            {
                _statusMessage = "No prefabs in list!";
                return;
            }

            StartCoroutine(CaptureAllCoroutine());
        }

        private IEnumerator CaptureAllCoroutine()
        {
            _isBatchCapturing = true;
            _batchTotal = _prefabsToCapture.Count;
            _batchProgress = 0;
            int captured = 0;

            for (int i = 0; i < _prefabsToCapture.Count; i++)
            {
                if (_prefabsToCapture[i] == null)
                {
                    _batchProgress = i + 1;
                    continue;
                }

                _currentPrefabIndex = i;
                SpawnPrefab(_prefabsToCapture[i]);

                // Wait 2 frames so the renderer draws the new object
                yield return null;
                yield return null;

                CaptureCurrentPrefab();
                captured++;
                _batchProgress = i + 1;
                _statusMessage = $"Capturing… {_batchProgress}/{_batchTotal}";
            }

#if UNITY_EDITOR
            // Single AssetDatabase refresh at the end instead of per-file
            UnityEditor.AssetDatabase.Refresh();
#endif

            _isBatchCapturing = false;
            _statusMessage = $"Done! Captured {captured}/{_batchTotal} thumbnails.";
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            // Header style
            _headerStyle = new GUIStyle(GUI.skin.box);
            _headerStyle.fontSize = 16;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.alignment = TextAnchor.MiddleCenter;
            _headerStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            _headerStyle.padding = new RectOffset(10, 10, 8, 8);

            // Section header style
            _sectionStyle = new GUIStyle(GUI.skin.label);
            _sectionStyle.fontSize = 12;
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);
            _sectionStyle.margin = new RectOffset(0, 0, 8, 4);

            // Button style
            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = 11;
            _buttonStyle.padding = new RectOffset(8, 8, 4, 4);

            // Selected button style
            _selectedButtonStyle = new GUIStyle(GUI.skin.button);
            _selectedButtonStyle.fontSize = 11;
            _selectedButtonStyle.fontStyle = FontStyle.Bold;
            _selectedButtonStyle.normal.textColor = Color.white;
            _selectedButtonStyle.normal.background = MakeColorTexture(new Color(0.3f, 0.5f, 0.8f));
            _selectedButtonStyle.padding = new RectOffset(8, 8, 4, 4);

            // Capture button style
            _captureButtonStyle = new GUIStyle(GUI.skin.button);
            _captureButtonStyle.fontSize = 14;
            _captureButtonStyle.fontStyle = FontStyle.Bold;
            _captureButtonStyle.normal.textColor = Color.white;
            _captureButtonStyle.normal.background = MakeColorTexture(new Color(0.2f, 0.6f, 0.3f));
            _captureButtonStyle.hover.background = MakeColorTexture(new Color(0.25f, 0.7f, 0.35f));
            _captureButtonStyle.padding = new RectOffset(10, 10, 10, 10);

            // Label style
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 11;
            _labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

            // Value style
            _valueStyle = new GUIStyle(GUI.skin.label);
            _valueStyle.fontSize = 11;
            _valueStyle.fontStyle = FontStyle.Bold;
            _valueStyle.normal.textColor = new Color(1f, 0.9f, 0.5f);
            _valueStyle.alignment = TextAnchor.MiddleRight;

            // Status style
            _statusStyle = new GUIStyle(GUI.skin.box);
            _statusStyle.fontSize = 11;
            _statusStyle.normal.textColor = new Color(0.5f, 1f, 0.5f);
            _statusStyle.alignment = TextAnchor.MiddleCenter;

            _stylesInitialized = true;
        }

        private Texture2D MakeColorTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            // Always draw canvas preview (even if UI is hidden)
            if (_showCanvasPreview)
            {
                DrawCanvasPreview();
            }

            if (!_showUI) return;

            InitStyles();

            float panelWidth = 280;
            float panelHeight = Mathf.Min(620, Screen.height - 40);

            // Main panel with rounded look
            GUI.Box(new Rect(15, 15, panelWidth, panelHeight), "");

            GUILayout.BeginArea(new Rect(20, 20, panelWidth - 10, panelHeight - 10));

            // Header
            GUILayout.Label("THUMBNAIL GENERATOR", _headerStyle);
            GUILayout.Space(8);

            // Status message
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Label(_statusMessage, _statusStyle);
                GUILayout.Space(5);
            }

            // Prefab Navigation - Compact
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", _buttonStyle, GUILayout.Width(30))) PreviousPrefab();

            string prefabName = _currentInstance != null ? _currentInstance.name.Replace("[PREVIEW] ", "") : "None";
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_currentPrefabIndex + 1}/{_prefabsToCapture.Count}: {prefabName}", _labelStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("▶", _buttonStyle, GUILayout.Width(30))) NextPrefab();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Tab buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Transform", _selectedTab == 0 ? _selectedButtonStyle : _buttonStyle)) _selectedTab = 0;
            if (GUILayout.Button("Camera", _selectedTab == 1 ? _selectedButtonStyle : _buttonStyle)) _selectedTab = 1;
            if (GUILayout.Button("Output", _selectedTab == 2 ? _selectedButtonStyle : _buttonStyle)) _selectedTab = 2;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Tab content
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(280));

            switch (_selectedTab)
            {
                case 0: DrawTransformTab(); break;
                case 1: DrawCameraTab(); break;
                case 2: DrawOutputTab(); break;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);

            // Capture buttons
            if (GUILayout.Button("📷  CAPTURE", _captureButtonStyle, GUILayout.Height(40)))
            {
                CaptureCurrentPrefab();
            }

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUI.enabled = !_isBatchCapturing;
            if (GUILayout.Button(_isBatchCapturing ? $"Capturing… {_batchProgress}/{_batchTotal}" : "Capture All", _buttonStyle))
            {
                CaptureAllPrefabs();
            }
            GUI.enabled = true;
            _showCanvasPreview = GUILayout.Toggle(_showCanvasPreview, "Preview", _buttonStyle, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Last capture preview
            if (_lastCapture != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Box(_lastCapture, GUILayout.Width(80), GUILayout.Height(80));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Shortcuts hint
            GUILayout.FlexibleSpace();
            GUILayout.Label("Space=Capture | Tab=Hide | Q/E=Rotate", _labelStyle);

            GUILayout.EndArea();
        }

        private void DrawTransformTab()
        {
            GUILayout.Label("ROTATION", _sectionStyle);

            DrawSlider("Y", ref _objectRotation.y, -180f, 180f, "°", true);
            DrawSlider("X", ref _objectRotation.x, -180f, 180f, "°", true);
            DrawSlider("Z", ref _objectRotation.z, -180f, 180f, "°", true);

            GUILayout.Space(5);

            // Quick rotation buttons
            GUILayout.BeginHorizontal();
            GUILayout.Label("Quick:", _labelStyle, GUILayout.Width(40));
            if (GUILayout.Button("0°", _buttonStyle, GUILayout.Width(35))) { _objectRotation.y = 0; UpdateCurrentInstance(); }
            if (GUILayout.Button("45°", _buttonStyle, GUILayout.Width(35))) { _objectRotation.y = 45; UpdateCurrentInstance(); }
            if (GUILayout.Button("90°", _buttonStyle, GUILayout.Width(35))) { _objectRotation.y = 90; UpdateCurrentInstance(); }
            if (GUILayout.Button("180°", _buttonStyle, GUILayout.Width(40))) { _objectRotation.y = 180; UpdateCurrentInstance(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("POSITION", _sectionStyle);

            // X offset (left/right)
            float newOffsetX = _objectOffset.x;
            DrawSlider("X", ref newOffsetX, -0.1f, 0.1f, "", false);
            if (newOffsetX != _objectOffset.x)
            {
                _objectOffset.x = newOffsetX;
                UpdateCurrentInstance();
            }

            // Y offset (up/down)
            float newOffsetY = _objectOffset.y;
            DrawSlider("Y", ref newOffsetY, -0.1f, 0.1f, "", false);
            if (newOffsetY != _objectOffset.y)
            {
                _objectOffset.y = newOffsetY;
                UpdateCurrentInstance();
            }

            // Z offset (forward/back)
            float newOffsetZ = _objectOffset.z;
            DrawSlider("Z", ref newOffsetZ, -0.1f, 0.1f, "", false);
            if (newOffsetZ != _objectOffset.z)
            {
                _objectOffset.z = newOffsetZ;
                UpdateCurrentInstance();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(40));
            if (GUILayout.Button("Reset", _buttonStyle)) { _objectOffset = Vector3.zero; UpdateCurrentInstance(); }
            GUILayout.EndHorizontal();
        }

        private void DrawCameraTab()
        {
            GUILayout.Label("ANGLE", _sectionStyle);

            DrawSlider("Yaw", ref _cameraYaw, -180f, 180f, "°", false);
            DrawSlider("Pitch", ref _cameraPitch, -89f, 89f, "°", false);

            GUILayout.Space(10);
            GUILayout.Label("FRAMING", _sectionStyle);

            DrawSlider("Zoom", ref _cameraZoom, 0.5f, 3f, "x", false);
            DrawSlider("FOV", ref _fieldOfView, 5f, 60f, "°", false);

            GUILayout.Space(5);

            // FOV presets
            GUILayout.BeginHorizontal();
            GUILayout.Label("Style:", _labelStyle, GUILayout.Width(40));
            if (GUILayout.Button("Flat", _fieldOfView <= 15f ? _selectedButtonStyle : _buttonStyle)) _fieldOfView = 10f;
            if (GUILayout.Button("Normal", _fieldOfView > 15f && _fieldOfView < 45f ? _selectedButtonStyle : _buttonStyle)) _fieldOfView = 30f;
            if (GUILayout.Button("Wide", _fieldOfView >= 45f ? _selectedButtonStyle : _buttonStyle)) _fieldOfView = 50f;
            GUILayout.EndHorizontal();
        }

        private void DrawOutputTab()
        {
            GUILayout.Label("SIZE", _sectionStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("128", _iconSize == 128 ? _selectedButtonStyle : _buttonStyle)) _iconSize = 128;
            if (GUILayout.Button("256", _iconSize == 256 ? _selectedButtonStyle : _buttonStyle)) _iconSize = 256;
            if (GUILayout.Button("512", _iconSize == 512 ? _selectedButtonStyle : _buttonStyle)) _iconSize = 512;
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            GUILayout.Label("QUALITY", _sectionStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("2x", _renderScale == 2 ? _selectedButtonStyle : _buttonStyle)) _renderScale = 2;
            if (GUILayout.Button("4x", _renderScale == 4 ? _selectedButtonStyle : _buttonStyle)) _renderScale = 4;
            if (GUILayout.Button("8x", _renderScale == 8 ? _selectedButtonStyle : _buttonStyle)) _renderScale = 8;
            GUILayout.EndHorizontal();

            int renderRes = _iconSize * _renderScale;
            GUILayout.Label($"Render: {renderRes}x{renderRes} → {_iconSize}x{_iconSize}", _labelStyle);

            GUILayout.Space(10);
            GUILayout.Label("BACKGROUND", _sectionStyle);

            _transparentBackground = GUILayout.Toggle(_transparentBackground, "  Transparent", _buttonStyle);

            GUILayout.Space(10);
            GUILayout.Label("OUTLINE", _sectionStyle);

            _addOutline = GUILayout.Toggle(_addOutline, "  Add Outline", _buttonStyle);

            if (_addOutline)
            {
                GUILayout.Space(5);

                int newWidth = _outlineWidth;
                DrawSliderInt("Width", ref newWidth, 1, 16, "px");
                _outlineWidth = newWidth;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Color:", _labelStyle, GUILayout.Width(40));
                if (GUILayout.Button("⬜", _outlineColor == Color.white ? _selectedButtonStyle : _buttonStyle, GUILayout.Width(30))) { _outlineColor = Color.white; }
                if (GUILayout.Button("⬛", _outlineColor == Color.black ? _selectedButtonStyle : _buttonStyle, GUILayout.Width(30))) { _outlineColor = Color.black; }
                if (GUILayout.Button("🟨", _outlineColor == Color.yellow ? _selectedButtonStyle : _buttonStyle, GUILayout.Width(30))) { _outlineColor = Color.yellow; }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawSlider(string label, ref float value, float min, float max, string suffix, bool updateInstance)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(40));
            float newValue = GUILayout.HorizontalSlider(value, min, max);
            // Use decimal format based on range: tight ranges get more precision
            string format;
            float range = max - min;
            if (range <= 0.5f)
                format = $"{value:F3}{suffix}";
            else if (range <= 2f)
                format = $"{value:F2}{suffix}";
            else if (range <= 10f)
                format = $"{value:F1}{suffix}";
            else
                format = $"{value:F0}{suffix}";
            GUILayout.Label(format, _valueStyle, GUILayout.Width(45));
            GUILayout.EndHorizontal();

            if (newValue != value)
            {
                value = newValue;
                if (updateInstance) UpdateCurrentInstance();
            }
        }

        private void DrawSliderInt(string label, ref int value, int min, int max, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(40));
            int newValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
            GUILayout.Label($"{value}{suffix}", _valueStyle, GUILayout.Width(45));
            GUILayout.EndHorizontal();

            value = newValue;
        }

        private void DrawCanvasPreview()
        {
            if (_dashedLineTexture == null)
            {
                CreateDashedLineTexture();
            }

            // Draw the actual camera render (1:1 square) centered on screen
            float screenCenterX = Screen.width / 2f;
            float screenCenterY = Screen.height / 2f;
            float canvasSize = Mathf.Min(Screen.width, Screen.height);

            float left = screenCenterX - canvasSize / 2f;
            float top = screenCenterY - canvasSize / 2f;
            float right = screenCenterX + canvasSize / 2f;
            float bottom = screenCenterY + canvasSize / 2f;

            // Draw preview - outline is only applied during capture, not in preview (for performance)
            if (_previewRenderTexture != null)
            {
                GUI.DrawTexture(new Rect(left, top, canvasSize, canvasSize), _previewRenderTexture, ScaleMode.ScaleToFit);
            }

            // Draw dashed border
            float dashLength = 10f;
            float lineWidth = 3f;

            // Draw with a simple approach using GUI.DrawTexture with tiling
            Color oldColor = GUI.color;
            GUI.color = Color.black;

            // Top line
            DrawDashedLine(left, top, right, top, dashLength, lineWidth);
            // Bottom line
            DrawDashedLine(left, bottom, right, bottom, dashLength, lineWidth);
            // Left line
            DrawDashedLine(left, top, left, bottom, dashLength, lineWidth);
            // Right line
            DrawDashedLine(right, top, right, bottom, dashLength, lineWidth);

            GUI.color = oldColor;

            // Draw size label with render info
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = Color.black;
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Bold;

            int renderRes = _iconSize * _renderScale;
            string sizeText = _renderScale > 1
                ? $"{_iconSize}x{_iconSize} (render {renderRes}x{renderRes})"
                : $"{_iconSize}x{_iconSize}";

            // Background for label
            float labelWidth = _renderScale > 1 ? 200 : 80;
            GUI.Box(new Rect(screenCenterX - labelWidth / 2, bottom + 5, labelWidth, 25), "");
            GUI.Label(new Rect(screenCenterX - labelWidth / 2, bottom + 5, labelWidth, 25), sizeText, labelStyle);
        }

        private void DrawDashedLine(float x1, float y1, float x2, float y2, float dashLength, float width)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float length = Mathf.Sqrt(dx * dx + dy * dy);

            if (length < 1) return;

            dx /= length;
            dy /= length;

            float pos = 0;
            bool draw = true;

            while (pos < length)
            {
                float segLength = Mathf.Min(dashLength, length - pos);

                if (draw)
                {
                    float startX = x1 + dx * pos;
                    float startY = y1 + dy * pos;
                    float endX = x1 + dx * (pos + segLength);
                    float endY = y1 + dy * (pos + segLength);

                    // Draw line segment
                    if (Mathf.Abs(dx) > Mathf.Abs(dy))
                    {
                        // Horizontal-ish
                        GUI.DrawTexture(new Rect(Mathf.Min(startX, endX), startY - width / 2, Mathf.Abs(endX - startX), width), Texture2D.whiteTexture);
                    }
                    else
                    {
                        // Vertical-ish
                        GUI.DrawTexture(new Rect(startX - width / 2, Mathf.Min(startY, endY), width, Mathf.Abs(endY - startY)), Texture2D.whiteTexture);
                    }
                }

                pos += dashLength;
                draw = !draw;
            }
        }

        private void OnDestroy()
        {
            if (_currentInstance != null)
            {
                Destroy(_currentInstance);
            }
            if (_lastCapture != null)
            {
                Destroy(_lastCapture);
            }
            if (_dashedLineTexture != null)
            {
                Destroy(_dashedLineTexture);
            }
            if (_previewRenderTexture != null)
            {
                if (_thumbnailCamera != null)
                {
                    _thumbnailCamera.targetTexture = null;
                }
                _previewRenderTexture.Release();
                Destroy(_previewRenderTexture);
            }
        }
    }
}
