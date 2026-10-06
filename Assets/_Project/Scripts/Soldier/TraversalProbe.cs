using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>What the probe found in front of the soldier.</summary>
    public struct TraversalScan
    {
        public Vector3 WallPoint;
        public Vector3 Approach;        // flat, into the obstacle
        public float Height;            // obstacle top above the feet
        public float Depth;             // top depth along Approach (float.MaxValue = deeper than probed)
        public bool CanVault;
        public Vector3 VaultLanding;    // feet position on the far side, on the NavMesh
        public bool CanMantle;
        public Vector3 MantleEnd;       // feet position on top, on the NavMesh
    }

    /// <summary>
    /// Raycast checks for traversal, GASP style: find a wall ahead, its top, how deep the top is,
    /// and whether there is room to land on top (mantle) or behind it (vault). Also finds drops.
    /// Ignores anything with a Rigidbody (soldiers' hitboxes, loose rifles).
    /// </summary>
    public static class TraversalProbe
    {
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly float[] wallHeights = { 0.15f, 0.45f, 0.8f, 1.2f, 1.7f };

        const float TopStep = 0.2f;
        const float MaxTopProbe = 1.6f;
        const float StandRadius = 0.3f;
        const float StandHeight = 1.7f;

        public static bool Scan(Transform self, Vector3 feet, Vector3 direction, float reach, float maxHeight,
                                float maxVaultDepth, LayerMask mask, out TraversalScan scan)
        {
            scan = default;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();

            // 1. Closest wall ahead at several heights.
            bool found = false;
            float bestDistance = float.MaxValue;
            RaycastHit wall = default;
            foreach (float y in wallHeights)
            {
                if (y > maxHeight + 0.1f) break;
                if (Closest(feet + Vector3.up * y, direction, reach, mask, self, out RaycastHit hit)
                    && Mathf.Abs(hit.normal.y) < 0.3f && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    wall = hit;
                    found = true;
                }
            }
            if (!found) return false;

            Vector3 approach = -wall.normal;
            approach.y = 0f;
            approach.Normalize();

            // 2. Top of the obstacle just past the wall face.
            float probeTop = feet.y + maxHeight + 0.3f;
            Vector3 topOrigin = wall.point + approach * 0.15f;
            topOrigin.y = probeTop;
            if (!Closest(topOrigin, Vector3.down, maxHeight + 0.3f, mask, self, out RaycastHit top) || top.normal.y < 0.7f)
                return false;
            float height = top.point.y - feet.y;

            // 3. How deep the top goes.
            float depth = float.MaxValue;
            for (float s = 0.15f + TopStep; s <= MaxTopProbe; s += TopStep)
            {
                Vector3 origin = wall.point + approach * s;
                origin.y = top.point.y + 0.3f;
                bool stillTop = Closest(origin, Vector3.down, 0.6f, mask, self, out RaycastHit h) && Mathf.Abs(h.point.y - top.point.y) < 0.25f;
                if (!stillTop)
                {
                    depth = s;
                    break;
                }
            }

            scan = new TraversalScan { WallPoint = wall.point, Approach = approach, Height = height, Depth = depth };

            // 4a. Vault: thin top, ground on the far side at about the same level, room to stand.
            if (depth <= maxVaultDepth)
            {
                Vector3 landing = wall.point + approach * (depth + 0.6f);
                landing.y = feet.y + height + 0.5f;
                if (Closest(landing, Vector3.down, height + 1.5f, mask, self, out RaycastHit ground)
                    && Mathf.Abs(ground.point.y - feet.y) < 0.4f
                    && HasRoom(ground.point, self)
                    && NavMesh.SamplePosition(ground.point, out NavMeshHit nav, 0.5f, NavMesh.AllAreas))
                {
                    scan.CanVault = true;
                    scan.VaultLanding = nav.position;
                }
            }

            // 4b. Mantle: enough top to stand on.
            if (depth > 0.55f)
            {
                Vector3 onTop = wall.point + approach * 0.5f;
                onTop.y = top.point.y;
                if (HasRoom(onTop, self) && NavMesh.SamplePosition(onTop, out NavMeshHit nav, 0.45f, NavMesh.AllAreas)
                    && Mathf.Abs(nav.position.y - top.point.y) < 0.2f)
                {
                    scan.CanMantle = true;
                    scan.MantleEnd = nav.position;
                }
            }

            return scan.CanVault || scan.CanMantle;
        }

        /// <summary>At a NavMesh edge ahead with lower ground beyond: where to land and how far down.</summary>
        public static bool FindDrop(Transform self, Vector3 feet, Vector3 direction, float edgeReach, float minDrop, float maxDrop,
                                    LayerMask mask, out Vector3 landing, out float drop)
        {
            landing = default;
            drop = 0f;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();

            if (!NavMesh.Raycast(feet, feet + direction * edgeReach, out NavMeshHit edge, NavMesh.AllAreas)) return false;
            if (Closest(feet + Vector3.up * 1f, direction, 1.2f, mask, self, out _)) return false; // wall, not an edge

            // NavMesh edges sit one agent radius inside the drop; land clear of the wall below.
            Vector3 beyond = edge.position + direction * 0.9f + Vector3.up * 0.5f;
            if (!Closest(beyond, Vector3.down, maxDrop + 0.6f, mask, self, out RaycastHit ground)) return false;
            drop = feet.y - ground.point.y;
            if (drop < minDrop || drop > maxDrop || !HasRoom(ground.point, self)) return false;
            if (!NavMesh.SamplePosition(ground.point, out NavMeshHit nav, 0.5f, NavMesh.AllAreas)) return false;
            landing = nav.position;
            return true;
        }

        static bool HasRoom(Vector3 feet, Transform self)
        {
            Vector3 bottom = feet + Vector3.up * (StandRadius + 0.15f);
            Vector3 topPoint = feet + Vector3.up * (StandHeight - StandRadius);
            var overlaps = Physics.OverlapCapsule(bottom, topPoint, StandRadius, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider c in overlaps)
                if (!c.transform.IsChildOf(self) && c.attachedRigidbody == null) return false;
            return true;
        }

        /// <summary>Closest hit that is static scenery (not this soldier, not a rigidbody).</summary>
        static bool Closest(Vector3 origin, Vector3 direction, float distance, LayerMask mask, Transform self, out RaycastHit closest)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
            closest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                if (h.distance >= best || h.transform.IsChildOf(self) || h.rigidbody != null) continue;
                best = h.distance;
                closest = h;
            }
            return best < float.MaxValue;
        }
    }
}
