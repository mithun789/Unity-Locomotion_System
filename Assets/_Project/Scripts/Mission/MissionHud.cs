using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Mission HUD (IMGUI): objective list with progress, an on-screen marker with distance for the current
    /// objective (clamped to the screen edge when off screen), centred announcements, and the end screen.
    /// PlayerHud still draws the crosshair, ammo, health and damage direction.
    /// </summary>
    [RequireComponent(typeof(MissionManager))]
    public class MissionHud : MonoBehaviour
    {
        [SerializeField] Color objectiveColor = new Color(1f, 0.85f, 0.35f);
        [SerializeField] Color doneColor = new Color(0.5f, 0.9f, 0.5f);
        [SerializeField] float markerEdgeMargin = 40f;

        MissionManager mission;
        GUIStyle titleStyle, lineStyle, bannerStyle, markerStyle, centredStyle;

        void Awake() => mission = GetComponent<MissionManager>();

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            DrawObjectives();
            DrawMarker();
            DrawBanner();
            if (mission.CurrentPhase == MissionManager.Phase.Won || mission.CurrentPhase == MissionManager.Phase.Lost) DrawEndScreen();
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            markerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            centredStyle = new GUIStyle(lineStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
        }

        void DrawObjectives()
        {
            float x = 20f, y = 20f;
            Shadowed(new Rect(x, y, 500f, 26f), mission.MissionName.ToUpper(), titleStyle, Color.white);
            y += 30f;
            for (int i = 0; i < mission.ObjectiveCount; i++)
            {
                MissionObjective objective = mission.GetObjective(i);
                if (objective == null) continue;
                bool done = i < mission.ObjectiveIndex || objective.IsComplete;
                bool current = i == mission.ObjectiveIndex && !done;
                if (!done && !current) continue;   // future objectives stay hidden

                string mark = done ? "[x] " : "[ ] ";
                Shadowed(new Rect(x, y, 600f, 22f), mark + objective.Title, lineStyle, done ? doneColor : objectiveColor);
                y += 22f;
                if (current && objective.Progress.Length > 0)
                {
                    Shadowed(new Rect(x + 28f, y, 600f, 22f), objective.Progress, lineStyle, Color.white);
                    y += 22f;
                }
            }
            if (mission.CurrentPhase == MissionManager.Phase.Running)
                Shadowed(new Rect(x, y + 4f, 300f, 22f), FormatTime(mission.ElapsedTime), lineStyle, new Color(1f, 1f, 1f, 0.7f));
        }

        void DrawMarker()
        {
            MissionObjective objective = mission.CurrentObjective;
            Camera cam = Camera.main;
            if (mission.CurrentPhase != MissionManager.Phase.Running || objective == null || !objective.HasMarker || cam == null) return;

            Vector3 world = objective.MarkerPosition;
            Vector3 screen = cam.WorldToScreenPoint(world);
            bool behind = screen.z < 0f;
            if (behind) screen = -screen;
            float gx = screen.x, gy = Screen.height - screen.y;
            bool offScreen = behind || gx < 0f || gx > Screen.width || gy < 0f || gy > Screen.height;
            if (offScreen)
            {
                // Push to the screen edge along the direction from the centre.
                Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 dir = new Vector2(gx, gy) - centre;
                if (dir.sqrMagnitude < 1f) dir = Vector2.down;
                float sx = (Screen.width * 0.5f - markerEdgeMargin) / Mathf.Max(Mathf.Abs(dir.x), 0.001f);
                float sy = (Screen.height * 0.5f - markerEdgeMargin) / Mathf.Max(Mathf.Abs(dir.y), 0.001f);
                Vector2 edge = centre + dir * Mathf.Min(sx, sy);
                gx = edge.x;
                gy = edge.y;
            }

            float distance = mission.PlayerTransform != null ? Vector3.Distance(mission.PlayerTransform.position, world) : 0f;
            DrawRect(gx - 7f, gy - 7f, 14f, 14f, objectiveColor);
            DrawRect(gx - 4f, gy - 4f, 8f, 8f, new Color(0f, 0f, 0f, 0.6f));
            Shadowed(new Rect(gx - 60f, gy + 10f, 120f, 22f), $"{distance:0} m", markerStyle, objectiveColor);
        }

        void DrawBanner()
        {
            if (Time.time >= mission.AnnouncementUntil || string.IsNullOrEmpty(mission.Announcement)) return;
            float alpha = Mathf.Clamp01((mission.AnnouncementUntil - Time.time) / 0.5f);
            Shadowed(new Rect(0f, Screen.height * 0.22f, Screen.width, 50f), mission.Announcement.ToUpper(), bannerStyle, new Color(1f, 1f, 1f, alpha));
        }

        void DrawEndScreen()
        {
            DrawRect(0f, 0f, Screen.width, Screen.height, new Color(0f, 0f, 0f, 0.45f));
            bool won = mission.CurrentPhase == MissionManager.Phase.Won;
            float y = Screen.height * 0.38f;
            Shadowed(new Rect(0f, y, Screen.width, 50f), won ? "MISSION COMPLETE" : "MISSION FAILED", bannerStyle,
                     won ? doneColor : new Color(1f, 0.35f, 0.3f));
            int accuracy = mission.ShotsFired > 0 ? Mathf.RoundToInt(100f * mission.ShotsHit / mission.ShotsFired) : 0;
            string stats = $"Time {FormatTime(mission.ElapsedTime)}    Kills {mission.Kills}    Accuracy {accuracy}%";
            Shadowed(new Rect(0f, y + 60f, Screen.width, 30f), stats, centredStyle, Color.white);
            Shadowed(new Rect(0f, y + 100f, Screen.width, 30f), "Press Enter to restart", centredStyle, new Color(1f, 1f, 1f, 0.8f));
        }

        static string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        static void DrawRect(float x, float y, float w, float h, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
