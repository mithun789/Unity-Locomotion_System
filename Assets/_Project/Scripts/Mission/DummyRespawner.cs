using System.Collections;
using UnityEngine;

namespace WarMission
{
    /// <summary>Test helper: when this soldier dies, spawn a fresh copy at the start pose and clear the body later.</summary>
    [RequireComponent(typeof(Health))]
    public class DummyRespawner : MonoBehaviour
    {
        [SerializeField] GameObject prefab;
        [SerializeField] float respawnDelay = 4f;
        [SerializeField] float corpseLifetime = 12f;

        Vector3 startPosition;
        Quaternion startRotation;
        Health health;

        void Awake()
        {
            health = GetComponent<Health>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        void OnEnable() => health.Died += OnDied;
        void OnDisable() => health.Died -= OnDied;

        void OnDied(DamageInfo damage) => StartCoroutine(Respawn());

        IEnumerator Respawn()
        {
            yield return new WaitForSeconds(respawnDelay);
            if (prefab != null)
            {
                GameObject clone = Instantiate(prefab, startPosition, startRotation);
                clone.name = name;
                var respawner = clone.AddComponent<DummyRespawner>();
                respawner.prefab = prefab;
                respawner.respawnDelay = respawnDelay;
                respawner.corpseLifetime = corpseLifetime;
            }
            Destroy(gameObject, Mathf.Max(0f, corpseLifetime - respawnDelay));
        }
    }
}
