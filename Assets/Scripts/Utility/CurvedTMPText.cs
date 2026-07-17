using TMPro;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class CurvedTMPText : MonoBehaviour
{
    [Header("Arc")]
    [SerializeField, Min(1f)] private float radius = 320f;
    [SerializeField, Range(-180f, 180f)] private float arcDegrees = 24f;
    [SerializeField] private float angleOffset = 0f;
    [SerializeField] private bool invertCurve;

    [Header("Stretch")]
    [SerializeField, Min(0.1f)] private float verticalScale = 1f;

    private TMP_Text _text;

    private void Awake()
    {
        CacheText();
    }

    private void OnEnable()
    {
        CacheText();
        _text.OnPreRenderText += HandlePreRenderText;
        MarkDirty();
    }

    private void OnDisable()
    {
        if (_text != null)
            _text.OnPreRenderText -= HandlePreRenderText;
    }

    private void OnValidate()
    {
        CacheText();
        if (isActiveAndEnabled)
            MarkDirty();
    }

    private void CacheText()
    {
        if (_text == null)
            _text = GetComponent<TMP_Text>();
    }

    private void MarkDirty()
    {
        if (_text == null)
            return;

        _text.havePropertiesChanged = true;
        _text.SetVerticesDirty();
    }

    private void HandlePreRenderText(TMP_TextInfo textInfo)
    {
        int characterCount = textInfo.characterCount;
        if (characterCount == 0)
            return;

        float minMidX = float.PositiveInfinity;
        float maxMidX = float.NegativeInfinity;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];
            if (!character.isVisible)
                continue;

            float midX = (character.origin + character.xAdvance) * 0.5f;
            if (midX < minMidX) minMidX = midX;
            if (midX > maxMidX) maxMidX = midX;
        }

        if (float.IsNaN(minMidX) || float.IsNaN(maxMidX) || float.IsInfinity(minMidX) || float.IsInfinity(maxMidX))
            return;

        float width = Mathf.Max(0.0001f, maxMidX - minMidX);
        float sign = invertCurve ? -1f : 1f;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];
            if (!character.isVisible)
                continue;

            int materialIndex = character.materialReferenceIndex;
            int vertexIndex = character.vertexIndex;
            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

            Vector3 pivot = new Vector3(
                (vertices[vertexIndex + 0].x + vertices[vertexIndex + 2].x) * 0.5f,
                character.baseLine,
                0f);

            vertices[vertexIndex + 0] -= pivot;
            vertices[vertexIndex + 1] -= pivot;
            vertices[vertexIndex + 2] -= pivot;
            vertices[vertexIndex + 3] -= pivot;

            float currentMidX = (character.origin + character.xAdvance) * 0.5f;
            float t = (currentMidX - minMidX) / width;
            float angleDeg = (t - 0.5f) * arcDegrees + angleOffset;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            Vector3 arcPosition = new Vector3(
                Mathf.Sin(angleRad) * radius,
                sign * (radius - Mathf.Cos(angleRad) * radius) * verticalScale,
                0f);

            Quaternion rotation = Quaternion.Euler(0f, 0f, angleDeg * sign);
            Matrix4x4 matrix = Matrix4x4.TRS(arcPosition, rotation, Vector3.one);

            vertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
            vertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
            vertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
            vertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);
        }
    }
}
