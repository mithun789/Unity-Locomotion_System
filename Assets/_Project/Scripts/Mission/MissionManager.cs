using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WarMission
{
    /// <summary>
    /// Runs a mission: objectives in order, win when the last one completes, lose when the player dies.
    /// Enter (gamepad Start) restarts after the end. Tracks time and player kills for the end screen.
    /// MissionHud draws it. Set up by War Mission > Add Mission To Open Scene.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        public enum Phase
        {
            Briefing,
            Running,
            Won,
            Lost,
        }

        [SerializeField] string missionName = "Operation Breakpoint";
        [SerializeField] MissionObjective[] objectives;
        [Tooltip("Found automatically (the soldier with PlayerSoldierInput) when empty.")]
        [SerializeField] Health player;
        [Tooltip("Seconds the mission title shows before the first objective starts.")]
        [SerializeField] float briefingTime = 3f;
        [SerializeField] float announceTime = 2.5f;

        int index = -1;
        float phaseStart;
        float runStart;
        float endTime;
        SoldierWeapon playerWeapon;

        public string MissionName => missionName;
        public Phase CurrentPhase { get; private set; } = Phase.Briefing;
        public Transform PlayerTransform => player != null ? player.transform : null;
        public MissionObjective CurrentObjective => objectives != null && index >= 0 && index < objectives.Length ? objectives[index] : null;
        public int ObjectiveIndex => index;
        public int ObjectiveCount => objectives != null ? objectives.Length : 0;
        public MissionObjective GetObjective(int i) => objectives[i];
        public float ElapsedTime => CurrentPhase == Phase.Running ? Time.time - runStart : CurrentPhase == Phase.Briefing ? 0f : endTime;
        public int Kills { get; private set; }
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }
        public string Announcement { get; private set; }
        public float AnnouncementUntil { get; private set; }

        public event Action<Phase> PhaseChanged;

        void Start()
        {
            if (player == null)
            {
                var input = FindAnyObjectByType<PlayerSoldierInput>();
                if (input != null) player = input.GetComponent<Health>();
            }
            if (player != null)
            {
                player.Died += OnPlayerDied;
                // The mission handles restarting; the test helper would draw a second prompt.
                if (player.TryGetComponent(out PlayerRestart restart)) restart.enabled = false;
                if (player.TryGetComponent(out playerWeapon))
                {
                    playerWeapon.Fired += OnPlayerFired;
                    playerWeapon.HitTarget += OnPlayerHit;
                }
            }
            else
            {
                Debug.LogWarning("[Mission] No player found; the mission cannot be lost.", this);
            }
            phaseStart = Time.time;
            Announce(missionName);
        }

        void OnDestroy()
        {
            if (player != null) player.Died -= OnPlayerDied;
            if (playerWeapon != null)
            {
                playerWeapon.Fired -= OnPlayerFired;
                playerWeapon.HitTarget -= OnPlayerHit;
            }
        }

        void Update()
        {
            switch (CurrentPhase)
            {
                case Phase.Briefing:
                    if (Time.time - phaseStart >= briefingTime)
                    {
                        SetPhase(Phase.Running);
                        runStart = Time.time;
                        NextObjective();
                    }
                    break;

                case Phase.Running:
                    MissionObjective current = CurrentObjective;
                    if (current == null) break;
                    current.Tick(this);
                    if (current.IsComplete)
                    {
                        current.Deactivate();
                        NextObjective();
                    }
                    break;

                default:
                    bool enter = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
                    bool start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
                    if (enter || start) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                    break;
            }
        }

        void NextObjective()
        {
            index++;
            if (objectives == null || index >= objectives.Length)
            {
                endTime = Time.time - runStart;
                SetPhase(Phase.Won);
                Announce("Mission complete");
                return;
            }
            MissionObjective next = objectives[index];
            if (next == null)
            {
                NextObjective();
                return;
            }
            Announce(next.Title);
            next.Activate(this);
        }

        /// <summary>Big centred message for a few seconds (new objective, wave incoming).</summary>
        public void Announce(string text)
        {
            Announcement = text;
            AnnouncementUntil = Time.time + announceTime;
        }

        void SetPhase(Phase phase)
        {
            CurrentPhase = phase;
            phaseStart = Time.time;
            PhaseChanged?.Invoke(phase);
        }

        void OnPlayerDied(DamageInfo damage)
        {
            if (CurrentPhase == Phase.Won || CurrentPhase == Phase.Lost) return;
            endTime = CurrentPhase == Phase.Running ? Time.time - runStart : 0f;
            SetPhase(Phase.Lost);
            Announce("Mission failed");
        }

        void OnPlayerFired() => ShotsFired++;

        void OnPlayerHit(Health target, bool killed)
        {
            ShotsHit++;
            if (killed && target != null && target.TryGetComponent(out TeamMember member) && member.Team == Team.Enemy) Kills++;
        }
    }
}
