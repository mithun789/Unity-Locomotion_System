using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// COD-style snap-to-cover for the player. Enter near a wall: kneel facing low cover, back against tall
    /// cover. Move slides along the wall and stops at its edge. Aim / fire pops up over low cover, or steps out
    /// past the nearest edge (left or right) of tall cover and back again on release. Moving away from the wall,
    /// sprinting or pressing the cover key again leaves cover. Used by PlayerSoldierInput.
    /// </summary>
    public class PlayerCover : MonoBehaviour
    {
        [Header("Enter")]
        [Tooltip("How far a wall can be to snap to it, in metres.")]
        [SerializeField] float reach = 1.4f;
        [Tooltip("Distance from the wall to the soldier's root while in cover, in metres.")]
        [SerializeField] float wallGap = 0.45f;
        [SerializeField] LayerMask coverMask = ~0;

        [Header("Height (probe heights above the feet)")]
        [Tooltip("A wall must reach this high to be cover at all.")]
        [SerializeField] float lowProbe = 0.8f;
        [Tooltip("A wall reaching this high is tall cover (stand); otherwise crouch.")]
        [SerializeField] float highProbe = 1.45f;

        [Header("Edges")]
        [Tooltip("Stop this far before the end of the wall, in metres.")]
        [SerializeField] float edgeMargin = 0.25f;
        [Tooltip("Pull away from the wall this hard (input dot) to leave cover.")]
        [SerializeField] float leaveThreshold = 0.7f;

        [Header("Peek (tall cover)")]
        [Tooltip("How far along the wall to look for an edge to peek around, in metres.")]
        [SerializeField] float edgeSearch = 1.5f;
        [Tooltip("How far past the edge to step when peeking, in metres.")]
        [SerializeField] float peekStep = 0.55f;

        readonly RaycastHit[] hits = new RaycastHit[16];

        public bool IsActive { get; private set; }
        public bool IsLow { get; private set; }
        /// <summary>Into the wall.</summary>
        public Vector3 Forward { get; private set; }
        public Vector3 Anchor { get; private set; }
        /// <summary>Stepping out past a tall cover edge to shoot.</summary>
        public bool IsPeeking { get; private set; }
        /// <summary>Stepping back into cover after a peek.</summary>
        public bool IsReturning { get; private set; }
        public Vector3 PeekTarget { get; private set; }
        /// <summary>
        /// Tall cover: look out to the soldier's own left (mirrored pose). Follows the last slide or peek
        /// direction; on entry, the nearest edge.
        /// </summary>
        public bool FaceLeft { get; private set; }
        public Vector3 ReturnTarget { get; private set; }

        /// <summary>
        /// Tall cover: find the nearest edge (either side) within edgeSearch and aim to step past it.
        /// False in low cover or in the middle of a long wall (then the soldier just stands).
        /// </summary>
        public bool BeginPeek()
        {
            if (!IsActive || IsLow || IsPeeking) return false;
            if (!FindEdge(out int side, out float d)) return false;
            Vector3 pos = transform.position;
            ReturnTarget = pos;
            PeekTarget = pos + Along * (side * (d + peekStep));
            FaceLeft = side > 0;
            IsPeeking = true;
            IsReturning = false;
            return true;
        }

        /// <summary>Along the wall, to the soldier's left (its back is to the wall).</summary>
        Vector3 Along => Vector3.Cross(Vector3.up, Forward);

        /// <summary>Nearest end of the tall wall within edgeSearch: side +1 along Along, -1 against it.</summary>
        bool FindEdge(out int side, out float distance)
        {
            Vector3 pos = transform.position;
            for (distance = 0.15f; distance <= edgeSearch; distance += 0.15f)
            {
                // Prefer the side the soldier already looks to.
                foreach (int s in FaceLeft ? new[] { 1, -1 } : new[] { -1, 1 })
                {
                    Vector3 probe = pos + Along * (s * distance);
                    if (ProbeWall(probe, Forward, wallGap + 0.6f, out _) && Covers(probe, Forward, out bool low) && !low) continue;
                    side = s;
                    return true;
                }
            }
            side = 0;
            return false;
        }

        public void EndPeek()
        {
            if (!IsPeeking) return;
            IsPeeking = false;
            IsReturning = true;
        }

        /// <summary>Snap to a wall in direction (world, flat). False when there is no cover there.</summary>
        public bool TryEnter(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return false;
            if (!ProbeWall(transform.position, direction.normalized, reach, out RaycastHit wall)) return false;

            Vector3 into = -wall.normal;
            into.y = 0f;
            into.Normalize();
            if (!Covers(transform.position, into, out bool low)) return false;

            IsActive = true;
            IsLow = low;
            Forward = into;
            Anchor = wall.point - into * wallGap;
            FaceLeft = false;
            if (!IsLow && FindEdge(out int side, out _)) FaceLeft = side > 0;
            return true;
        }

        public void Exit()
        {
            IsActive = false;
            IsPeeking = false;
            IsReturning = false;
        }

        /// <summary>
        /// Keeps cover up to date for this frame and returns the move input to use (edge-limited).
        /// Leaves cover when the wall is gone, the player pulls away, or sprints.
        /// </summary>
        public Vector2 Tick(Vector2 move, Vector3 moveWorld, bool sprint)
        {
            if (!IsActive) return move;

            Vector3 pos = transform.position;
            // Out past the edge: the wall checks below would fail; the input side steers the soldier.
            if (IsPeeking) return move;
            if (IsReturning)
            {
                Vector3 back = ReturnTarget - pos;
                back.y = 0f;
                if (back.sqrMagnitude > 0.02f) return move;
                IsReturning = false;
            }

            if (sprint || Vector3.Dot(moveWorld, -Forward) > leaveThreshold
                || !ProbeWall(pos, Forward, wallGap + 0.6f, out RaycastHit wall) || !Covers(pos, Forward, out bool low))
            {
                Exit();
                return move;
            }

            // Follow the wall (it may curve or step) and its height.
            Vector3 into = -wall.normal;
            into.y = 0f;
            if (into.sqrMagnitude > 0.01f) Forward = into.normalized;
            Anchor = wall.point - Forward * wallGap;
            IsLow = low;

            // Edge: no wall a little further along the direction of travel -> do not slide that way.
            float side = Vector3.Dot(moveWorld, Along);
            if (Mathf.Abs(side) > 0.05f)
            {
                FaceLeft = side > 0f;
                Vector3 ahead = pos + Along * (Mathf.Sign(side) * edgeMargin);
                if (!ProbeWall(ahead, Forward, wallGap + 0.6f, out _) || !Covers(ahead, Forward, out _))
                    return Vector2.zero;
            }
            return move;
        }

        /// <summary>The wall is at least lowProbe high; low when it is lower than highProbe.</summary>
        bool Covers(Vector3 feet, Vector3 into, out bool low)
        {
            low = false;
            if (!ProbeAt(feet, into, lowProbe, wallGap + 0.8f)) return false;
            low = !ProbeAt(feet, into, highProbe, wallGap + 0.8f);
            return true;
        }

        bool ProbeAt(Vector3 feet, Vector3 into, float height, float distance) =>
            ProbeWall(feet + Vector3.up * (height - 0.45f), into, distance, out _);

        /// <summary>Closest near-vertical scenery face along direction, from knee height (+ offset).</summary>
        bool ProbeWall(Vector3 feet, Vector3 direction, float distance, out RaycastHit wall)
        {
            Vector3 origin = feet + Vector3.up * 0.45f;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, coverMask, QueryTriggerInteraction.Ignore);
            wall = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                if (h.distance >= best || h.rigidbody != null || h.transform.IsChildOf(transform) || Mathf.Abs(h.normal.y) > 0.3f) continue;
                best = h.distance;
                wall = h;
            }
            return best < float.MaxValue;
        }
    }
}
