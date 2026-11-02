using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Get the player into a zone (this transform, radius) and stay there for holdTime seconds
    /// (0 = arrive). Leaving the zone resets the hold timer. Shows a HUD marker.
    /// </summary>
    public class ReachZoneObjective : MissionObjective
    {
        [SerializeField] float radius = 3f;
        [Tooltip("Seconds the player must stay in the zone. 0 = just arrive.")]
        [SerializeField] float holdTime = 0f;
        [Tooltip("Enemies alive within this distance pause the hold timer (the zone is contested). 0 = off.")]
        [SerializeField] float contestRadius = 15f;

        float held;
        bool contested;
        float nextContestCheck;

        public override bool HasMarker => !IsComplete;
        public override Vector3 MarkerPosition => transform.position + Vector3.up * 1.5f;

        public override string Progress
        {
            get
            {
                if (holdTime <= 0f) return "";
                if (held <= 0f) return $"Hold the zone for {holdTime:0} s";
                return contested ? $"Zone contested! {held:0}/{holdTime:0} s" : $"Holding {held:0}/{holdTime:0} s";
            }
        }

        public override void Tick(MissionManager mission)
        {
            Transform player = mission.PlayerTransform;
            if (player == null) return;
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > radius * radius)
            {
                held = 0f;
                return;
            }
            if (Time.time >= nextContestCheck)
            {
                nextContestCheck = Time.time + 0.5f;
                contested = contestRadius > 0f && EnemyNear();
            }
            if (!contested) held += Time.deltaTime;
            if (held >= holdTime) IsComplete = true;
        }

        bool EnemyNear()
        {
            var members = TeamRegistry.Members;
            float r = contestRadius * contestRadius;
            for (int i = 0; i < members.Count; i++)
            {
                TeamMember m = members[i];
                if (!m.IsAlive || m.Team != Team.Enemy || (m.transform.position - transform.position).sqrMagnitude >= r) continue;
                // Only fighting AI contest the zone (target dummies do not).
                if (m.TryGetComponent(out AISoldierBrain _)) return true;
            }
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
