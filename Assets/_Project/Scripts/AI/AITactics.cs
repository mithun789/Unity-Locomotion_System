using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// Position searches for tactical moves: flank spots that see the enemy from the side, and retreat
    /// spots away from the threat. Samples a few candidates on the NavMesh and scores them.
    /// </summary>
    public static class AITactics
    {
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly Vector3[] corners = new Vector3[32];
        static readonly float[] flankAngles = { 75f, 100f, 125f };
        static readonly float[] flankDistances = { 9f, 14f };
        static readonly float[] retreatAngles = { 0f, 35f, -35f, 70f, -70f };
        static readonly float[] retreatDistances = { 9f, 14f };
        static NavMeshPath path;

        /// <summary>
        /// A NavMesh spot beside the target (75-125 degrees around it from where the soldier is now) that has
        /// line of sight to the target and a path shorter than maxPath. side: +1 / -1 picks a side first, 0 either.
        /// </summary>
        public static bool FindFlankPosition(Vector3 self, Vector3 target, int side, float maxPath, LayerMask blockMask, out Vector3 result)
        {
            result = self;
            Vector3 fromTarget = Flat(self - target);
            if (fromTarget.sqrMagnitude < 0.01f) return false;
            fromTarget.Normalize();

            float bestCost = float.MaxValue;
            for (int s = -1; s <= 1; s += 2)
            {
                float sideCost = side == 0 || s == side ? 0f : 4f;
                for (int a = 0; a < flankAngles.Length; a++)
                {
                    for (int d = 0; d < flankDistances.Length; d++)
                    {
                        Vector3 candidate = target + Quaternion.Euler(0f, s * flankAngles[a], 0f) * fromTarget * flankDistances[d];
                        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                        Vector3 p = hit.position;
                        if (!SceneryClear(p + Vector3.up * 1.5f, target + Vector3.up * 1.4f, blockMask)) continue;
                        float length = PathLength(self, p);
                        if (length > maxPath) continue;

                        float cost = length + Mathf.Abs(flankAngles[a] - 100f) * 0.05f + sideCost;
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            result = p;
                        }
                    }
                }
            }
            return bestCost < float.MaxValue;
        }

        /// <summary>A NavMesh spot away from the threat, preferring one the threat cannot see.</summary>
        public static bool FindRetreatPosition(Vector3 self, Vector3 threat, float maxPath, LayerMask blockMask, out Vector3 result)
        {
            result = self;
            Vector3 away = Flat(self - threat);
            if (away.sqrMagnitude < 0.01f) return false;
            away.Normalize();

            float myDistance = Flat(self - threat).magnitude;
            float bestCost = float.MaxValue;
            for (int a = 0; a < retreatAngles.Length; a++)
            {
                for (int d = 0; d < retreatDistances.Length; d++)
                {
                    Vector3 candidate = self + Quaternion.Euler(0f, retreatAngles[a], 0f) * away * retreatDistances[d];
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                    Vector3 p = hit.position;
                    float gain = Flat(p - threat).magnitude - myDistance;
                    if (gain < 4f) continue;
                    float length = PathLength(self, p);
                    if (length > maxPath) continue;

                    bool hidden = !SceneryClear(p + Vector3.up * 1.4f, threat + Vector3.up * 1.4f, blockMask);
                    float cost = length - gain * 0.5f - (hidden ? 6f : 0f);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        result = p;
                    }
                }
            }
            return bestCost < float.MaxValue;
        }

        /// <summary>Walking length of the NavMesh path, or infinity when there is no full path.</summary>
        public static float PathLength(Vector3 from, Vector3 to)
        {
            path ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                return float.PositiveInfinity;
            int count = path.GetCornersNonAlloc(corners);
            float length = 0f;
            for (int i = 1; i < count; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        /// <summary>No static scenery between the points (soldier hitboxes have rigidbodies and are ignored).</summary>
        public static bool SceneryClear(Vector3 from, Vector3 to, LayerMask blockMask)
        {
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(from, dir / distance, hits, distance, blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].rigidbody == null) return false;
            return true;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
