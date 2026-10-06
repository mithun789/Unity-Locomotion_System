using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace WarMission.EditorTools
{
    /// <summary>
    /// Places CoverPoints around every box under the scene's "Environment" object.
    /// Low cover (0.9-1.25 m): points along each side, soldier crouches behind and stands to shoot over.
    /// High cover (1.25 m and up): points at the corners of each side, soldier steps out past the corner.
    /// Points must be on the NavMesh (rebake first). Re-running replaces the previous points.
    /// </summary>
    public static class CoverPointGenerator
    {
        const string RootName = "CoverPoints";
        const float MinLowHeight = 0.9f;
        const float MinHighHeight = 1.25f;
        const float WallGap = 0.55f;      // soldier distance from the cover face
        const float LowSpacing = 1.4f;
        const float CornerInset = 0.35f;

        [MenuItem("War Mission/Generate Cover Points")]
        public static void GenerateInActiveScene()
        {
            int count = Generate();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[CoverPointGenerator] {count} cover points in {EditorSceneManager.GetActiveScene().name}");
        }

        public static int Generate()
        {
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var environment = GameObject.Find("Environment");
            if (environment == null) return 0;

            var root = new GameObject(RootName).transform;
            int count = 0;
            foreach (BoxCollider box in environment.GetComponentsInChildren<BoxCollider>())
            {
                Bounds b = box.bounds;
                float height = b.size.y;
                if (height < MinLowHeight) continue;
                bool high = height >= MinHighHeight;

                // Four sides: outward normal, tangent along the face, face half length.
                var sides = new[]
                {
                    (Vector3.forward, Vector3.right, b.extents.x, b.extents.z),
                    (Vector3.back, Vector3.left, b.extents.x, b.extents.z),
                    (Vector3.right, Vector3.back, b.extents.z, b.extents.x),
                    (Vector3.left, Vector3.forward, b.extents.z, b.extents.x),
                };
                foreach (var (normal, tangent, halfLength, halfDepth) in sides)
                {
                    Vector3 faceCenter = new Vector3(b.center.x, b.min.y, b.center.z) + normal * (halfDepth + WallGap);
                    if (high)
                    {
                        foreach (int end in new[] { -1, 1 })
                            count += TryAdd(root, box.name, faceCenter + tangent * (end * (halfLength - CornerInset)), normal, CoverHeight.High, tangent * end);
                    }
                    else
                    {
                        int slots = Mathf.Max(1, Mathf.FloorToInt((halfLength * 2f - 0.6f) / LowSpacing) + 1);
                        float span = (slots - 1) * LowSpacing;
                        for (int i = 0; i < slots; i++)
                            count += TryAdd(root, box.name, faceCenter + tangent * (-span * 0.5f + i * LowSpacing), normal, CoverHeight.Low, Vector3.zero);
                    }
                }
            }
            return count;
        }

        /// <summary>Adds a point facing the cover if it is on the NavMesh and not inside other scenery.</summary>
        static int TryAdd(Transform root, string coverName, Vector3 position, Vector3 outward, CoverHeight height, Vector3 cornerDirection)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit nav, 0.35f, NavMesh.AllAreas)) return 0;
            if (Mathf.Abs(nav.position.y - position.y) > 0.2f) return 0; // snapped onto a box top
            Vector3 p = nav.position;
            if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return 0;

            var go = new GameObject($"Cover_{coverName}_{height}");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-outward));
            var point = go.AddComponent<CoverPoint>();
            int side = 0;
            if (height == CoverHeight.High) side = Vector3.Dot(cornerDirection, go.transform.right) > 0f ? 1 : -1;
            point.Configure(height, side);
            return 1;
        }
    }
}
