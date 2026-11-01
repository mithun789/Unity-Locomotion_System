using System.Collections.Generic;
using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// A group of AI soldiers (children of this object). Shares enemy sightings so the whole squad
    /// reacts when one member sees something, and gives each member a formation slot on the route.
    /// Also coordinates tactics: one flanker at a time, reload and flank callouts (squad mates suppress
    /// to cover them), and morale that drops as members die and recovers with kills.
    /// </summary>
    public class Squad : MonoBehaviour
    {
        [Tooltip("Sideways distance between members walking a shared route, in metres.")]
        [SerializeField] float formationSpacing = 3f;
        [Tooltip("Members report a sighting at most this often, in seconds.")]
        [SerializeField] float reportInterval = 0.5f;

        [Header("Morale (0..1)")]
        [Tooltip("Morale lost per dead member, as a share of the starting squad size (1 = lose 1/size).")]
        [SerializeField] float lossPerDeath = 1f;
        [SerializeField] float gainPerKill = 0.15f;
        [Tooltip("Morale regained per second, up to recoverCap, once nobody died for recoverDelay seconds.")]
        [SerializeField] float recoverRate = 0.02f;
        [SerializeField] float recoverDelay = 15f;
        [SerializeField] float recoverCap = 0.8f;

        [Header("Callouts")]
        [Tooltip("A reload or flank callout counts as active for this long, in seconds.")]
        [SerializeField] float calloutTime = 3f;

        readonly List<AISoldierBrain> members = new List<AISoldierBrain>();
        float nextReportTime;
        int startSize;
        float morale = 1f;
        float lastDeathTime = float.NegativeInfinity;
        float lastReloadCall = float.NegativeInfinity;
        AISoldierBrain reloadCaller;
        AISoldierBrain flanker;

        public Vector3 LastReportPosition { get; private set; }
        public float LastReportTime { get; private set; } = float.NegativeInfinity;

        public int AliveCount
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null && members[i].IsAlive) alive++;
                return alive;
            }
        }

        public float Morale => morale;

        /// <summary>The member currently flanking, or null.</summary>
        public AISoldierBrain Flanker => flanker != null && flanker.IsAlive ? flanker : null;

        void Update()
        {
            if (Time.time - lastDeathTime >= recoverDelay && morale < recoverCap)
                morale = Mathf.Min(recoverCap, morale + recoverRate * Time.deltaTime);
        }

        public void Register(AISoldierBrain member)
        {
            if (members.Contains(member)) return;
            members.Add(member);
            startSize = Mathf.Max(startSize, members.Count);
        }

        public void Unregister(AISoldierBrain member)
        {
            members.Remove(member);
            ReleaseFlank(member);
        }

        public void Report(Vector3 enemyPosition)
        {
            if (Time.time < nextReportTime) return;
            nextReportTime = Time.time + reportInterval;
            LastReportPosition = enemyPosition;
            LastReportTime = Time.time;
        }

        public void OnMemberDied(AISoldierBrain member)
        {
            morale = Mathf.Max(0f, morale - lossPerDeath / Mathf.Max(1, startSize));
            lastDeathTime = Time.time;
            ReleaseFlank(member);

            // The closest living mate shouts it.
            AISoldierBrain closest = null;
            float best = float.MaxValue;
            for (int i = 0; i < members.Count; i++)
            {
                AISoldierBrain other = members[i];
                if (other == null || other == member || !other.IsAlive) continue;
                float d = (other.transform.position - member.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    closest = other;
                }
            }
            if (closest != null) closest.Say(Callout.ManDown, urgent: true);
        }

        public void OnEnemyKilled() => morale = Mathf.Min(1f, morale + gainPerKill);

        // ---------------------------------------------------------------- tactics

        /// <summary>Only one member flanks at a time; the others hold and suppress.</summary>
        public bool TryClaimFlank(AISoldierBrain member)
        {
            if (Flanker != null && Flanker != member) return false;
            flanker = member;
            return true;
        }

        public void ReleaseFlank(AISoldierBrain member)
        {
            if (flanker == member) flanker = null;
        }

        public bool IsFlankFree(AISoldierBrain member) => Flanker == null || Flanker == member;

        public void CallReloading(AISoldierBrain member)
        {
            lastReloadCall = Time.time;
            reloadCaller = member;
        }

        /// <summary>True when another member is flanking or reloading and wants covering fire.</summary>
        public bool NeedsCoveringFire(AISoldierBrain asking)
        {
            if (Flanker != null && Flanker != asking) return true;
            return reloadCaller != asking && Time.time - lastReloadCall < calloutTime;
        }

        /// <summary>Formation offset for a member, centred on the route (0, +1, -1, +2, ...), in world space.</summary>
        public Vector3 SlotOffset(Transform member, Vector3 routeDirection)
        {
            int index = member.GetSiblingIndex();
            int side = index % 2 == 0 ? 1 : -1;
            float offset = (index + 1) / 2 * side * formationSpacing;
            routeDirection.y = 0f;
            Vector3 right = routeDirection.sqrMagnitude > 0.001f ? Vector3.Cross(Vector3.up, routeDirection.normalized) : transform.right;
            return right * offset;
        }
    }
}
