using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Minimal debug HUD drawn with IMGUI: crosshair sized by weapon spread, hit marker, ammo, health.
    /// Milestone 6 replaces this with the real HUD.
    /// </summary>
    public class PlayerHud : MonoBehaviour
    {
        [SerializeField] SoldierWeapon weapon;
        [SerializeField] Health health;
        [SerializeField] Camera viewCamera;
        [SerializeField] SoldierGrenades grenades;

        [Header("Crosshair")]
        [SerializeField] Color crosshairColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] float lineLength = 8f;
        [SerializeField] float lineThickness = 2f;
        [SerializeField] float minGap = 4f;

        [Header("Hit marker")]
        [SerializeField] float hitMarkerTime = 0.15f;

        float hitMarkerEnd;
        bool lastHitKilled;
        GUIStyle labelStyle;

        void Awake()
        {
            if (weapon == null) weapon = GetComponent<SoldierWeapon>();
            if (health == null) health = GetComponent<Health>();
            if (viewCamera == null) viewCamera = Camera.main;
        }

        void Start()
        {
            if (grenades == null) grenades = GetComponent<SoldierGrenades>();
        }

        void OnEnable()
        {
            if (weapon != null) weapon.HitTarget += OnHitTarget;
        }

        void OnDisable()
        {
            if (weapon != null) weapon.HitTarget -= OnHitTarget;
        }

        void OnHitTarget(Health target, bool killed)
        {
            hitMarkerEnd = Time.time + hitMarkerTime * (killed ? 2f : 1f);
            lastHitKilled = killed;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || weapon == null || viewCamera == null) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };

            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            bool alive = health == null || !health.IsDead;

            if (alive)
            {
                // Gap in pixels that matches the spread cone at screen distance.
                float halfFov = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float gap = minGap + Screen.height * 0.5f * Mathf.Tan(weapon.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(halfFov);
                DrawRect(c.x - lineThickness * 0.5f, c.y - gap - lineLength, lineThickness, lineLength, crosshairColor);
                DrawRect(c.x - lineThickness * 0.5f, c.y + gap, lineThickness, lineLength, crosshairColor);
                DrawRect(c.x - gap - lineLength, c.y - lineThickness * 0.5f, lineLength, lineThickness, crosshairColor);
                DrawRect(c.x + gap, c.y - lineThickness * 0.5f, lineLength, lineThickness, crosshairColor);
            }

            if (Time.time < hitMarkerEnd)
            {
                Color color = lastHitKilled ? new Color(1f, 0.2f, 0.2f) : Color.white;
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, c);
                DrawRect(c.x - 12f, c.y - 1f, 8f, 2f, color);
                DrawRect(c.x + 4f, c.y - 1f, 8f, 2f, color);
                DrawRect(c.x - 1f, c.y - 12f, 2f, 8f, color);
                DrawRect(c.x - 1f, c.y + 4f, 2f, 8f, color);
                GUI.matrix = old;
            }

            string ammo = weapon.IsReloading ? "RELOADING" : $"{weapon.Ammo} / {weapon.MagazineSize}";
            GUI.Label(new Rect(Screen.width - 220f, Screen.height - 50f, 200f, 40f), ammo, labelStyle);
            if (grenades != null)
                GUI.Label(new Rect(Screen.width - 220f, Screen.height - 86f, 200f, 40f), $"G  {grenades.Count}", labelStyle);
            if (health != null)
                GUI.Label(new Rect(20f, Screen.height - 50f, 240f, 40f), alive ? $"HP {Mathf.CeilToInt(health.Current)}" : "DEAD", labelStyle);
        }

        static void DrawRect(float x, float y, float w, float h, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
