using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Draws the scope view (IMGUI): black around a round lens with a soft edge, a duplex reticle with
    /// range ticks and a red centre dot, a short black "scope-in" fade, the magnification and the breath bar.
    /// </summary>
    [RequireComponent(typeof(WeaponScope))]
    public class ScopeOverlay : MonoBehaviour
    {
        [SerializeField] Color reticleColor = new Color(0.02f, 0.02f, 0.02f, 0.95f);
        [SerializeField] Color dotColor = new Color(1f, 0.15f, 0.1f, 1f);
        [Tooltip("Lens diameter as a share of the screen height.")]
        [Range(0.5f, 1f)] [SerializeField] float lensSize = 0.94f;

        const int MaskSize = 512;
        WeaponScope scope;
        Texture2D lensMask;
        GUIStyle textStyle;

        void Awake() => scope = GetComponent<WeaponScope>();

        void OnDestroy()
        {
            if (lensMask != null) Destroy(lensMask);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !scope.IsScoped) return;
            if (lensMask == null) lensMask = BuildLensMask();
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };

            float size = Screen.height * lensSize;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var lens = new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size);

            // Black outside the lens.
            Fill(new Rect(0f, 0f, lens.xMin + 1f, Screen.height), Color.black);
            Fill(new Rect(lens.xMax - 1f, 0f, Screen.width - lens.xMax + 1f, Screen.height), Color.black);
            Fill(new Rect(lens.xMin, 0f, size, lens.yMin + 1f), Color.black);
            Fill(new Rect(lens.xMin, lens.yMax - 1f, size, Screen.height - lens.yMax + 1f), Color.black);
            GUI.color = Color.white;
            GUI.DrawTexture(lens, lensMask);

            DrawReticle(cx, cy, size * 0.5f);

            // Scope-in: the view fades up from black right after the overlay appears.
            float fade = 1f - Mathf.Clamp01((scope.Blend - scope.OverlayStart) / Mathf.Max(0.01f, 1f - scope.OverlayStart));
            if (fade > 0f) Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, fade));

            DrawInfo(lens);
        }

        void DrawReticle(float cx, float cy, float radius)
        {
            float thin = Mathf.Max(1f, Screen.height / 720f);
            float thick = thin * 4f;
            float gap = radius * 0.3f;

            // Thin cross through the centre, thick posts toward the edge (duplex).
            Fill(new Rect(cx - gap, cy - thin * 0.5f, gap * 2f, thin), reticleColor);
            Fill(new Rect(cx - thin * 0.5f, cy - gap, thin, gap * 2f), reticleColor);
            Fill(new Rect(cx - radius, cy - thick * 0.5f, radius - gap, thick), reticleColor);
            Fill(new Rect(cx + gap, cy - thick * 0.5f, radius - gap, thick), reticleColor);
            Fill(new Rect(cx - thick * 0.5f, cy + gap, thick, radius - gap), reticleColor);
            Fill(new Rect(cx - thick * 0.5f, cy - radius, thick, radius - gap), reticleColor);

            // Range / hold-over ticks below the centre, wind ticks to the sides.
            float step = gap / 5f;
            for (int i = 1; i <= 4; i++)
            {
                float w = i % 2 == 0 ? step * 1.2f : step * 0.7f;
                Fill(new Rect(cx - w * 0.5f, cy + step * i - thin * 0.5f, w, thin), reticleColor);
                Fill(new Rect(cx + step * i - thin * 0.5f, cy - w * 0.25f, thin, w * 0.5f), reticleColor);
                Fill(new Rect(cx - step * i - thin * 0.5f, cy - w * 0.25f, thin, w * 0.5f), reticleColor);
            }

            float dot = thin * 3f;
            Fill(new Rect(cx - dot * 0.5f, cy - dot * 0.5f, dot, dot), dotColor);
        }

        void DrawInfo(Rect lens)
        {
            float x = lens.xMax + 20f;
            float y = Screen.height * 0.5f - 40f;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(x, y, 200f, 24f), $"{scope.Magnification:0.#}x", textStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(x, y + 24f, 260f, 24f), "Scroll: zoom", textStyle);

            // Breath bar: shows while holding, recovering or out of breath.
            if (scope.BreathRemaining >= 0.999f && !scope.IsHoldingBreath)
            {
                GUI.Label(new Rect(x, y + 48f, 260f, 24f), "Shift: hold breath", textStyle);
                GUI.color = Color.white;
                return;
            }
            float barWidth = 140f;
            Fill(new Rect(x, y + 56f, barWidth, 8f), new Color(0f, 0f, 0f, 0.6f));
            Color fill = scope.IsExhausted ? new Color(1f, 0.3f, 0.25f, 0.9f) : new Color(0.85f, 0.9f, 1f, 0.9f);
            Fill(new Rect(x, y + 56f, barWidth * scope.BreathRemaining, 8f), fill);
            GUI.color = Color.white;
        }

        /// <summary>Transparent lens with a soft dark rim, black outside the circle.</summary>
        static Texture2D BuildLensMask()
        {
            var texture = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[MaskSize * MaskSize];
            float half = MaskSize * 0.5f;
            for (int y = 0; y < MaskSize; y++)
            {
                for (int x = 0; x < MaskSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Clear centre, darkening toward the rim (lens vignette), solid black past the edge.
                    float vignette = Mathf.SmoothStep(0f, 0.55f, Mathf.InverseLerp(0.8f, 0.985f, r));
                    float outside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.985f, 1f, r));
                    byte a = (byte)(Mathf.Clamp01(Mathf.Max(vignette, outside)) * 255f);
                    pixels[y * MaskSize + x] = new Color32(0, 0, 0, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
    }
}
