using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WarMission
{
    /// <summary>
    /// Watches an AI-only battle: alive count per team, elapsed time, and the winner once one side is gone.
    /// Enter restarts the scene. Debug IMGUI until the Milestone 6 HUD.
    /// </summary>
    public class SquadBattleMonitor : MonoBehaviour
    {
        GUIStyle style;
        float endTime = -1f;

        public int AliveAllies { get; private set; }
        public int AliveEnemies { get; private set; }
        public bool IsOver => endTime >= 0f;
        public Team Winner { get; private set; }

        void Update()
        {
            AliveAllies = AliveEnemies = 0;
            var members = TeamRegistry.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].IsAlive) continue;
                if (members[i].Team == Team.Ally) AliveAllies++;
                else AliveEnemies++;
            }

            if (!IsOver && Time.timeSinceLevelLoad > 1f && (AliveAllies == 0 || AliveEnemies == 0))
            {
                endTime = Time.timeSinceLevelLoad;
                Winner = AliveAllies > 0 ? Team.Ally : Team.Enemy;
                Debug.Log($"[SquadBattle] {Winner} wins after {endTime:F1} s ({AliveAllies} allies, {AliveEnemies} enemies left)");
            }

            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };

            float time = IsOver ? endTime : Time.timeSinceLevelLoad;
            GUI.color = new Color(0.45f, 0.65f, 1f);
            GUI.Label(new Rect(20f, 20f, 300f, 36f), $"Allies  {AliveAllies}", style);
            GUI.color = new Color(1f, 0.45f, 0.4f);
            GUI.Label(new Rect(20f, 54f, 300f, 36f), $"Enemies {AliveEnemies}", style);
            GUI.color = Color.white;
            GUI.Label(new Rect(20f, 88f, 300f, 36f), $"{time:F0} s", style);
            if (IsOver)
                GUI.Label(new Rect(20f, 130f, 600f, 36f), $"{(Winner == Team.Ally ? "ALLIES" : "ENEMIES")} WIN  -  Enter to restart", style);
        }
    }
}
