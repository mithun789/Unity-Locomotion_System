using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// ISoldierInput for AI. Writes the same SoldierCommand the player does; movement goes through AIPathFollower.
    ///
    /// Calm: Patrol, and Investigate gunshots or squad reports.
    /// Fighting (enemy seen, or under fire, within combatMemory seconds): AIDecisionMaker scores the options
    /// (engage, take cover, advance, flank, suppress, retreat, search) from what the soldier knows and its
    /// AIPersonality, and the brain carries out the chosen intent with these states:
    /// Engage (shoot while strafing), TakeCover (AICover: nearest, advance or fall-back cover), Flank,
    /// Suppress (fire at the last known position), Retreat (no cover found), Investigate (search).
    /// With a Squad parent: shares sightings, one flanker at a time, mates suppress for a flanker or a
    /// reloading member, and morale drops as members die.
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
            Flank,
            Suppress,
            Retreat,
            /// <summary>Running from a live grenade.</summary>
            Evade,
            /// <summary>Standing to throw a grenade.</summary>
            ThrowGrenade,
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
        [Tooltip("Seconds from first sight to first shot (min, max), before personality.")]
        [SerializeField] Vector2 reactionDelay = new Vector2(0.3f, 0.6f);
        [SerializeField] Vector2Int burstShots = new Vector2Int(3, 6);
        [SerializeField] Vector2 burstPause = new Vector2(0.3f, 0.8f);
        [Tooltip("Seconds without sight before a new sighting needs the full reaction delay again.")]
        [SerializeField] float loseTargetTime = 1.5f;
        [Tooltip("Seconds between strafe direction changes while shooting in the open (min, max).")]
        [SerializeField] Vector2 strafeInterval = new Vector2(1.2f, 2.8f);
        [Tooltip("Shooting at targets farther than this, careful soldiers kneel.")]
        [SerializeField] float kneelDistance = 15f;

        [Header("Aim error (metres at the target)")]
        [SerializeField] float maxAimError = 1.2f;
        [SerializeField] float minAimError = 0.15f;
        [Tooltip("Seconds of continuous sight for the error to shrink from max to min.")]
        [SerializeField] float aimSettleTime = 2.5f;
        [SerializeField] float aimWobbleSpeed = 1.5f;

        [Header("Fighting")]
        [Tooltip("Stay in fight mode this long after the last sighting or shot at this soldier, in seconds.")]
        [SerializeField] float combatMemory = 10f;
        [Tooltip("Hit or near-missed within this many seconds counts as under fire.")]
        [SerializeField] float underFireWindow = 1.5f;
        [Tooltip("Squad mates within this distance count as nearby support, in metres.")]
        [SerializeField] float supportRadius = 25f;
        [Tooltip("After a failed cover search, wait this long before trying again, in seconds.")]
        [SerializeField] float coverRetryDelay = 2f;
        [Tooltip("Seconds behind advance cover before bounding forward again.")]
        [SerializeField] float advanceCooldown = 6f;
        [SerializeField] float flankCooldown = 12f;
        [Tooltip("Give up a flank after this many seconds.")]
        [SerializeField] float flankTimeout = 12f;
        [Tooltip("Longest walk to a flank spot, in metres.")]
        [SerializeField] float maxFlankPath = 35f;
        [SerializeField] float retreatCooldown = 10f;
        [SerializeField] float maxRetreatPath = 25f;
        [Tooltip("Scenery that blocks sight for flank and retreat spot checks.")]
        [SerializeField] LayerMask tacticsMask = ~0;

        [Header("Grenades")]
        [Tooltip("Seconds between grenades from this soldier.")]
        [SerializeField] float grenadeCooldown = 12f;
        [SerializeField] float minGrenadeDistance = 8f;
        [Tooltip("No grenade when a team mate is this close to the target, in metres.")]
        [SerializeField] float grenadeFriendlyRadius = 7f;
        [Tooltip("Run this far (path length, metres) at most to get away from a live grenade.")]
        [SerializeField] float maxEvadePath = 14f;

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
        AIPersonality personality;
        AIDecisionMaker decision;
        AICallouts callouts;
        SoldierGrenades grenades;

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

        // Fight bookkeeping.
        float combatStart;
        float nextApplyRetry;
        float coverFailTime = float.NegativeInfinity;
        float advanceFailTime = float.NegativeInfinity;
        float lastAdvanceTime = float.NegativeInfinity;
        float flankFailTime = float.NegativeInfinity;
        float lastFlankEnd = float.NegativeInfinity;
        float retreatFailTime = float.NegativeInfinity;
        float lastRetreatTime = float.NegativeInfinity;
        float searchingSighting = float.NegativeInfinity;
        float searchedSighting = float.NegativeInfinity;
        Vector3 movePoint;
        float moveDeadline;
        TeamMember reactingTo;
        float lastHadTargetTime = float.NegativeInfinity;
        int strafeDir;
        float nextStrafeChange;
        bool engageKneel;
        float lastGrenadeTime = float.NegativeInfinity;
        Vector3 grenadeTarget;
        bool grenadeIssued;
        float grenadeIssueTime;
        bool pendingCoverGrenade;
        float grenadeDeadline;
        Vector3 evadeFrom;

        public State CurrentState { get; private set; } = State.Patrol;
        public bool InCombat { get; private set; }
        public bool IsAlive => health == null || !health.IsDead;
        public AIIntent Intent => decision.Intent;
        public AIDecisionMaker Decision => decision;
        public AIPersonality Personality => personality;
        public Squad Squad => squad;
        /// <summary>Where the current flank or retreat is heading.</summary>
        public Vector3 MovePoint => movePoint;

        void Awake()
        {
            perception = GetComponent<AIPerception>();
            path = GetComponent<AIPathFollower>();
            weapon = GetComponent<SoldierWeapon>();
            health = GetComponent<Health>();
            self = GetComponent<TeamMember>();
            squad = GetComponentInParent<Squad>();
            cover = GetComponent<AICover>();
            // Older prefabs: add the thinking parts if the prefab builder has not been re-run.
            personality = GetOrAdd<AIPersonality>();
            decision = GetOrAdd<AIDecisionMaker>();
            GetOrAdd<AIDebugLabel>();
            callouts = GetOrAdd<AICallouts>();
            noiseSeed = Random.value * 100f;
            burstLeft = Random.Range(burstShots.x, burstShots.y + 1);
        }

        T GetOrAdd<T>() where T : Component => TryGetComponent(out T c) ? c : gameObject.AddComponent<T>();

        void OnEnable()
        {
            if (weapon != null)
            {
                weapon.Fired += OnFired;
                weapon.ReloadStarted += OnReloadStarted;
                weapon.HitTarget += OnHitTarget;
            }
            if (health != null) health.Died += OnDied;
            if (squad != null) squad.Register(this);
        }

        void OnDisable()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
                weapon.ReloadStarted -= OnReloadStarted;
                weapon.HitTarget -= OnHitTarget;
            }
            if (health != null) health.Died -= OnDied;
            if (squad != null) squad.Unregister(this);
        }

        void OnFired()
        {
            if (--burstLeft > 0) return;
            // Disciplined shooters fire shorter bursts with tighter pauses.
            int max = personality.Discipline > 0.7f ? Mathf.Max(burstShots.x, burstShots.y - 2) : burstShots.y;
            burstLeft = Random.Range(burstShots.x, max + 1);
            nextBurstTime = Time.time + Random.Range(burstPause.x, burstPause.y);
        }

        void OnReloadStarted()
        {
            if (squad != null) squad.CallReloading(this);
            if (InCombat) Say(Callout.Reloading);
        }

        /// <summary>
        /// Order from outside (mission spawner, scripted event): go and look at this spot. Ignored while
        /// fighting or dodging a grenade; the soldier's own perception takes over once it sees something.
        /// </summary>
        public void Alert(Vector3 position)
        {
            if (!IsAlive || InCombat || CurrentState == State.Evade) return;
            StartInvestigate(position);
        }

        /// <summary>Shout a callout (shown above the head).</summary>
        public void Say(Callout line, bool urgent = false)
        {
            if (callouts != null) callouts.Say(line, urgent);
        }

        /// <summary>Has at least one grenade left (for the debug label).</summary>
        public bool HasGrenades => grenades != null && grenades.Count > 0;

        void OnHitTarget(Health target, bool killed)
        {
            if (!killed || target == null || !target.TryGetComponent(out TeamMember member) || !self.IsEnemyOf(member)) return;
            if (squad != null) squad.OnEnemyKilled();
            Say(Callout.TargetDown);
        }

        void OnDied(DamageInfo damage)
        {
            DangerZones.AddDeath(self.Team, transform.position);
            SetState(State.Dead);
            if (squad != null) squad.OnMemberDied(this);
        }

        public SoldierCommand GetCommand()
        {
            UpdateState();
            switch (CurrentState)
            {
                case State.Engage: return Engage();
                case State.TakeCover: return InCover();
                case State.Flank: return Flank();
                case State.Suppress: return Suppress();
                case State.Retreat: return Retreat();
                case State.Evade: return Evade();
                case State.ThrowGrenade: return ThrowGrenade();
                case State.Investigate: return Investigate();
                case State.Patrol: return Patrol();
                default: return SoldierCommand.Idle(transform);
            }
        }

        // ---------------------------------------------------------------- transitions

        void UpdateState()
        {
            if (!IsAlive)
            {
                SetState(State.Dead);
                return;
            }

            if (grenades == null) TryGetComponent(out grenades);
            if (perception.Target != null && squad != null) squad.Report(perception.Target.transform.position);
            TrackReaction();
            if (UpdateEvade()) return;

            bool squadContact = InCombat && squad != null && Time.time - squad.LastReportTime < 3f;
            bool contact = perception.Target != null || Time.time - perception.LastContactTime < combatMemory || squadContact;
            if (contact)
            {
                if (!InCombat) EnterCombat();
                Think();
                return;
            }
            if (InCombat) LeaveCombat();

            // Calm: react to noise and squad reports.
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

        /// <summary>A fresh sighting needs the reaction delay before the first shot; a quick re-sighting less.</summary>
        void TrackReaction()
        {
            TeamMember target = perception.Target;
            if (target != null)
            {
                if (target != reactingTo)
                {
                    float delay = Random.Range(reactionDelay.x, reactionDelay.y) * Mathf.Lerp(1.3f, 0.75f, personality.Discipline);
                    if (Time.time - lastHadTargetTime < loseTargetTime) delay *= 0.4f;
                    fireAllowedTime = Mathf.Max(fireAllowedTime, Time.time + delay);
                    reactingTo = target;
                }
                lastHadTargetTime = Time.time;
            }
            else if (Time.time - lastHadTargetTime > loseTargetTime)
            {
                reactingTo = null;
            }
        }

        void EnterCombat()
        {
            InCombat = true;
            combatStart = Time.time;
            decision.BeginFight(AIIntent.Engage);
            if (CurrentState != State.TakeCover) SetState(State.Engage);
            if (perception.Target != null) Say(Callout.Contact, urgent: true);
        }

        void LeaveCombat()
        {
            InCombat = false;
            handledHeardTime = perception.LastHeardTime;
            if (squad != null) handledSquadTime = squad.LastReportTime;
            if (CurrentState == State.Investigate) return;
            if (KnownEnemyTime() > searchedSighting)
            {
                StartSearch();
                Say(Callout.LostHim);
            }
            else
            {
                SetState(State.Patrol);
                path.Stop();
            }
        }

        void Think()
        {
            AIIntent intent = decision.Decide(BuildContext(), out bool changed);
            if (changed || (!StateMatches(intent) && Time.time >= nextApplyRetry)) ApplyIntent(intent);
            UpdateCombatState();
        }

        AIContext BuildContext()
        {
            Vector3 threat = ThreatPosition();
            float knownTime = KnownEnemyTime();
            bool knowsEnemy = !float.IsNegativeInfinity(knownTime);
            float now = Time.time;
            bool advancing = CurrentState == State.TakeCover && cover != null && cover.CurrentMode == CoverSearch.Advance
                             && now - lastAdvanceTime < advanceCooldown;

            return new AIContext
            {
                Health = health != null ? health.Current / Mathf.Max(1f, health.MaxHealth) : 1f,
                Ammo = weapon != null ? (float)weapon.Ammo / Mathf.Max(1, weapon.MagazineSize) : 1f,
                HasTarget = perception.Target != null,
                TargetDistance = FlatDistance(transform.position, threat),
                TimeSinceSeen = knowsEnemy ? now - knownTime : 999f,
                Suppression = perception.Suppression,
                UnderFire = perception.IsUnderFire(underFireWindow),
                VisibleEnemies = perception.VisibleEnemyCount,
                NearbyAllies = CountNearbyAllies(),
                Morale = squad != null ? squad.Morale : 1f,
                InCover = CurrentState == State.TakeCover && cover != null && cover.IsInPosition,
                CanUseCover = cover != null && now - coverFailTime > coverRetryDelay,
                CanFlank = knowsEnemy && (squad == null || squad.IsFlankFree(this))
                           && now - lastFlankEnd > flankCooldown && now - flankFailTime > flankCooldown * 0.5f,
                CanAdvance = knowsEnemy && !advancing && now - advanceFailTime > 4f,
                CanRetreat = now - lastRetreatTime > retreatCooldown && now - retreatFailTime > 3f,
                CanSearch = knowsEnemy && knownTime > searchedSighting,
                TeamNeedsCover = squad != null && squad.NeedsCoveringFire(this),
                CombatTime = now - combatStart,
                CanGrenade = CanThrowGrenadeAt(threat, out int enemiesNear),
                TargetHidden = perception.Target == null || IsInCoverOrLow(perception.Target),
                EnemiesNearTarget = enemiesNear,
            };
        }

        /// <summary>Is the current state already carrying out this intent?</summary>
        bool StateMatches(AIIntent intent)
        {
            switch (intent)
            {
                case AIIntent.Engage: return CurrentState == State.Engage;
                case AIIntent.TakeCover: return CurrentState == State.TakeCover;
                case AIIntent.Advance:
                    return CurrentState == State.TakeCover && cover.CurrentMode == CoverSearch.Advance
                           && Time.time - lastAdvanceTime < advanceCooldown;
                case AIIntent.Flank: return CurrentState == State.Flank;
                case AIIntent.Suppress:
                    return CurrentState == State.Suppress || (CurrentState == State.TakeCover && cover.IsInPosition);
                case AIIntent.Retreat:
                    return CurrentState == State.Retreat || (CurrentState == State.TakeCover && cover.CurrentMode == CoverSearch.Fallback);
                case AIIntent.Search: return CurrentState == State.Investigate;
                case AIIntent.Grenade: return CurrentState == State.ThrowGrenade || pendingCoverGrenade;
            }
            return false;
        }

        void ApplyIntent(AIIntent intent)
        {
            nextApplyRetry = Time.time + 0.5f;
            Vector3 threat = ThreatPosition();
            switch (intent)
            {
                case AIIntent.Engage:
                    SetState(State.Engage);
                    break;

                case AIIntent.TakeCover:
                    if (cover != null && cover.TryTakeCover(threat))
                    {
                        if (CurrentState != State.TakeCover && perception.IsUnderFire(underFireWindow)) Say(Callout.TakingCover);
                        SetState(State.TakeCover);
                    }
                    else Failed(ref coverFailTime);
                    break;

                case AIIntent.Advance:
                    if (cover != null && cover.TryTakeCover(threat, CoverSearch.Advance))
                    {
                        lastAdvanceTime = Time.time;
                        SetState(State.TakeCover);
                        Say(Callout.MovingUp);
                    }
                    else Failed(ref advanceFailTime);
                    break;

                case AIIntent.Retreat:
                    if (cover != null && cover.TryTakeCover(threat, CoverSearch.Fallback))
                    {
                        lastRetreatTime = Time.time;
                        SetState(State.TakeCover);
                        Say(Callout.FallingBack, urgent: true);
                    }
                    else if (AITactics.FindRetreatPosition(transform.position, threat, maxRetreatPath, tacticsMask, out Vector3 retreatSpot))
                    {
                        BeginMove(retreatSpot, 10f);
                        SetState(State.Retreat);
                        Say(Callout.FallingBack, urgent: true);
                    }
                    else Failed(ref retreatFailTime);
                    break;

                case AIIntent.Flank:
                {
                    int side = squad != null ? (transform.GetSiblingIndex() % 2 == 0 ? 1 : -1) : 0;
                    if ((squad == null || squad.TryClaimFlank(this))
                        && AITactics.FindFlankPosition(transform.position, threat, side, maxFlankPath, tacticsMask, out Vector3 flankSpot))
                    {
                        BeginMove(flankSpot, flankTimeout);
                        SetState(State.Flank);
                        Say(Callout.Flanking);
                    }
                    else
                    {
                        if (squad != null && CurrentState != State.Flank) squad.ReleaseFlank(this);
                        Failed(ref flankFailTime);
                    }
                    break;
                }

                case AIIntent.Suppress:
                    // In cover the peeks fire at the last known position; in the open, stand and fire.
                    if (!(CurrentState == State.TakeCover && cover.IsInPosition)) SetState(State.Suppress);
                    if (squad != null && squad.NeedsCoveringFire(this)) Say(Callout.CoveringFire);
                    break;

                case AIIntent.Grenade:
                    if (Time.time - lastGrenadeTime < grenadeCooldown || grenades == null || !grenades.CanThrow)
                    {
                        decision.ThinkNow();
                        break;
                    }
                    grenadeTarget = GrenadeAimPoint();
                    grenadeIssued = false;
                    if (CurrentState == State.TakeCover && cover.IsInPosition)
                    {
                        pendingCoverGrenade = true;   // thrown on the next peek
                        grenadeDeadline = Time.time + 6f;
                    }
                    else
                    {
                        grenadeDeadline = Time.time + 2.5f;
                        SetState(State.ThrowGrenade);
                    }
                    break;

                case AIIntent.Search:
                    StartSearch();
                    break;
            }
        }

        /// <summary>The intent could not be carried out: remember when, keep fighting, rethink soon.</summary>
        void Failed(ref float failTime)
        {
            failTime = Time.time;
            decision.ThinkNow();
            if (CurrentState == State.Patrol || CurrentState == State.Investigate) SetState(State.Engage);
        }

        /// <summary>Per-frame checks for the current fight state: arrivals, timeouts, being flanked.</summary>
        void UpdateCombatState()
        {
            switch (CurrentState)
            {
                case State.TakeCover:
                    UpdateCover();
                    break;

                case State.Flank:
                    if (path.IsAtDestination || Time.time > moveDeadline)
                    {
                        SetState(State.Engage);
                        decision.ThinkNow();
                    }
                    break;

                case State.Retreat:
                    if (path.IsAtDestination || Time.time > moveDeadline)
                    {
                        lastRetreatTime = Time.time;
                        SetState(State.Engage);
                        decision.ThinkNow();
                    }
                    break;
            }
        }

        void UpdateCover()
        {
            if (cover == null || !cover.HasCover)
            {
                SetState(State.Engage);
                decision.ThinkNow();
                return;
            }

            Vector3 threat = ThreatPosition();
            if (!cover.Current.Protects(threat) && !cover.TryTakeCover(threat))
            {
                // Flanked and nowhere better to go: fight from here.
                coverFailTime = Time.time;
                SetState(State.Engage);
                decision.ThinkNow();
                return;
            }

            // Pinned down: stay low longer; aggressive soldiers peek sooner and longer.
            float suppression = perception.Suppression;
            cover.HideScale = Mathf.Clamp(1f + 1.5f * suppression + 0.5f * personality.Caution - 0.4f * personality.Aggression, 0.6f, 3f);
            cover.PeekScale = Mathf.Clamp(Mathf.Lerp(0.8f, 1.3f, personality.Aggression) * (1f - 0.4f * suppression), 0.4f, 1.5f);
            if (suppression > 0.75f && cover.IsPeeking && perception.IsUnderFire(0.3f)) cover.Duck();
        }

        void SetState(State next)
        {
            if (next == CurrentState) return;
            State previous = CurrentState;
            if (previous == State.Flank)
            {
                lastFlankEnd = Time.time;
                if (squad != null) squad.ReleaseFlank(this);
            }
            if (previous == State.TakeCover && cover != null) cover.Leave();
            if (previous == State.TakeCover) pendingCoverGrenade = false;
            if (next == State.Engage || next == State.Suppress || next == State.Dead) path.Stop();
            CurrentState = next;
        }

        void BeginMove(Vector3 point, float timeout)
        {
            movePoint = point;
            moveDeadline = Time.time + timeout;
            path.SetDestination(point);
        }

        /// <summary>Best guess of where the enemy is: in sight, last shot from, last seen, or reported by the squad.</summary>
        Vector3 ThreatPosition()
        {
            if (perception.Target != null) return perception.Target.transform.position;
            if (perception.IsUnderFire(5f)) return perception.LastThreatPosition;
            return KnownEnemyPosition();
        }

        /// <summary>Freshest known enemy position: own sighting (predicted on), squad report, or last shot from.</summary>
        Vector3 KnownEnemyPosition()
        {
            bool ownSighting = perception.LastSeenTime >= perception.LastUnderFireTime;
            Vector3 own = ownSighting ? perception.PredictedTargetPosition(2f) : perception.LastThreatPosition;
            float ownTime = Mathf.Max(perception.LastSeenTime, perception.LastUnderFireTime);
            if (squad != null && squad.LastReportTime > ownTime) return squad.LastReportPosition;
            return own;
        }

        float KnownEnemyTime()
        {
            float time = Mathf.Max(perception.LastSeenTime, perception.LastUnderFireTime);
            return squad != null ? Mathf.Max(time, squad.LastReportTime) : time;
        }

        int CountNearbyAllies()
        {
            int count = 0;
            var members = TeamRegistry.Members;
            float range = supportRadius * supportRadius;
            for (int i = 0; i < members.Count; i++)
            {
                TeamMember other = members[i];
                if (other == self || !other.IsAlive || self.IsEnemyOf(other)) continue;
                if ((other.transform.position - transform.position).sqrMagnitude <= range) count++;
            }
            return count;
        }

        void StartSearch()
        {
            searchingSighting = KnownEnemyTime();
            StartInvestigate(KnownEnemyPosition());
        }

        void StartInvestigate(Vector3 point)
        {
            SetState(State.Investigate);
            // Squad mates spread out around the spot instead of all running to the same point.
            if (squad != null) point += squad.SlotOffset(transform, point - transform.position);
            investigatePoint = point;
            searchUntil = float.PositiveInfinity;
            path.SetDestination(point);
        }

        // ---------------------------------------------------------------- states

        SoldierCommand Patrol()
        {
            // Half-noticed something: stop and look at it (raising the rifle) until it is spotted or fades.
            if (perception.IsSuspicious)
            {
                Say(Callout.Suspicious);
                return new SoldierCommand { AimPoint = perception.SuspiciousPosition + Vector3.up * 1.3f, Aim = true };
            }

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
            if (!path.IsAtDestination && perception.IsSuspicious)
            {
                // Walk on carefully, rifle toward whatever was half seen.
                Vector3 suspect = perception.SuspiciousPosition + Vector3.up * 1.3f;
                return new SoldierCommand
                {
                    Move = SoldierCommand.WorldToMove(path.GetMoveDirection(), transform.position, suspect) * 0.6f,
                    AimPoint = suspect,
                    Aim = true,
                };
            }
            if (!path.IsAtDestination)
            {
                bool run = path.RemainingDistance > runDistance;
                SoldierCommand moving = LookAhead(path.GetMoveDirection(), run);
                moving.Aim = true;
                return moving;
            }

            // Arrived: sweep the view left and right, then go back to patrol (or let the fight logic decide).
            if (float.IsPositiveInfinity(searchUntil)) searchUntil = Time.time + searchTime;
            if (Time.time >= searchUntil)
            {
                searchedSighting = Mathf.Max(searchedSighting, searchingSighting);
                SetState(State.Patrol);
                path.Stop();
                decision.ThinkNow();
            }
            float sweep = Mathf.Sin(Time.time * 1.2f) * 70f;
            Vector3 look = Quaternion.Euler(0f, sweep, 0f) * FlatForwardTo(investigatePoint);
            return new SoldierCommand { AimPoint = EyePoint(look), Aim = true };
        }

        SoldierCommand InCover()
        {
            SoldierCommand command = cover.Tick(ThreatPosition(), out bool canShoot);
            TeamMember target = perception.Target;
            if (pendingCoverGrenade && Time.time > grenadeDeadline && !grenadeIssued)
            {
                pendingCoverGrenade = false;
                lastGrenadeTime = Time.time;   // could not get the throw off; try again later
                decision.ThinkNow();
            }
            if (pendingCoverGrenade && (canShoot || cover.Current.Height == CoverHeight.Low))
            {
                IssueGrenade(ref command);
                if (grenadeIssued && grenades != null && !grenades.IsThrowing && Time.time - grenadeIssueTime > 0.3f)
                {
                    pendingCoverGrenade = false;
                    decision.ThinkNow();
                }
                return command;
            }
            if (canShoot && target != null)
            {
                Vector3 aim = target.Chest.position + AimError(target.Chest.position);
                command.AimPoint = aim;
                command.Aim = true;
                command.Fire = CanFireAt(aim);
            }
            else if (canShoot && decision.Intent == AIIntent.Suppress)
            {
                Vector3 aim = SuppressPoint();
                command.AimPoint = aim;
                command.Aim = true;
                command.Fire = CanFireAt(aim);
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
                // Briefly lost: keep aiming where the target probably is, hold fire, top up the magazine.
                return new SoldierCommand
                {
                    AimPoint = KnownEnemyPosition() + Vector3.up * 1.3f,
                    Aim = true,
                    Crouch = engageKneel,
                    Reload = weapon != null && weapon.Ammo < weapon.MagazineSize / 2,
                };
            }

            Vector3 aim = target.Chest.position + AimError(target.Chest.position);
            Vector3 move = StrafeDirection(target.transform.position);
            return new SoldierCommand
            {
                Move = SoldierCommand.WorldToMove(move, transform.position, aim) * 0.7f,
                AimPoint = aim,
                Aim = true,
                Crouch = engageKneel,
                Fire = CanFireAt(aim),
                Reload = weapon != null && weapon.Ammo == 0,
            };
        }

        /// <summary>Fire at the last known position to keep the enemy's head down.</summary>
        SoldierCommand Suppress()
        {
            Vector3 aim = perception.Target != null
                ? perception.Target.Chest.position + AimError(perception.Target.Chest.position)
                : SuppressPoint();
            return new SoldierCommand
            {
                AimPoint = aim,
                Aim = true,
                Crouch = personality.Caution > 0.5f,
                Fire = CanFireAt(aim),
                Reload = weapon != null && weapon.Ammo == 0,
            };
        }

        SoldierCommand Flank()
        {
            Vector3 dir = path.GetMoveDirection();
            TeamMember target = perception.Target;
            // Sprint on the long leg; walk and shoot near the end or when the enemy shows up close.
            bool sprint = path.RemainingDistance > 6f && (target == null || FlatDistance(transform.position, target.transform.position) > 15f);
            if (sprint) return LookAhead(dir, true);

            Vector3 aim = target != null ? target.Chest.position + AimError(target.Chest.position) : KnownEnemyPosition() + Vector3.up * 1.3f;
            return new SoldierCommand
            {
                Move = SoldierCommand.WorldToMove(dir, transform.position, aim),
                AimPoint = aim,
                Aim = true,
                Fire = target != null && CanFireAt(aim),
            };
        }

        SoldierCommand Retreat()
        {
            Vector3 dir = path.GetMoveDirection();
            if (path.RemainingDistance > 3f) return LookAhead(dir, true);
            Vector3 look = ThreatPosition() + Vector3.up * 1.3f;
            return new SoldierCommand
            {
                Move = SoldierCommand.WorldToMove(dir, transform.position, look),
                AimPoint = look,
                Aim = true,
            };
        }

        SoldierCommand Evade()
        {
            Vector3 dir = path.GetMoveDirection();
            return LookAhead(dir, true);
        }

        /// <summary>Face the target spot, throw once facing it, then let the decision maker pick the next move.</summary>
        SoldierCommand ThrowGrenade()
        {
            Vector3 look = new Vector3(grenadeTarget.x, transform.position.y + 1.5f, grenadeTarget.z);
            var command = new SoldierCommand { AimPoint = look };
            IssueGrenade(ref command);

            bool done = grenadeIssued && grenades != null && !grenades.IsThrowing && Time.time - grenadeIssueTime > 0.3f;
            if (done || Time.time > grenadeDeadline)
            {
                if (!grenadeIssued) lastGrenadeTime = Time.time;
                SetState(State.Engage);
                decision.ThinkNow();
            }
            return command;
        }

        /// <summary>Sets Grenade on the command the first frame the body faces the target.</summary>
        void IssueGrenade(ref SoldierCommand command)
        {
            if (grenadeIssued || grenades == null || !grenades.CanThrow) return;
            Vector3 to = grenadeTarget - transform.position;
            to.y = 0f;
            // Low cover: the soldier faces the cover (toward the threat). High cover: the peek faces out.
            // Standing soldiers keep their feet planted up to the turn-in-place angle (60 deg), so allow 70.
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) > 70f) return;
            command.Grenade = true;
            command.GrenadeTarget = grenadeTarget;
            grenadeIssued = true;
            grenadeIssueTime = Time.time;
            lastGrenadeTime = Time.time;
            Say(Callout.GrenadeOut, urgent: true);
        }

        /// <summary>
        /// Running from live grenades comes before everything else. Returns true while evading.
        /// </summary>
        bool UpdateEvade()
        {
            Grenade danger = perception.DangerGrenade;
            if (CurrentState == State.Evade)
            {
                // Keep running (or hold at the spot) until the grenade is gone or out of range.
                if (danger != null && Time.time < moveDeadline) return true;
                if (InCombat) SetState(State.Engage);
                else StartInvestigate(evadeFrom);
                decision.ThinkNow();
                return false;
            }
            if (danger == null) return false;

            Vector3 from = danger.Position;
            evadeFrom = danger.ThrowerTeam != null ? danger.ThrowerTeam.transform.position : from;
            if (!AITactics.FindRetreatPosition(transform.position, from, maxEvadePath, tacticsMask, out Vector3 spot))
            {
                Vector3 away = transform.position - from;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -transform.forward;
                Vector3 candidate = transform.position + away.normalized * (danger.Radius + 2f);
                spot = NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : candidate;
            }
            SetState(State.Evade);
            BeginMove(spot, Mathf.Max(1.5f, danger.TimeLeft + 0.5f));
            Say(Callout.Grenade, urgent: true);
            return true;
        }

        /// <summary>Where to land a grenade: the visible target, else the freshest known enemy position.</summary>
        Vector3 GrenadeAimPoint() =>
            perception.Target != null ? perception.Target.transform.position : KnownEnemyPosition();

        /// <summary>
        /// A grenade is possible: one in hand, off cooldown, target in range and not next to a team mate.
        /// Also counts enemies near the target spot (a group is a better target).
        /// </summary>
        bool CanThrowGrenadeAt(Vector3 threat, out int enemiesNear)
        {
            enemiesNear = 0;
            if (grenades == null || !grenades.CanThrow || Time.time - lastGrenadeTime < grenadeCooldown) return false;
            Vector3 point = GrenadeAimPoint();
            float distance = FlatDistance(transform.position, point);
            if (distance < minGrenadeDistance || distance > grenades.MaxRange) return false;

            var members = TeamRegistry.Members;
            float friendly = grenadeFriendlyRadius * grenadeFriendlyRadius;
            for (int i = 0; i < members.Count; i++)
            {
                TeamMember other = members[i];
                if (other == self || !other.IsAlive) continue;
                float d = (other.transform.position - point).sqrMagnitude;
                if (!self.IsEnemyOf(other))
                {
                    if (d < friendly) return false;
                }
                else if (d < 16f && perception.AwarenessOf(other) > 0f)
                {
                    enemiesNear++;
                }
            }
            return true;
        }

        static bool IsInCoverOrLow(TeamMember member)
        {
            SoldierMotor motor = member.Motor;
            return motor != null && (motor.IsInCover || motor.IsCrouched);
        }

        // ---------------------------------------------------------------- helpers

        bool CanFireAt(Vector3 aim) =>
            Time.time >= fireAllowedTime && Time.time >= nextBurstTime && IsFacing(aim) && LineOfFireClear(aim);

        /// <summary>Last known enemy position at chest height, with a wide wobble.</summary>
        Vector3 SuppressPoint()
        {
            Vector3 point = KnownEnemyPosition() + Vector3.up * 1.2f;
            return point + AimError(point) * 1.5f;
        }

        /// <summary>
        /// Sidestep while shooting in the open so the soldier is harder to hit. Aggressive soldiers move more,
        /// careful ones kneel at range. Flips when the NavMesh ends on that side.
        /// </summary>
        Vector3 StrafeDirection(Vector3 targetPosition)
        {
            if (Time.time >= nextStrafeChange)
            {
                nextStrafeChange = Time.time + Random.Range(strafeInterval.x, strafeInterval.y);
                float still = Mathf.Lerp(0.6f, 0.15f, personality.Aggression);
                strafeDir = Random.value < still ? 0 : (Random.value < 0.5f ? -1 : 1);
                engageKneel = FlatDistance(transform.position, targetPosition) > kneelDistance && Random.value < personality.Caution;
                if (engageKneel) strafeDir = 0;
            }
            if (strafeDir == 0) return Vector3.zero;

            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return Vector3.zero;
            Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
            Vector3 dir = right * strafeDir;
            if (NavMesh.Raycast(transform.position, transform.position + dir * 1.2f, out _, NavMesh.AllAreas))
            {
                strafeDir = -strafeDir;
                dir = -dir;
            }
            return dir;
        }

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

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>False when the first thing along the shot is a teammate.</summary>
        bool LineOfFireClear(Vector3 aimPoint)
        {
            if (weapon == null || weapon.Muzzle == null || self == null) return true;
            Vector3 origin = weapon.Muzzle.position;
            Vector3 dir = aimPoint - origin;
            float distance = dir.magnitude;
            if (distance < 0.01f) return true;
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

        /// <summary>
        /// Smooth wobble around the target that shrinks while the target stays in sight. Disciplined soldiers
        /// aim tighter; suppressed soldiers aim worse.
        /// </summary>
        Vector3 AimError(Vector3 targetPoint)
        {
            float settle = Mathf.Clamp01((Time.time - perception.TargetVisibleSince) / aimSettleTime);
            float radius = Mathf.Lerp(maxAimError, minAimError, settle)
                           * Mathf.Lerp(1.3f, 0.75f, personality.Discipline)
                           * (1f + perception.Suppression);
            float t = Time.time * aimWobbleSpeed;
            float x = (Mathf.PerlinNoise(t, noiseSeed) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(noiseSeed, t) - 0.5f) * 2f;

            Vector3 toTarget = (targetPoint - transform.position).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, toTarget).normalized;
            return (right * x + Vector3.up * y) * radius;
        }
    }
}
