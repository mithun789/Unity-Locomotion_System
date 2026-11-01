using UnityEngine;
using UnityEngine.InputSystem;

namespace WarMission
{
    public enum Callout
    {
        Contact,
        Reloading,
        Flanking,
        MovingUp,
        FallingBack,
        CoveringFire,
        GrenadeOut,
        Grenade,
        ManDown,
        TargetDown,
        LostHim,
        Suspicious,
        TakingCover,
    }

    /// <summary>
    /// Short shouted lines above an AI soldier ("Contact!", "Grenade!", "Reloading!") so the player reads what
    /// the AI decided. Each line has a cooldown so soldiers do not repeat themselves. F4 toggles the text.
    /// No audio yet: SpokeLine is the hook for voice clips later.
    /// </summary>
    public class AICallouts : MonoBehaviour
    {
        static bool visible = true;
        static int toggledFrame = -1;
        static GUIStyle style;

        [SerializeField] float showTime = 1.8f;
        [Tooltip("Seconds before the same line can be said again.")]
        [SerializeField] float repeatCooldown = 6f;
        [SerializeField] float heightAboveHead = 0.35f;
        [SerializeField] float maxDistance = 45f;

        readonly float[] lastSaid = new float[System.Enum.GetValues(typeof(Callout)).Length];
        TeamMember member;
        Health health;
        string text;
        float textUntil;

        public event System.Action<Callout> SpokeLine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            visible = true;
            toggledFrame = -1;
        }

        void Awake()
        {
            member = GetComponent<TeamMember>();
            health = GetComponent<Health>();
            for (int i = 0; i < lastSaid.Length; i++) lastSaid[i] = float.NegativeInfinity;
        }

        /// <summary>Shout a line unless it was said recently. Urgent lines replace whatever is showing.</summary>
        public bool Say(Callout line, bool urgent = false)
        {
            if (health != null && health.IsDead) return false;
            if (Time.time - lastSaid[(int)line] < repeatCooldown) return false;
            if (!urgent && Time.time < textUntil - showTime * 0.5f) return false;
            lastSaid[(int)line] = Time.time;
            text = Text(line);
            textUntil = Time.time + showTime;
            SpokeLine?.Invoke(line);
            return true;
        }

        static string Text(Callout line)
        {
            switch (line)
            {
                case Callout.Contact: return "Contact!";
                case Callout.Reloading: return "Reloading!";
                case Callout.Flanking: return "Flanking!";
                case Callout.MovingUp: return "Moving up!";
                case Callout.FallingBack: return "Falling back!";
                case Callout.CoveringFire: return "Covering fire!";
                case Callout.GrenadeOut: return "Frag out!";
                case Callout.Grenade: return "Grenade!";
                case Callout.ManDown: return "Man down!";
                case Callout.TargetDown: return "Target down!";
                case Callout.LostHim: return "Lost him...";
                case Callout.Suspicious: return "Huh?";
                case Callout.TakingCover: return "Taking cover!";
            }
            return line.ToString();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame && toggledFrame != Time.frameCount)
            {
                toggledFrame = Time.frameCount;
                visible = !visible;
            }
        }

        void OnGUI()
        {
            if (!visible || Time.time >= textUntil || Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null || member == null || member.Head == null) return;
            Vector3 screen = cam.WorldToScreenPoint(member.Head.position + Vector3.up * heightAboveHead);
            if (screen.z <= 0f || screen.z > maxDistance) return;

            style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
            Vector2 size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.color = member.Team == Team.Ally ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.8f, 0.6f);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }
    }
}
