using UnityEngine;

namespace WarMission
{
    /// <summary>Patrol waypoints are this object's children, visited in order and looped.</summary>
    public class PatrolRoute : MonoBehaviour
    {
        public int Count => transform.childCount;

        public Vector3 GetPoint(int index) => transform.GetChild(index % Count).position;

        void OnDrawGizmos()
        {
            if (Count == 0) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            for (int i = 0; i < Count; i++)
            {
                Gizmos.DrawWireSphere(GetPoint(i), 0.3f);
                Gizmos.DrawLine(GetPoint(i), GetPoint(i + 1));
            }
        }
    }
}
