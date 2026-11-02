using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace WarMission.EditorTools
{
    /// <summary>
    /// War Mission > Add Mission To Open Scene: builds a "Mission" object in the open scene around the player:
    /// MissionManager + MissionHud, a WaveSpawner with three spawn points ~32 m ahead of the player (on the
    /// NavMesh), objective 1 "Eliminate the enemy waves", objective 2 "Reach the extraction point" (behind
    /// the player start, hold 5 s). Re-running replaces the previous Mission object. Needs a baked NavMesh.
    /// </summary>
    public static class MissionSetupTool
    {
        const string EnemyPrefabPath = "Assets/_Project/Prefabs/Soldier_Enemy.prefab";
        const string RootName = "Mission";

        [MenuItem("War Mission/Add Mission To Open Scene")]
        static void AddMission()
        {
            var player = Object.FindAnyObjectByType<PlayerSoldierInput>();
            if (player == null)
            {
                EditorUtility.DisplayDialog("Add Mission", "No soldier with PlayerSoldierInput in the open scene. Add Soldier_Player first.", "OK");
                return;
            }
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            if (enemyPrefab == null)
            {
                EditorUtility.DisplayDialog("Add Mission", $"Enemy prefab not found at {EnemyPrefabPath}. Run Build Soldier Prefab.", "OK");
                return;
            }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            Vector3 start = player.transform.position;
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Mission");
            var manager = root.AddComponent<MissionManager>();
            root.AddComponent<MissionHud>();

            // Waves come from ahead of the player.
            var spawnerObject = new GameObject("WaveSpawner");
            spawnerObject.transform.SetParent(root.transform, false);
            var spawner = spawnerObject.AddComponent<WaveSpawner>();
            float[] angles = { 0f, 55f, -55f };
            var points = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < angles.Length; i++)
            {
                Vector3 wanted = start + Quaternion.Euler(0f, angles[i], 0f) * forward * 32f;
                if (!TryNavMeshPoint(wanted, start, out Vector3 point)) continue;
                var spawn = new GameObject($"Spawn_{i + 1}");
                spawn.transform.SetParent(spawnerObject.transform, false);
                spawn.transform.position = point;
                points.Add(spawn.transform);
            }
            if (points.Count == 0)
                Debug.LogWarning("[MissionSetup] No NavMesh found ahead of the player; move the WaveSpawner's spawn points by hand.");

            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
            SetArray(spawnerSo.FindProperty("spawnPoints"), points.ToArray());
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            // Objective 1: clear the waves.
            var wavesObject = new GameObject("Objective_EliminateWaves");
            wavesObject.transform.SetParent(root.transform, false);
            var waves = wavesObject.AddComponent<EliminateWavesObjective>();
            var wavesSo = new SerializedObject(waves);
            wavesSo.FindProperty("title").stringValue = "Eliminate the enemy waves";
            wavesSo.FindProperty("spawner").objectReferenceValue = spawner;
            wavesSo.ApplyModifiedPropertiesWithoutUndo();

            // Objective 2: extraction behind the start, held for a few seconds.
            var extractObject = new GameObject("Objective_Extraction");
            extractObject.transform.SetParent(root.transform, false);
            extractObject.transform.position = TryNavMeshPoint(start - forward * 18f, start, out Vector3 extraction) ? extraction : start;
            var extract = extractObject.AddComponent<ReachZoneObjective>();
            var extractSo = new SerializedObject(extract);
            extractSo.FindProperty("title").stringValue = "Reach the extraction point";
            extractSo.FindProperty("holdTime").floatValue = 5f;
            extractSo.ApplyModifiedPropertiesWithoutUndo();

            var managerSo = new SerializedObject(manager);
            SetArray(managerSo.FindProperty("objectives"), new Object[] { waves, extract });
            managerSo.FindProperty("player").objectReferenceValue = player.GetComponent<Health>();
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[MissionSetup] Mission added: {points.Count} spawn points, extraction at {extractObject.transform.position}. Save the scene.");
        }

        /// <summary>A NavMesh point near the wanted spot that has a full path from the start.</summary>
        static bool TryNavMeshPoint(Vector3 wanted, Vector3 start, out Vector3 point)
        {
            point = wanted;
            var path = new NavMeshPath();
            for (float shrink = 1f; shrink >= 0.4f; shrink -= 0.2f)
            {
                Vector3 candidate = start + (wanted - start) * shrink;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(start, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                point = hit.position;
                return true;
            }
            return false;
        }

        static void SetArray(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
