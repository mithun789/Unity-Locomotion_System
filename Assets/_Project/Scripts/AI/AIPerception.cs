using System.Collections.Generic;
using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// What an AI soldier notices.
    /// Sight: vision cone with line of sight to head and chest. Spotting is gradual: awareness of each enemy
    /// in view fills up (faster when close, in the centre of view, moving, firing; slower when crouched),
    /// and the enemy becomes a target at full awareness. Half-noticed enemies make the soldier suspicious.
    /// Hearing: gunshots, explosions, and footsteps of enemies moving close by.
    /// Also "who shot me", suppression from near misses, and live grenades nearby.
    /// Target choice: a threat score (distance, attacker, aiming at me, wounded) that also spreads fire
    /// across enemies other AI already target.
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

        [Header("Spotting (awareness 0..1 per enemy)")]
        [Tooltip("Awareness gained per second for an enemy in plain view at close range, centre of view.")]
        [SerializeField] float spotRate = 2.5f;
        [Tooltip("Awareness gain at the edge of sight range, relative to close range.")]
        [SerializeField] float farSpotFactor = 0.25f;
        [Tooltip("Outside this half-angle (degrees) the enemy is in peripheral vision and spotted slower.")]
        [SerializeField] float focusAngle = 25f;
        [SerializeField] float peripheralFactor = 0.45f;
        [SerializeField] float crouchedFactor = 0.55f;
        [SerializeField] float sprintingFactor = 1.5f;
        [Tooltip("Spot rate multiplier for an enemy that fired in the last 2 s.")]
        [SerializeField] float firingFactor = 4f;
        [Tooltip("Spot rate multiplier while already fighting (alert soldiers spot almost at once).")]
        [SerializeField] float alertFactor = 5f;
        [Tooltip("Awareness lost per second while an enemy is out of view.")]
        [SerializeField] float awarenessDecay = 0.25f;
        [Tooltip("Awareness at which the soldier gets suspicious and looks.")]
        [SerializeField] float suspicionThreshold = 0.3f;

        [Header("Hearing")]
        [SerializeField] float hearingRange = 30f;
        [SerializeField] float explosionHearingRange = 60f;
        [Tooltip("Footsteps are heard within these distances (walk, sprint), in metres. Crouched steps are silent.")]
        [SerializeField] float walkHearing = 6f;
        [SerializeField] float sprintHearing = 14f;

        [Header("Suppression")]
        [Tooltip("An enemy shot passing this close to the chest counts as being under fire, in metres.")]
        [SerializeField] float nearMissRadius = 2.5f;
        [Tooltip("Suppression added by a near miss / a hit / a nearby blast (suppression runs 0..1).")]
        [SerializeField] float nearMissSuppression = 0.2f;
        [SerializeField] float hitSuppression = 0.35f;
        [SerializeField] float blastSuppression = 0.6f;
        [Tooltip("Suppression lost per second.")]
        [SerializeField] float suppressionDecay = 0.3f;

        [Header("Target choice (lower score wins; score starts at distance)")]
        [Tooltip("Score multiplier for whoever hit this soldier recently.")]
        [SerializeField] float attackerPreference = 0.5f;
        [Tooltip("How long an attacker stays preferred, in seconds.")]
        [SerializeField] float attackerMemory = 5f;
        [Tooltip("Score multiplier for an enemy aiming at this soldier.")]
        [SerializeField] float aimingAtMePreference = 0.75f;
        [Tooltip("Score multiplier for a badly wounded enemy (finish it).")]
        [SerializeField] float woundedPreference = 0.8f;
        [Tooltip("Score multiplier per other AI already targeting that enemy.")]
        [SerializeField] float crowdedPenalty = 1.25f;
        [Tooltip("Score multiplier for keeping the current target (avoids flicking between targets).")]
        [SerializeField] float keepTargetPreference = 0.85f;

        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Dictionary<TeamMember, float> awareness = new Dictionary<TeamMember, float>();
        readonly List<TeamMember> awarenessKeys = new List<TeamMember>();
        TeamMember self;
        Health health;
        float nextScan;
        float lastScanTime;
        TeamMember attacker;
        float attackerTime = float.NegativeInfinity;
        float suppression;
        float suppressionTime;
        Vector3 previousSeenPosition;
        float previousSeenTime = float.NegativeInfinity;

        /// <summary>Best enemy currently in sight and fully spotted, or null.</summary>
        public TeamMember Target { get; private set; }
        /// <summary>When the current target became visible (continuously since then).</summary>
        public float TargetVisibleSince { get; private set; }
        public Vector3 LastSeenPosition { get; private set; }
        public float LastSeenTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Last gunshot, explosion or footsteps heard, or last position of whoever hit this soldier.</summary>
        public Vector3 LastHeardPosition { get; private set; }
        public float LastHeardTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Last time an enemy hit this soldier or a shot passed close.</summary>
        public float LastUnderFireTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Where the last threatening shot came from.</summary>
        public Vector3 LastThreatPosition { get; private set; }

        /// <summary>The enemy seen last (may be out of sight or dead now).</summary>
        public TeamMember LastSeenTarget { get; private set; }
        /// <summary>Flat velocity of the last seen enemy, measured between sightings.</summary>
        public Vector3 LastSeenVelocity { get; private set; }
        /// <summary>Number of spotted enemies in sight at the last scan.</summary>
        public int VisibleEnemyCount { get; private set; }
        /// <summary>Last time this soldier saw an enemy or came under fire.</summary>
        public float LastContactTime => Mathf.Max(LastSeenTime, LastUnderFireTime);

        /// <summary>Highest awareness of an enemy not yet spotted (0..1), and where it was.</summary>
        public float Suspicion { get; private set; }
        public Vector3 SuspiciousPosition { get; private set; }
        public bool IsSuspicious => Suspicion >= suspicionThreshold;

        /// <summary>Nearest live grenade that will hurt this soldier, or null.</summary>
        public Grenade DangerGrenade { get; private set; }

        /// <summary>0..1: how pinned down this soldier feels. Rises with near misses and hits, decays over time.</summary>
        public float Suppression => Mathf.Max(0f, suppression - (Time.time - suppressionTime) * suppressionDecay);

        public bool IsUnderFire(float window) => Time.time - LastUnderFireTime <= window;

        /// <summary>Where the last seen enemy probably is now: last position plus its velocity, up to maxLead seconds.</summary>
        public Vector3 PredictedTargetPosition(float maxLead)
        {
            float lead = Mathf.Clamp(Time.time - LastSeenTime, 0f, maxLead);
            return LastSeenPosition + LastSeenVelocity * lead;
        }

        /// <summary>Awareness (0..1) of one enemy.</summary>
        public float AwarenessOf(TeamMember enemy) => enemy != null && awareness.TryGetValue(enemy, out float a) ? a : 0f;

        void AddSuppression(float amount)
        {
            suppression = Mathf.Min(1f, Suppression + amount);
            suppressionTime = Time.time;
        }

        void Awake()
        {
            self = GetComponent<TeamMember>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            SoldierWeapon.AnyFired += OnGunshot;
            SoldierWeapon.AnyShotPath += OnShotPath;
            Grenade.AnyExploded += OnExplosion;
            if (health != null) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            SoldierWeapon.AnyFired -= OnGunshot;
            SoldierWeapon.AnyShotPath -= OnShotPath;
            Grenade.AnyExploded -= OnExplosion;
            if (health != null) health.Damaged -= OnDamaged;
            SetTarget(null);
        }

        void Update()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + scanInterval;
            float dt = Mathf.Min(Time.time - lastScanTime, 0.5f);
            lastScanTime = Time.time;
            if (!self.IsAlive)
            {
                SetTarget(null);
                return;
            }
            Scan(dt);
            ScanGrenades();
        }

        void Scan(float dt)
        {
            TeamMember best = null;
            float bestScore = float.MaxValue;
            int visible = 0;
            float suspicion = 0f;
            Vector3 suspicious = SuspiciousPosition;
            bool alert = Time.time - LastContactTime < 10f;

            var members = TeamRegistry.Members;
            for (int i = 0; i < members.Count; i++)
            {
                TeamMember other = members[i];
                if (other == self || !other.IsAlive || !self.IsEnemyOf(other)) continue;

                float aware = AwarenessOf(other);
                if (CanSee(other, out float distance, out float angle))
                {
                    if (aware < 1f)
                    {
                        aware = Mathf.Min(1f, aware + SpotRate(other, distance, angle, alert) * dt);
                        awareness[other] = aware;
                    }
                    if (aware < 1f)
                    {
                        if (aware > suspicion)
                        {
                            suspicion = aware;
                            suspicious = other.transform.position;
                        }
                        continue;
                    }

                    visible++;
                    float score = TargetScore(other, distance);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = other;
                    }
                }
                else
                {
                    if (aware > 0f) awareness[other] = Mathf.Max(0f, aware - awarenessDecay * dt);
                    HearFootsteps(other);
                }
            }
            PruneAwareness();

            Suspicion = suspicion;
            SuspiciousPosition = suspicious;
            VisibleEnemyCount = visible;
            if (best != Target) TargetVisibleSince = Time.time;
            SetTarget(best);
            if (best != null) RecordSighting(best);
        }

        float SpotRate(TeamMember other, float distance, float angle, bool alert)
        {
            float rate = spotRate * Mathf.Lerp(1f, farSpotFactor, distance / sightRange);
            if (angle > focusAngle) rate *= peripheralFactor;
            SoldierMotor motor = other.Motor;
            if (motor != null)
            {
                if (motor.IsCrouched || motor.IsCoverLow) rate *= crouchedFactor;
                else if (motor.IsSprinting) rate *= sprintingFactor;
                else if (motor.Velocity.sqrMagnitude < 0.04f) rate *= 0.8f;
            }
            if (Time.time - other.LastShotTime < 2f) rate *= firingFactor;
            if (alert) rate *= alertFactor;
            if (other == attacker && Time.time - attackerTime < attackerMemory) rate *= 10f;
            return rate;
        }

        float TargetScore(TeamMember other, float distance)
        {
            float score = Mathf.Max(distance, 1f);
            if (other == attacker && Time.time - attackerTime < attackerMemory) score *= attackerPreference;
            if (IsAimingAtMe(other)) score *= aimingAtMePreference;
            if (other.Health != null && other.Health.Current < other.Health.MaxHealth * 0.35f) score *= woundedPreference;
            int crowd = other.TargetedBy - (other == Target ? 1 : 0);
            for (int c = 0; c < crowd && c < 4; c++) score *= crowdedPenalty;
            if (other == Target) score *= keepTargetPreference;
            return score;
        }

        bool IsAimingAtMe(TeamMember other)
        {
            Vector3 toMe = transform.position - other.transform.position;
            toMe.y = 0f;
            Vector3 facing = other.transform.forward;
            facing.y = 0f;
            return toMe.sqrMagnitude > 0.01f && Vector3.Angle(facing, toMe) < 15f;
        }

        void HearFootsteps(TeamMember other)
        {
            SoldierMotor motor = other.Motor;
            if (motor == null || motor.IsCrouched || motor.Velocity.sqrMagnitude < 0.5f) return;
            float range = motor.IsSprinting ? sprintHearing : walkHearing;
            Vector3 position = other.transform.position;
            if ((position - transform.position).sqrMagnitude > range * range) return;
            LastHeardPosition = position;
            LastHeardTime = Time.time;
            float aware = AwarenessOf(other);
            if (aware < suspicionThreshold) awareness[other] = suspicionThreshold;
        }

        void SetTarget(TeamMember target)
        {
            if (target == Target) return;
            if (Target != null) Target.TargetedBy = Mathf.Max(0, Target.TargetedBy - 1);
            if (target != null) target.TargetedBy++;
            Target = target;
        }

        void RecordSighting(TeamMember best)
        {
            Vector3 position = best.transform.position;
            if (best == LastSeenTarget && Time.time - previousSeenTime < 1f)
            {
                Vector3 velocity = (position - previousSeenPosition) / Mathf.Max(Time.time - previousSeenTime, 0.01f);
                velocity.y = 0f;
                LastSeenVelocity = Vector3.Lerp(LastSeenVelocity, Vector3.ClampMagnitude(velocity, 6f), 0.5f);
            }
            else
            {
                LastSeenVelocity = Vector3.zero;
            }
            previousSeenPosition = position;
            previousSeenTime = Time.time;
            LastSeenTarget = best;
            LastSeenPosition = position;
            LastSeenTime = Time.time;
        }

        /// <summary>Forget dead or removed enemies.</summary>
        void PruneAwareness()
        {
            awarenessKeys.Clear();
            foreach (var pair in awareness)
                if (pair.Key == null || !pair.Key.IsAlive) awarenessKeys.Add(pair.Key);
            for (int i = 0; i < awarenessKeys.Count; i++) awareness.Remove(awarenessKeys[i]);
        }

        bool CanSee(TeamMember other, out float distance, out float angle)
        {
            Vector3 eye = self.Head.position;
            Vector3 toChest = other.Chest.position - eye;
            distance = toChest.magnitude;
            angle = 180f;
            if (distance > sightRange) return false;

            Vector3 flat = new Vector3(toChest.x, 0f, toChest.z);
            angle = Vector3.Angle(transform.forward, flat);
            if (angle > fieldOfView * 0.5f) return false;

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

        /// <summary>A live grenade within its blast radius (plus a margin) is a danger, whoever threw it.</summary>
        void ScanGrenades()
        {
            Grenade nearest = null;
            float nearestDistance = float.MaxValue;
            var grenades = Grenade.Live;
            for (int i = 0; i < grenades.Count; i++)
            {
                Grenade g = grenades[i];
                if (g == null) continue;
                float d = Vector3.Distance(g.Position, transform.position);
                if (d < g.Radius + 1.5f && d < nearestDistance)
                {
                    nearest = g;
                    nearestDistance = d;
                }
            }
            DangerGrenade = nearest;
        }

        void OnGunshot(SoldierWeapon weapon)
        {
            if (weapon.transform == transform) return;
            if ((weapon.transform.position - transform.position).sqrMagnitude > hearingRange * hearingRange) return;
            LastHeardPosition = weapon.transform.position;
            LastHeardTime = Time.time;
            // A shot from an enemy in view gives it away.
            if (weapon.TryGetComponent(out TeamMember shooter) && self.IsEnemyOf(shooter) && AwarenessOf(shooter) > 0f)
                awareness[shooter] = Mathf.Max(AwarenessOf(shooter), 0.7f);
        }

        void OnExplosion(Vector3 position, GameObject thrower)
        {
            float distance = Vector3.Distance(position, transform.position);
            if (distance > explosionHearingRange) return;
            LastHeardPosition = position;
            LastHeardTime = Time.time;
            if (distance < 10f && thrower != null && thrower.TryGetComponent(out TeamMember enemy) && self.IsEnemyOf(enemy))
            {
                LastUnderFireTime = Time.time;
                LastThreatPosition = enemy.transform.position;
                AddSuppression(blastSuppression * (1f - distance / 10f));
            }
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
            AddSuppression(hitSuppression);
            awareness[shooter] = Mathf.Max(AwarenessOf(shooter), 0.8f);
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
            AddSuppression(nearMissSuppression);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Gizmos.color = Target != null ? Color.red : IsSuspicious ? new Color(1f, 0.6f, 0f) : Color.yellow;
            Vector3 left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * transform.forward;
            Gizmos.DrawLine(eye, eye + left * sightRange);
            Gizmos.DrawLine(eye, eye + right * sightRange);
            if (Target != null) Gizmos.DrawLine(eye, Target.Chest.position);
            else if (IsSuspicious) Gizmos.DrawLine(eye, SuspiciousPosition + Vector3.up * 1.2f);
        }
    }
}
