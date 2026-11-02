using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Frag grenade: a small physics ball that explodes when the fuse runs out. Damage falls off with
    /// distance and needs a clear path from the blast to the victim's chest (cover protects).
    /// Live grenades sit in a static list so AI can see them and run.
    /// Built at runtime by SoldierGrenades (no prefab needed).
    /// </summary>
    public class Grenade : MonoBehaviour
    {
        static readonly List<Grenade> live = new List<Grenade>();
        static readonly Collider[] overlap = new Collider[64];
        static readonly HashSet<Health> damaged = new HashSet<Health>();
        static readonly RaycastHit[] hits = new RaycastHit[16];

        [SerializeField] float fuseTime = 3f;
        [SerializeField] float radius = 6f;
        [SerializeField] float maxDamage = 160f;
        [Tooltip("Damage at the edge of the radius, as a share of maxDamage.")]
        [SerializeField] float edgeDamage = 0.1f;
        [SerializeField] float force = 450f;
        [SerializeField] LayerMask blockMask = ~0;

        float explodeTime;

        /// <summary>Live grenades, for AI avoidance.</summary>
        public static IReadOnlyList<Grenade> Live => live;
        /// <summary>A grenade exploded (position, thrower). AI hears it and feels it as incoming fire.</summary>
        public static event Action<Vector3, GameObject> AnyExploded;

        public GameObject Thrower { get; private set; }
        public TeamMember ThrowerTeam { get; private set; }
        public float Radius => radius;
        public float TimeLeft => explodeTime - Time.time;
        public Vector3 Position => transform.position;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            live.Clear();
            AnyExploded = null;
        }

        /// <summary>Arm the grenade. fuseLeft is what remains of the fuse after cooking.</summary>
        public void Arm(GameObject thrower, float fuseLeft)
        {
            Thrower = thrower;
            ThrowerTeam = thrower != null ? thrower.GetComponent<TeamMember>() : null;
            explodeTime = Time.time + Mathf.Min(fuseLeft, fuseTime);
        }

        void OnEnable() => live.Add(this);
        void OnDisable() => live.Remove(this);

        void Update()
        {
            if (Time.time >= explodeTime) Explode();
        }

        void Explode()
        {
            Vector3 center = transform.position + Vector3.up * 0.15f;
            damaged.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Health health = overlap[i].GetComponentInParent<Health>();
                if (health == null || health.IsDead || !damaged.Add(health)) continue;

                var member = health.GetComponent<TeamMember>();
                Vector3 chest = member != null && member.Chest != null ? member.Chest.position : health.transform.position + Vector3.up;
                if (!Clear(center, chest, health.transform)) continue;

                float t = Mathf.Clamp01(Vector3.Distance(center, chest) / radius);
                Vector3 dir = chest - center;
                dir = dir.sqrMagnitude > 0.001f ? (dir.normalized + Vector3.up * 0.5f).normalized : Vector3.up;
                health.TakeDamage(new DamageInfo
                {
                    Amount = maxDamage * Mathf.Lerp(1f, edgeDamage, t),
                    Point = chest,
                    Direction = dir,
                    Force = force * (1f - t * 0.7f),
                    Source = Thrower,
                });
            }

            // Loose physics objects get pushed too.
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = overlap[i].attachedRigidbody;
                if (body != null && !body.isKinematic) body.AddExplosionForce(force * 0.5f, center, radius, 0.5f, ForceMode.Impulse);
            }

            AnyExploded?.Invoke(center, Thrower);
            SpawnFlash(center);
            Destroy(gameObject);
        }

        /// <summary>No scenery between the blast and the target (the target's own hitboxes do not count).</summary>
        bool Clear(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            if (distance < 0.05f) return true;
            int count = Physics.RaycastNonAlloc(from, dir / distance, hits, distance, blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(target) || hits[i].transform == transform) continue;
                if (hits[i].rigidbody == null) return false;
            }
            return true;
        }

        void SpawnFlash(Vector3 center)
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "GrenadeBlast";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.position = center;
            flash.AddComponent<GrenadeBlast>().Init(radius * 0.6f);
        }
    }

    /// <summary>Quick expanding orange ball for the explosion, then gone.</summary>
    public class GrenadeBlast : MonoBehaviour
    {
        const float Duration = 0.35f;
        float start;
        float size;
        Material material;

        public void Init(float maxSize)
        {
            size = maxSize;
            start = Time.time;
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                material = renderer.material;
                var color = new Color(1f, 0.55f, 0.15f);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", color * 2000f);
            }
            transform.localScale = Vector3.one * 0.2f;
        }

        void Update()
        {
            float t = (Time.time - start) / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = Vector3.one * Mathf.Lerp(0.2f, size, Mathf.Sqrt(t));
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
