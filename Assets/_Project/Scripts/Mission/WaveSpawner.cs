using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// Sends enemy waves at the player. Each wave is a new Squad of AI soldiers spawned on the NavMesh around
    /// one of the spawn points (the one farthest from the player, so they do not pop in beside them).
    /// Spawned soldiers know roughly where the player is: idle ones are sent to hunt every huntInterval.
    /// The next wave starts its delay once the current one is dead.
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        [Serializable]
        public class Wave
        {
            public int count = 3;
            [Tooltip("Seconds after the previous wave died (or after Begin, for the first wave).")]
            public float delay = 5f;
        }

        [SerializeField] GameObject enemyPrefab;
        [SerializeField] Wave[] waves = { new Wave { count = 3, delay = 4f }, new Wave { count = 4, delay = 8f }, new Wave { count = 5, delay = 8f } };
        [SerializeField] Transform[] spawnPoints;
        [Tooltip("Members spawn within this radius of the spawn point, in metres.")]
        [SerializeField] float spawnSpread = 3f;
        [Tooltip("Seconds between hunt orders to idle soldiers.")]
        [SerializeField] float huntInterval = 6f;
        [Tooltip("Hunt orders aim at a point this far from the player (they search, not home in).")]
        [SerializeField] float huntError = 5f;

        readonly List<AISoldierBrain> alive = new List<AISoldierBrain>();
        Transform target;
        int nextWave;
        float nextSpawnTime = float.PositiveInfinity;
        float nextHunt;
        bool started;

        public int TotalWaves => waves.Length;
        /// <summary>Number of waves spawned so far.</summary>
        public int CurrentWave => nextWave;
        public int AliveCount => alive.Count;
        public bool IsBetweenWaves => started && alive.Count == 0 && nextWave < waves.Length;
        public float TimeToNextWave => Mathf.Max(0f, nextSpawnTime - Time.time);
        public bool IsFinished => started && nextWave >= waves.Length && alive.Count == 0;

        /// <summary>A wave spawned (1-based number).</summary>
        public event Action<int> WaveStarted;

        public void Begin(Transform player)
        {
            target = player;
            started = true;
            nextWave = 0;
            nextSpawnTime = waves.Length > 0 ? Time.time + waves[0].delay : Time.time;
        }

        void Update()
        {
            if (!started) return;
            alive.RemoveAll(b => b == null || !b.IsAlive);

            if (alive.Count == 0 && nextWave < waves.Length && Time.time >= nextSpawnTime)
            {
                SpawnWave(waves[nextWave]);
                nextWave++;
                WaveStarted?.Invoke(nextWave);
                nextSpawnTime = nextWave < waves.Length ? float.PositiveInfinity : nextSpawnTime;
            }
            // Wave cleared: start the countdown to the next one.
            if (alive.Count == 0 && nextWave < waves.Length && float.IsPositiveInfinity(nextSpawnTime))
                nextSpawnTime = Time.time + waves[nextWave].delay;

            if (Time.time >= nextHunt)
            {
                nextHunt = Time.time + huntInterval;
                Hunt();
            }
        }

        void SpawnWave(Wave wave)
        {
            if (enemyPrefab == null)
            {
                Debug.LogWarning("[WaveSpawner] No enemy prefab assigned.", this);
                return;
            }
            Vector3 origin = PickSpawnPoint();
            var squadObject = new GameObject($"Wave {nextWave + 1}");
            squadObject.transform.position = origin;
            squadObject.AddComponent<Squad>();

            Vector3 toPlayer = target != null ? target.position - origin : transform.forward;
            toPlayer.y = 0f;
            Quaternion facing = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;

            for (int i = 0; i < wave.count; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * spawnSpread;
                Vector3 position = origin + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(position, out NavMeshHit hit, spawnSpread + 2f, NavMesh.AllAreas)) position = hit.position;
                GameObject soldier = Instantiate(enemyPrefab, position, facing, squadObject.transform);
                soldier.name = $"Enemy_W{nextWave + 1}_{i + 1}";
                if (soldier.TryGetComponent(out AISoldierBrain brain))
                {
                    alive.Add(brain);
                    if (target != null) brain.Alert(HuntPoint());
                }
            }
        }

        Vector3 PickSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return transform.position;
            Transform best = null;
            float bestDistance = -1f;
            foreach (Transform point in spawnPoints)
            {
                if (point == null) continue;
                float d = target != null ? (point.position - target.position).sqrMagnitude : UnityEngine.Random.value;
                if (d > bestDistance)
                {
                    bestDistance = d;
                    best = point;
                }
            }
            return best != null ? best.position : transform.position;
        }

        /// <summary>Soldiers with nothing to do go and look near the player.</summary>
        void Hunt()
        {
            if (target == null) return;
            for (int i = 0; i < alive.Count; i++)
                if (!alive[i].InCombat) alive[i].Alert(HuntPoint());
        }

        Vector3 HuntPoint()
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * huntError;
            return target.position + new Vector3(offset.x, 0f, offset.y);
        }

        void OnDrawGizmos()
        {
            if (spawnPoints == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            foreach (Transform point in spawnPoints)
                if (point != null) Gizmos.DrawWireSphere(point.position, spawnSpread);
        }
    }
}
