using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WarMission
{
    /// <summary>Test helper until Milestone 6: after the player dies, Enter (or gamepad Start) reloads the scene.</summary>
    [RequireComponent(typeof(Health))]
    public class PlayerRestart : MonoBehaviour
    {
        Health health;
        GUIStyle style;

        void Awake() => health = GetComponent<Health>();

        void Update()
        {
            if (!health.IsDead) return;
            bool enter = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            bool start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            // The scene must be in Build Settings (TestRange is).
            if (enter || start) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnGUI()
        {
            if (!health.IsDead || Event.current.type != EventType.Repaint) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 60f), "Press Enter to restart", style);
        }
    }
}
