using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Cover behaviour for AI: picks the best free CoverPoint that blocks the threat, runs to it, then cycles
    /// hide (back against the cover: crouched behind low cover, standing at a high corner) and peek
    /// (stand up and turn / step out past the corner) phases.
    /// The brain supplies the aim and decides when to fire during a peek.
    /// </summary>
    [RequireComponent(typeof(AIPathFollower))]
    public class AICover : MonoBehaviour
    {
        enum Phase
        {
            None,
            Moving,
            Hiding,
            Peeking,
        }

        [Header("Search")]
        [SerializeField] float searchRadius = 18f;
        [Tooltip("Ignore cover this close to the threat, in metres.")]
        [SerializeField] float minThreatDistance = 5f;
        [SerializeField] LayerMask blockMask = ~0;

        [Header("In cover (seconds)")]
        [SerializeField] Vector2 hideTime = new Vector2(1.0f, 2.2f);
        [SerializeField] Vector2 peekTime = new Vector2(1.4f, 2.6f);
        [Tooltip("Time to stand up / step out before shooting.")]
        [SerializeField] float peekSettle = 0.35f;
        [Tooltip("How far a soldier steps out past a high cover corner, in metres.")]
        [SerializeField] float stepOut = 0.9f;
        [Tooltip("Run to cover farther than this, in metres.")]
        [SerializeField] float sprintDistance = 3f;

        readonly RaycastHit[] hits = new RaycastHit[16];
        AIPathFollower path;
        Health health;
        Phase phase;
        float phaseEnd;
        float peekStart;

        public CoverPoint Current { get; private set; }
        public bool HasCover => Current != null;
        public bool IsInPosition => phase == Phase.Hiding || phase == Phase.Peeking;

        void Awake()
        {
            path = GetComponent<AIPathFollower>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (health != null) health.Died += OnDied;
        }

        void OnDisable()
        {
            if (health != null) health.Died -= OnDied;
            Leave();
        }

        void OnDied(DamageInfo damage) => Leave();

        /// <summary>Finds and claims the best cover against the threat. False when none is suitable.</summary>
        public bool TryTakeCover(Vector3 threat)
        {
            CoverPoint best = null;
            float bestCost = float.MaxValue;
            Vector3 me = transform.position;
            float myThreatDistance = FlatDistance(me, threat);

            var points = CoverPoint.All;
            for (int i = 0; i < points.Count; i++)
            {
                CoverPoint cp = points[i];
                if (!cp.IsFreeFor(this) || !cp.Protects(threat)) continue;
                float distance = FlatDistance(me, cp.Position);
                if (distance > searchRadius) continue;
                float threatDistance = FlatDistance(cp.Position, threat);
                if (threatDistance < minThreatDistance || !BlocksThreat(cp, threat)) continue;

                // Prefer close cover; avoid running toward the threat to reach it.
                float cost = distance + Mathf.Max(0f, myThreatDistance - threatDistance) * 0.5f;
                if (cp == Current) cost -= 2f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = cp;
                }
            }
            if (best == null) return false;
            if (best == Current) return true;

            Leave();
            best.TryClaim(this);
            Current = best;
            phase = Phase.Moving;
            path.SetDestination(best.Position);
            return true;
        }

        public void Leave()
        {
            if (Current != null) Current.Release(this);
            Current = null;
            phase = Phase.None;
        }

        /// <summary>
        /// Movement and stance for this frame. canShoot is true once the soldier has stood up / stepped out.
        /// AimPoint looks toward the threat; the brain replaces it with the real target when shooting.
        /// </summary>
        public SoldierCommand Tick(Vector3 threat, out bool canShoot)
        {
            canShoot = false;
            if (Current == null) return SoldierCommand.Idle(transform);

            Vector3 look = EyePoint(threat);
            switch (phase)
            {
                case Phase.Moving:
                {
                    path.SetDestination(Current.Position);
                    if (path.IsAtDestination)
                    {
                        StartHiding();
                        return InCoverPose(look);
                    }
                    Vector3 dir = path.GetMoveDirection();
                    Vector3 aimAhead = dir.sqrMagnitude > 0.0001f ? transform.position + Vector3.up * 1.5f + dir * 10f : look;
                    return new SoldierCommand
                    {
                        Move = SoldierCommand.WorldToMove(dir, transform.position, aimAhead),
                        AimPoint = aimAhead,
                        Sprint = path.RemainingDistance > sprintDistance,
                    };
                }

                case Phase.Hiding:
                    if (Time.time >= phaseEnd)
                    {
                        phase = Phase.Peeking;
                        peekStart = Time.time;
                        phaseEnd = Time.time + Random.Range(peekTime.x, peekTime.y);
                    }
                    return InCoverPose(look);

                case Phase.Peeking:
                {
                    if (Time.time >= phaseEnd) StartHiding();
                    if (Current.Height == CoverHeight.High)
                    {
                        Vector3 peek = Current.PeekPosition(stepOut);
                        canShoot = FlatDistance(transform.position, peek) < 0.25f && Time.time - peekStart > peekSettle;
                        return MoveTo(peek, look, aim: true);
                    }
                    canShoot = Time.time - peekStart > peekSettle;
                    SoldierCommand stand = Hold(look, crouch: false);
                    stand.Aim = true;
                    return stand;
                }
            }
            return Hold(look, crouch: false);
        }

        void StartHiding()
        {
            phase = Phase.Hiding;
            phaseEnd = Time.time + Random.Range(hideTime.x, hideTime.y);
        }

        SoldierCommand Hold(Vector3 look, bool crouch) => new SoldierCommand { AimPoint = look, Crouch = crouch };

        /// <summary>Back against the cover: crouched behind low cover, standing at a high corner.</summary>
        SoldierCommand InCoverPose(Vector3 look)
        {
            // Slide back along the wall to the point (after stepping out past a corner).
            Vector3 to = Current.Position - transform.position;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized * Mathf.Clamp01(to.magnitude * 2f) : Vector3.zero;
            return new SoldierCommand
            {
                Move = SoldierCommand.WorldToMove(dir, transform.position, look),
                AimPoint = look,
                InCover = true,
                CoverLow = Current.Height == CoverHeight.Low,
                CoverForward = Current.Forward,
                CoverAnchor = Current.Position,
                // Look out toward the corner it peeks around (PeekSide +1 is the soldier's left).
                CoverFaceLeft = Current.PeekSide > 0,
            };
        }

        /// <summary>Short sidestep (peek out / back) while facing the threat.</summary>
        SoldierCommand MoveTo(Vector3 point, Vector3 look, bool aim)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.zero;
            return new SoldierCommand
            {
                Move = SoldierCommand.WorldToMove(dir, transform.position, look) * Mathf.Clamp01(to.magnitude * 2f),
                AimPoint = look,
                Aim = aim,
            };
        }

        Vector3 EyePoint(Vector3 threat) => new Vector3(threat.x, transform.position.y + 1.3f, threat.z);

        /// <summary>Scenery between the hiding head and the threat's chest.</summary>
        bool BlocksThreat(CoverPoint cp, Vector3 threat)
        {
            Vector3 eye = cp.Position + Vector3.up * (cp.Height == CoverHeight.Low ? 0.9f : 1.5f);
            Vector3 target = threat + Vector3.up * 1.3f;
            Vector3 dir = target - eye;
            float distance = dir.magnitude;
            int count = Physics.RaycastNonAlloc(eye, dir / distance, hits, distance, blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].rigidbody == null && !hits[i].transform.IsChildOf(transform)) return true;
            return false;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
