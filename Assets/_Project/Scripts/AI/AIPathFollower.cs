using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// Plans a NavMesh path and hands out the direction to walk. The brain turns that direction into
    /// SoldierCommand.Move, so AI moves through the same motor code as the player.
    /// </summary>
    public class AIPathFollower : MonoBehaviour
    {
        [SerializeField] float repathInterval = 0.5f;
        [Tooltip("Distance at which the destination counts as reached, in metres.")]
        [SerializeField] float arriveDistance = 0.5f;
        [Tooltip("Distance at which an intermediate corner counts as passed, in metres.")]
        [SerializeField] float cornerDistance = 0.35f;
        [Tooltip("How far off the NavMesh a destination may be and still be used, in metres.")]
        [SerializeField] float destinationSnap = 2f;

        readonly Vector3[] corners = new Vector3[32];
        NavMeshPath path;
        int cornerCount;
        int cornerIndex;
        float nextRepath;

        public bool HasDestination { get; private set; }
        public Vector3 Destination { get; private set; }
        public bool IsAtDestination => HasDestination && FlatDistance(transform.position, Destination) <= arriveDistance;
        public float RemainingDistance => HasDestination ? FlatDistance(transform.position, Destination) : 0f;

        void Awake() => path = new NavMeshPath();

        public void SetDestination(Vector3 destination)
        {
            if (HasDestination && (destination - Destination).sqrMagnitude < 0.04f) return;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, destinationSnap, NavMesh.AllAreas))
                destination = hit.position;
            Destination = destination;
            HasDestination = true;
            nextRepath = 0f;
        }

        public void Stop()
        {
            HasDestination = false;
            cornerCount = 0;
        }

        /// <summary>Normalized flat direction to walk this frame, or zero when there is nothing to do.</summary>
        public Vector3 GetMoveDirection()
        {
            if (!HasDestination || IsAtDestination) return Vector3.zero;
            if (Time.time >= nextRepath) Repath();
            if (cornerCount < 2) return Vector3.zero;

            Vector3 position = transform.position;
            while (cornerIndex < cornerCount - 1 && FlatDistance(position, corners[cornerIndex]) <= cornerDistance)
                cornerIndex++;

            Vector3 dir = corners[cornerIndex] - position;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        void Repath()
        {
            nextRepath = Time.time + repathInterval;
            if (NavMesh.CalculatePath(transform.position, Destination, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
            {
                cornerCount = path.GetCornersNonAlloc(corners);
                cornerIndex = 1;
            }
            else
            {
                cornerCount = 0;
            }
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnDrawGizmosSelected()
        {
            if (!HasDestination || cornerCount < 2) return;
            Gizmos.color = Color.cyan;
            for (int i = 1; i < cornerCount; i++) Gizmos.DrawLine(corners[i - 1], corners[i]);
        }
    }
}
