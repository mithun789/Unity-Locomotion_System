using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// ISoldierInput for AI. States: Patrol, Investigate, Engage, TakeCover, Dead.
    /// Writes the same SoldierCommand the player does; movement goes through AIPathFollower.
    /// With a Squad parent: walks the route in formation, reports sightings and answers squad reports.
    /// With AICover: under fire it moves to cover, then hides and peeks to shoot.
    /// </summary>
    [RequireComponent(typeof(AIPerception), typeof(AIPathFollower))]
    public class AISoldierBrain : MonoBehaviour, ISoldierInput
    {
        public enum State
        {
            Patrol,
            Investigate,
            Engage,
            TakeCover,
            Dead,
        }

        [Header("Patrol")]
        [SerializeField] PatrolRoute route;
        [SerializeField] Vector2 waitAtPoint = new Vector2(1f, 2.5f);

        [Header("Investigate")]
        [Tooltip("Seconds spent looking around after reaching the investigated spot.")]
        [SerializeField] float searchTime = 4f;
        [Tooltip("Run to spots farther than this, in metres.")]
        [SerializeField] float runDistance = 8f;

        [Header("Engage")]
        [Tooltip("Seconds from first sight to first shot (min, max).")]
        [SerializeField] Vector2 reactionDelay = new Vector2(0.3f, 0.6f);
        [SerializeField] Vector2Int burstShots = new Vector2Int(3, 6);
        [SerializeField] Vector2 burstPause = new Vector2(0.3f, 0.8f);
        [Tooltip("Seconds without sight before going to look where the target was last seen.")]
        [SerializeField] float loseTargetTime = 1.5f;

        [Header("Aim error (metres at the target)")]
        [SerializeField] float maxAimError = 1.2f;
        [SerializeField] float minAimError = 0.15f;
        [Tooltip("Seconds of continuous sight for the error to shrink from max to min.")]
        [SerializeField] float aimSettleTime = 2.5f;
        [SerializeField] float aimWobbleSpeed = 1.5f;

        [Header("Cover")]
        [Tooltip("Hit or near-missed within this many seconds counts as under fire.")]
        [SerializeField] float underFireWindow = 1.5f;
        [Tooltip("Seconds between cover searches while under fire in the open.")]
        [SerializeField] float coverSearchInterval = 1f;
        [Tooltip("Leave cover after this long without seeing or being shot at, in seconds.")]
        [SerializeField] float coverGiveUpTime = 8f;

        [Header("Friendly fire")]
        [SerializeField] LayerMask lineOfFireMask = ~0;

        readonly RaycastHit[] fireHits = new RaycastHit[16];
        AIPerception perception;
        AIPathFollower path;
        SoldierWeapon weapon;
        Health health;
        TeamMember self;
        Squad squad;
        AICover cover;
        float nextCoverSearch;

        int patrolIndex;
        float waitUntil;
        Vector3 investigatePoint;
        float searchUntil;
        float handledHeardTime = float.NegativeInfinity;
        float handledSquadTime = float.NegativeInfinity;
        float fireAllowedTime;
        float nextBurstTime;
        int burstLeft;
        float noiseSeed;

        public State CurrentState { get; private set; } = State.Patrol;

        void Awake()
        {
            perception = GetComponent<AIPerception>();
            path = GetComponent<AIPathFollower>();
            weapon = GetComponent<SoldierWeapon>();
            health = GetComponent<Health>();
            self = GetComponent<TeamMember>();
            squad = GetComponentInParent<Squad>();
            cover = GetComponent<AICover>();
            noiseSeed = Random.value * 100f;
            burstLeft = Random.Range(burstShots.x, burstShots.y + 1);
        }

        void OnEnable()
        {
            if (weapon != null) weapon.Fired += OnFired;
        }

        void OnDisable()
        {
            if (weapon != null) weapon.Fired -= OnFired;
        }

        void OnFired()
        {
            if (--burstLeft > 0) return;
            burstLeft = Random.Range(burstShots.x, burstShots.y + 1);
            nextBurstTime = Time.time + Random.Range(burstPause.x, burstPause.y);
        }

        public SoldierCommand GetCommand()
        {
            UpdateState();
            switch (CurrentState)
            {
                case State.Engage: return Engage();
                case State.TakeCover: return InCover();
                case State.Investigate: return Investigate();
                case State.Patrol: return Patrol();
                default: return SoldierCommand.Idle(transform);
            }
        }

        // ---------------------------------------------------------------- transitions

        void UpdateState()
        {
            if (health != null && health.IsDead)
            {
                CurrentState = State.Dead;
                return;
            }

            if (perception.Target != null && squad != null) squad.Report(perception.Target.transform.position);

            if (CurrentState == State.TakeCover && StayInCover()) return;

            // Under fire in the open: get to cover.
            if (cover != null && CurrentState != State.TakeCover && perception.IsUnderFire(underFireWindow)
                && Time.time >= nextCoverSearch)
            {
                nextCoverSearch = Time.time + coverSearchInterval;
                if (cover.TryTakeCover(ThreatPosition()))
                {
                    CurrentState = State.TakeCover;
                    fireAllowedTime = Time.time + Random.Range(reactionDelay.x, reactionDelay.y);
                    return;
                }
            }

            if (perception.Target != null)
            {
                if (CurrentState != State.Engage)
                {
                    CurrentState = State.Engage;
                    path.Stop();
                    fireAllowedTime = Time.time + Random.Range(reactionDelay.x, reactionDelay.y);
                }
                return;
            }

            if (CurrentState == State.Engage)
            {
                if (Time.time - perception.LastSeenTime > loseTargetTime) StartInvestigate(perception.LastSeenPosition);
                return;
            }

            if (perception.LastHeardTime > handledHeardTime)
            {
                handledHeardTime = perception.LastHeardTime;
                StartInvestigate(perception.LastHeardPosition);
            }
            else if (squad != null && squad.LastReportTime > handledSquadTime)
            {
                handledSquadTime = squad.LastReportTime;
                StartInvestigate(squad.LastReportPosition);
            }
        }

        /// <summary>Keep using cover; false when it no longer makes sense (state has been changed).</summary>
        bool StayInCover()
        {
            if (cover == null || !cover.HasCover)
            {
                CurrentState = State.Engage;
                return false;
            }

            Vector3 threat = ThreatPosition();
            if (!cover.Current.Protects(threat) && !cover.TryTakeCover(threat))
            {
                // Flanked and nowhere better to go: fight from here.
                cover.Leave();
                CurrentState = State.Engage;
                return false;
            }

            bool quiet = perception.Target == null && Time.time - perception.LastSeenTime > coverGiveUpTime
                         && !perception.IsUnderFire(coverGiveUpTime * 0.5f);
            if (quiet)
            {
                cover.Leave();
                StartInvestigate(perception.LastSeenPosition);
            }
            return true;
        }

        /// <summary>Best guess of where the enemy is: in sight, last shot from, or last seen.</summary>
        Vector3 ThreatPosition()
        {
            if (perception.Target != null) return perception.Target.transform.position;
            if (perception.IsUnderFire(5f)) return perception.LastThreatPosition;
            return perception.LastSeenTime > perception.LastUnderFireTime ? perception.LastSeenPosition : perception.LastThreatPosition;
        }

        void StartInvestigate(Vector3 point)
        {
            CurrentState = State.Investigate;
            // Squad mates spread out around the spot instead of all running to the same point.
            if (squad != null) point += squad.SlotOffset(transform, point - transform.position);
            investigatePoint = point;
            searchUntil = float.PositiveInfinity;
            path.SetDestination(point);
        }

        // ---------------------------------------------------------------- states

        SoldierCommand Patrol()
        {
            if (route == null || route.Count == 0)
            {
                path.Stop();
                return SoldierCommand.Idle(transform);
            }

            Vector3 point = route.GetPoint(patrolIndex);
            if (squad != null)
                point += squad.SlotOffset(transform, point - route.GetPoint(patrolIndex + route.Count - 1));
            path.SetDestination(point);
            if (path.IsAtDestination)
            {
                if (waitUntil <= 0f) waitUntil = Time.time + Random.Range(waitAtPoint.x, waitAtPoint.y);
                if (Time.time >= waitUntil)
                {
                    waitUntil = 0f;
                    patrolIndex = (patrolIndex + 1) % route.Count;
                }
                return LookAhead(Vector3.zero, false);
            }
            return LookAhead(path.GetMoveDirection(), false);
        }

        SoldierCommand Investigate()
        {
            if (!path.IsAtDestination)
            {
                bool run = path.RemainingDistance > runDistance;
                SoldierCommand moving = LookAhead(path.GetMoveDirection(), run);
                moving.Aim = true;
                return moving;
            }

            // Arrived: sweep the view left and right, then go back to patrol.
            if (float.IsPositiveInfinity(searchUntil)) searchUntil = Time.time + searchTime;
            if (Time.time >= searchUntil)
            {
                CurrentState = State.Patrol;
                path.Stop();
            }
            float sweep = Mathf.Sin(Time.time * 1.2f) * 70f;
            Vector3 look = Quaternion.Euler(0f, sweep, 0f) * FlatForwardTo(investigatePoint);
            return new SoldierCommand { AimPoint = EyePoint(look), Aim = true };
        }

        SoldierCommand InCover()
        {
            SoldierCommand command = cover.Tick(ThreatPosition(), out bool canShoot);
            TeamMember target = perception.Target;
            if (canShoot && target != null)
            {
                Vector3 aim = target.Chest.position + AimError(target.Chest.position);
                command.AimPoint = aim;
                command.Aim = true;
                command.Fire = Time.time >= fireAllowedTime && Time.time >= nextBurstTime && IsFacing(aim) && LineOfFireClear(aim);
            }
            // Reload while tucked away rather than while exposed.
            command.Reload = weapon != null && weapon.Ammo < weapon.MagazineSize / 3 && !canShoot;
            return command;
        }

        SoldierCommand Engage()
        {
            TeamMember target = perception.Target;
            if (target == null)
            {
                // Briefly lost: keep aiming where the target was, hold fire.
                return new SoldierCommand { AimPoint = perception.LastSeenPosition + Vector3.up * 1.3f, Aim = true };
            }

            Vector3 aim = target.Chest.position + AimError(target.Chest.position);
            bool canFire = Time.time >= fireAllowedTime && Time.time >= nextBurstTime && IsFacing(aim) && LineOfFireClear(aim);
            return new SoldierCommand
            {
                AimPoint = aim,
                Aim = true,
                Fire = canFire,
                Reload = weapon != null && weapon.Ammo == 0,
            };
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Walk (or run) along moveDirection while looking the same way.</summary>
        SoldierCommand LookAhead(Vector3 moveDirection, bool run)
        {
            Vector3 look = moveDirection.sqrMagnitude > 0.0001f ? moveDirection : transform.forward;
            Vector3 aimPoint = EyePoint(look);
            return new SoldierCommand
            {
                Move = SoldierCommand.WorldToMove(moveDirection, transform.position, aimPoint),
                AimPoint = aimPoint,
                Sprint = run && moveDirection.sqrMagnitude > 0.0001f,
            };
        }

        Vector3 EyePoint(Vector3 direction) => transform.position + Vector3.up * 1.5f + direction.normalized * 10f;

        Vector3 FlatForwardTo(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        }

        /// <summary>False when the first thing along the shot is a teammate.</summary>
        bool LineOfFireClear(Vector3 aimPoint)
        {
            if (weapon == null || weapon.Muzzle == null || self == null) return true;
            Vector3 origin = weapon.Muzzle.position;
            Vector3 dir = aimPoint - origin;
            float distance = dir.magnitude;
            int count = Physics.RaycastNonAlloc(origin, dir / distance, fireHits, distance, lineOfFireMask, QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            Collider first = null;
            for (int i = 0; i < count; i++)
            {
                if (fireHits[i].distance < closest && !fireHits[i].transform.IsChildOf(transform))
                {
                    closest = fireHits[i].distance;
                    first = fireHits[i].collider;
                }
            }
            if (first == null) return true;
            var member = first.GetComponentInParent<TeamMember>();
            return member == null || !member.IsAlive || self.IsEnemyOf(member);
        }

        /// <summary>
        /// Only shoot once the rifle points at the target. Standing still the feet stay planted and the spine
        /// aims, so the body itself may be up to the turn-in-place angle off.
        /// </summary>
        bool IsFacing(Vector3 point)
        {
            if (weapon != null && weapon.Muzzle != null)
            {
                Vector3 toTarget = point - weapon.Muzzle.position;
                return Vector3.Angle(weapon.Muzzle.forward, toTarget) < 10f;
            }
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            return Vector3.Angle(transform.forward, dir) < 20f;
        }

        /// <summary>Smooth wobble around the target that shrinks while the target stays in sight.</summary>
        Vector3 AimError(Vector3 targetPoint)
        {
            float settle = Mathf.Clamp01((Time.time - perception.TargetVisibleSince) / aimSettleTime);
            float radius = Mathf.Lerp(maxAimError, minAimError, settle);
            float t = Time.time * aimWobbleSpeed;
            float x = (Mathf.PerlinNoise(t, noiseSeed) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(noiseSeed, t) - 0.5f) * 2f;

            Vector3 toTarget = (targetPoint - transform.position).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, toTarget).normalized;
            return (right * x + Vector3.up * y) * radius;
        }
    }
}
