using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// A group of AI soldiers (children of this object). Shares enemy sightings so the whole squad
    /// reacts when one member sees something, and gives each member a formation slot on the route.
    /// </summary>
    public class Squad : MonoBehaviour
    {
        [Tooltip("Sideways distance between members walking a shared route, in metres.")]
        [SerializeField] float formationSpacing = 3f;
        [Tooltip("Members report a sighting at most this often, in seconds.")]
        [SerializeField] float reportInterval = 0.5f;

        float nextReportTime;

        public Vector3 LastReportPosition { get; private set; }
        public float LastReportTime { get; private set; } = float.NegativeInfinity;

        public void Report(Vector3 enemyPosition)
        {
            if (Time.time < nextReportTime) return;
            nextReportTime = Time.time + reportInterval;
            LastReportPosition = enemyPosition;
            LastReportTime = Time.time;
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
