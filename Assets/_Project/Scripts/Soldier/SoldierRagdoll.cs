using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// Ragdoll bodies stay kinematic (and act as hitboxes) while alive.
    /// On death: stop the Animator, agent and brain, let the bodies fall, push the hit body, drop the rifle.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class SoldierRagdoll : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [Tooltip("Rifle root. Detached and dropped on death.")]
        [SerializeField] Transform weapon;
        [SerializeField] float weaponMass = 3.5f;

        Rigidbody[] bodies;
        Health health;

        void Awake()
        {
            health = GetComponent<Health>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            bodies = animator.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody body in bodies)
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }
        }

        void OnEnable() => health.Died += OnDied;
        void OnDisable() => health.Died -= OnDied;

        // The dropped rifle is no longer our child; clean it up with the body.
        void OnDestroy()
        {
            if (weapon != null && weapon.parent == null) Destroy(weapon.gameObject);
        }

        void OnDied(DamageInfo damage)
        {
            Vector3 inherited = TryGetComponent(out SoldierMotor motor) ? motor.Velocity : Vector3.zero;

            // Stop everything that drives the body.
            if (TryGetComponent(out Soldier soldier)) soldier.enabled = false;
            if (TryGetComponent(out SoldierAim aim)) aim.enabled = false;
            if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
            animator.enabled = false;

            Rigidbody closest = null;
            float best = float.MaxValue;
            foreach (Rigidbody body in bodies)
            {
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.linearVelocity = inherited;

                float d = (body.worldCenterOfMass - damage.Point).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    closest = body;
                }
            }
            if (closest != null)
                closest.AddForceAtPosition(damage.Direction * damage.Force, damage.Point, ForceMode.Impulse);

            DropWeapon(inherited);
        }

        void DropWeapon(Vector3 velocity)
        {
            if (weapon == null) return;
            weapon.SetParent(null, true);
            if (!weapon.TryGetComponent(out BoxCollider _))
            {
                var box = weapon.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.03f, 0.15f);
                box.size = new Vector3(0.06f, 0.16f, 0.9f);
            }
            var body = weapon.gameObject.AddComponent<Rigidbody>();
            body.mass = weaponMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = velocity;
        }
    }
}
