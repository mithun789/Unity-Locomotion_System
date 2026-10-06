using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Vision cone with line of sight to head and chest, gunshot hearing, and "who shot me".
    /// Picks the closest visible enemy, preferring the one that recently hit this soldier.
    /// </summary>
    [RequireComponent(typeof(TeamMember))]
    public class AIPerception : MonoBehaviour
    {
        [Header("Vision")]
        [SerializeField] float fieldOfView = 110f;
        [SerializeField] float sightRange = 40f;
        [SerializeField] LayerMask sightMask = ~0;
        [Tooltip("Seconds between vision checks.")]
        [SerializeField] float scanInterval = 0.2f;

        [Header("Hearing")]
        [SerializeField] float hearingRange = 30f;

        [Header("Suppression")]
        [Tooltip("An enemy shot passing this close to the chest counts as being under fire, in metres.")]
        [SerializeField] float nearMissRadius = 2.5f;

        [Header("Target choice")]
        [Tooltip("An attacker counts as this much closer when choosing a target.")]
        [SerializeField] float attackerPreference = 0.5f;
        [Tooltip("How long an attacker stays preferred, in seconds.")]
        [SerializeField] float attackerMemory = 5f;

        readonly RaycastHit[] hits = new RaycastHit[16];
        TeamMember self;
        Health health;
        float nextScan;
        TeamMember attacker;
        float attackerTime = float.NegativeInfinity;

        /// <summary>Best enemy currently in sight, or null.</summary>
        public TeamMember Target { get; private set; }
        /// <summary>When the current target became visible (continuously since then).</summary>
        public float TargetVisibleSince { get; private set; }
        public Vector3 LastSeenPosition { get; private set; }
        public float LastSeenTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Last gunshot heard or last position of whoever hit this soldier.</summary>
        public Vector3 LastHeardPosition { get; private set; }
        public float LastHeardTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Last time an enemy hit this soldier or a shot passed close.</summary>
        public float LastUnderFireTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Where the last threatening shot came from.</summary>
        public Vector3 LastThreatPosition { get; private set; }

        public bool IsUnderFire(float window) => Time.time - LastUnderFireTime <= window;

        void Awake()
        {
            self = GetComponent<TeamMember>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            SoldierWeapon.AnyFired += OnGunshot;
            SoldierWeapon.AnyShotPath += OnShotPath;
            if (health != null) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            SoldierWeapon.AnyFired -= OnGunshot;
            SoldierWeapon.AnyShotPath -= OnShotPath;
            if (health != null) health.Damaged -= OnDamaged;
        }

        void Update()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + scanInterval;
            if (!self.IsAlive)
            {
                Target = null;
                return;
            }
            Scan();
        }

        void Scan()
        {
            TeamMember best = null;
            float bestScore = float.MaxValue;
            var members = TeamRegistry.Members;
            for (int i = 0; i < members.Count; i++)
            {
                TeamMember other = members[i];
                if (other == self || !other.IsAlive || !self.IsEnemyOf(other) || !CanSee(other)) continue;

                float score = Vector3.Distance(transform.position, other.transform.position);
                if (other == attacker && Time.time - attackerTime < attackerMemory) score *= attackerPreference;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = other;
                }
            }

            if (best != Target) TargetVisibleSince = Time.time;
            Target = best;
            if (best != null)
            {
                LastSeenPosition = best.transform.position;
                LastSeenTime = Time.time;
            }
        }

        bool CanSee(TeamMember other)
        {
            Vector3 eye = self.Head.position;
            Vector3 toChest = other.Chest.position - eye;
            if (toChest.sqrMagnitude > sightRange * sightRange) return false;

            Vector3 flat = new Vector3(toChest.x, 0f, toChest.z);
            if (Vector3.Angle(transform.forward, flat) > fieldOfView * 0.5f) return false;

            return HasLineOfSight(eye, other.Head.position, other) || HasLineOfSight(eye, other.Chest.position, other);
        }

        bool HasLineOfSight(Vector3 from, Vector3 to, TeamMember other)
        {
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            int count = Physics.RaycastNonAlloc(from, dir / distance, hits, distance + 0.3f, sightMask, QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            Transform first = null;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance < closest && !hits[i].transform.IsChildOf(transform))
                {
                    closest = hits[i].distance;
                    first = hits[i].transform;
                }
            }
            return first == null || first.IsChildOf(other.transform);
        }

        void OnGunshot(SoldierWeapon weapon)
        {
            if (weapon.transform == transform) return;
            if ((weapon.transform.position - transform.position).sqrMagnitude > hearingRange * hearingRange) return;
            LastHeardPosition = weapon.transform.position;
            LastHeardTime = Time.time;
        }

        void OnDamaged(DamageInfo damage)
        {
            if (damage.Source == null || !damage.Source.TryGetComponent(out TeamMember shooter) || !self.IsEnemyOf(shooter)) return;
            attacker = shooter;
            attackerTime = Time.time;
            LastHeardPosition = shooter.transform.position;
            LastHeardTime = Time.time;
            LastUnderFireTime = Time.time;
            LastThreatPosition = shooter.transform.position;
        }

        /// <summary>Bullets passing close make the soldier feel under fire, even without a hit.</summary>
        void OnShotPath(SoldierWeapon weapon, Vector3 from, Vector3 to)
        {
            if (weapon.transform == transform || !weapon.TryGetComponent(out TeamMember shooter) || !self.IsEnemyOf(shooter)) return;
            Vector3 chest = self.Chest.position;
            Vector3 segment = to - from;
            float t = Mathf.Clamp01(Vector3.Dot(chest - from, segment) / Mathf.Max(segment.sqrMagnitude, 0.0001f));
            if ((from + segment * t - chest).sqrMagnitude > nearMissRadius * nearMissRadius) return;
            LastUnderFireTime = Time.time;
            LastThreatPosition = shooter.transform.position;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Gizmos.color = Target != null ? Color.red : Color.yellow;
            Vector3 left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * transform.forward;
            Gizmos.DrawLine(eye, eye + left * sightRange);
            Gizmos.DrawLine(eye, eye + right * sightRange);
            if (Target != null) Gizmos.DrawLine(eye, Target.Chest.position);
        }
    }
}
