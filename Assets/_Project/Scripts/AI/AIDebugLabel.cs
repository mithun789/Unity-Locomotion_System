using UnityEngine;
using UnityEngine.InputSystem;

namespace WarMission
{
    /// <summary>
    /// Debug text above an AI soldier: state, intent, top decision scores, personality, health, ammo,
    /// suppression and squad morale. F3 toggles all labels. IMGUI until the Milestone 6 HUD.
    /// </summary>
    [RequireComponent(typeof(AISoldierBrain))]
    public class AIDebugLabel : MonoBehaviour
    {
        static bool visible;
        static int toggledFrame = -1;

        [SerializeField] float heightAboveHead = 0.9f;
        [SerializeField] float maxDistance = 60f;

        static GUIStyle style;
        readonly int[] order = new int[AIDecisionMaker.IntentCount];
        AISoldierBrain brain;
        AIPerception perception;
        SoldierWeapon weapon;
        Health health;
        TeamMember member;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            visible = false;
            toggledFrame = -1;
        }

        void Awake()
        {
            brain = GetComponent<AISoldierBrain>();
            perception = GetComponent<AIPerception>();
            weapon = GetComponent<SoldierWeapon>();
            health = GetComponent<Health>();
            member = GetComponent<TeamMember>();
        }

        void Update()
        {
            // Every label sees the key; only the first one per frame flips the shared flag.
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame && toggledFrame != Time.frameCount)
            {
                toggledFrame = Time.frameCount;
                visible = !visible;
            }
        }

        void OnGUI()
        {
            if (!visible || Event.current.type != EventType.Repaint || brain.CurrentState == AISoldierBrain.State.Dead) return;
            Camera cam = Camera.main;
            if (cam == null || member == null || member.Head == null) return;

            Vector3 world = member.Head.position + Vector3.up * heightAboveHead;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f || screen.z > maxDistance) return;

            style ??= new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerCenter, richText = true };
            string text = BuildText();
            Vector2 size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x - 4f, rect.y - 2f, rect.width + 8f, rect.height + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }

        string BuildText()
        {
            string colour = member.Team == Team.Ally ? "#7fb2ff" : "#ff8a7a";
            string head = brain.InCombat
                ? $"<b><color={colour}>{brain.CurrentState}</color></b>  ({brain.Intent})"
                : $"<b><color={colour}>{brain.CurrentState}</color></b>";

            AIPersonality p = brain.Personality;
            string traits = p != null ? $"{p.Archetype}  A{p.Aggression:0.0} C{p.Caution:0.0} D{p.Discipline:0.0} T{p.Teamwork:0.0}" : "";

            float hp = health != null ? health.Current : 0f;
            string ammo = weapon != null ? $"{weapon.Ammo}/{weapon.MagazineSize}" : "-";
            string morale = brain.Squad != null ? $"  M{brain.Squad.Morale:0.00}" : "";
            string nades = brain.HasGrenades ? "  G" : "";
            string aware = perception.Suspicion > 0f ? $"  Sus {perception.Suspicion:0.00}" : "";
            string stats = $"HP {hp:0}  Ammo {ammo}{nades}  Sup {perception.Suppression:0.00}{morale}{aware}";

            if (!brain.InCombat) return $"{head}\n{traits}\n{stats}";
            return $"{head}\n{TopScores()}\n{traits}\n{stats}";
        }

        /// <summary>The three best intents with their scores.</summary>
        string TopScores()
        {
            float[] scores = brain.Decision.Scores;
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => scores[b].CompareTo(scores[a]));
            string text = "";
            for (int i = 0; i < 3 && i < order.Length; i++)
            {
                if (scores[order[i]] <= 0f) break;
                if (i > 0) text += "  ";
                text += $"{(AIIntent)order[i]} {scores[order[i]]:0.00}";
            }
            return text;
        }
    }
}
