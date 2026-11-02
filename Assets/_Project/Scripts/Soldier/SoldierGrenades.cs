using System;
using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Frag grenades for any soldier (player: G / gamepad North; AI: AISoldierBrain decides).
    /// SoldierCommand.Grenade starts a throw toward GrenadeTarget (or AimPoint); the grenade leaves the right
    /// hand after releaseDelay on a ballistic arc that lands on the target.
    /// </summary>
    public class SoldierGrenades : MonoBehaviour
    {
        [SerializeField] int startCount = 2;
        [SerializeField] float maxRange = 28f;
        [Tooltip("Seconds from the start of the throw to the release from the hand.")]
        [SerializeField] float releaseDelay = 0.55f;
        [Tooltip("Seconds the throw blocks firing and aiming.")]
        [SerializeField] float throwDuration = 1.1f;
        [SerializeField] float cooldown = 1.5f;
        [Tooltip("Launch angle above the horizon, degrees.")]
        [SerializeField] float launchAngle = 35f;
        [SerializeField] float maxThrowSpeed = 22f;
        [SerializeField] float fuseTime = 3f;
        [SerializeField] float grenadeSize = 0.12f;
        [Tooltip("Ground for landing spots past max range.")]
        [SerializeField] LayerMask groundMask = ~0;

        Animator animator;
        Transform hand;
        float throwStart;
        float nextThrowTime;
        bool released;
        Vector3 target;

        public int Count { get; private set; }
        public int StartCount => startCount;
        public float MaxRange => maxRange;
        public bool IsThrowing { get; private set; }
        public bool CanThrow => Count > 0 && !IsThrowing && Time.time >= nextThrowTime;

        /// <summary>The throw animation starts.</summary>
        public event Action ThrowStarted;
        /// <summary>A grenade left the hand.</summary>
        public event Action<Grenade> Thrown;

        void Awake()
        {
            Count = startCount;
            animator = GetComponentInChildren<Animator>();
        }

        public void AddGrenades(int amount) => Count += Mathf.Max(0, amount);

        public void Tick(in SoldierCommand command, float dt)
        {
            if (IsThrowing)
            {
                if (!released && Time.time - throwStart >= releaseDelay) Release();
                if (Time.time - throwStart >= throwDuration) IsThrowing = false;
                return;
            }

            if (!command.Grenade || !CanThrow) return;
            target = command.GrenadeTarget.sqrMagnitude > 0.0001f ? command.GrenadeTarget : command.AimPoint;
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            if (flat.magnitude > maxRange)
            {
                // Aimed past the range (or at the sky): land at max range, on whatever ground is there.
                target = transform.position + flat.normalized * maxRange;
                if (Physics.Raycast(target + Vector3.up * 6f, Vector3.down, out RaycastHit ground, 20f, groundMask, QueryTriggerInteraction.Ignore))
                    target = ground.point;
            }

            IsThrowing = true;
            released = false;
            throwStart = Time.time;
            nextThrowTime = Time.time + throwDuration + cooldown;
            Count--;
            ThrowStarted?.Invoke();
        }

        void Release()
        {
            released = true;
            Vector3 origin = HandPosition();
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Grenade";
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * grenadeSize;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material m = renderer.material;
                var color = new Color(0.22f, 0.27f, 0.18f);
                if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            }

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Collider own = go.GetComponent<Collider>();
            foreach (Collider c in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(own, c);

            body.linearVelocity = LaunchVelocity(origin, target);
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f;

            var grenade = go.AddComponent<Grenade>();
            grenade.Arm(gameObject, fuseTime);
            Thrown?.Invoke(grenade);
        }

        Vector3 HandPosition()
        {
            if (hand == null && animator != null && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null) return hand.position + transform.forward * 0.2f;
            return transform.position + Vector3.up * 1.7f + transform.forward * 0.4f;
        }

        /// <summary>Velocity on a fixed launch angle that lands on the target (steeper if the target is above).</summary>
        Vector3 LaunchVelocity(Vector3 from, Vector3 to)
        {
            Vector3 flat = to - from;
            float height = flat.y;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 dir = distance > 0.01f ? flat / distance : transform.forward;
            float g = -Physics.gravity.y;

            float angle = launchAngle;
            for (int attempt = 0; attempt < 3; attempt++, angle += 12f)
            {
                float rad = angle * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float denominator = 2f * cos * cos * (distance * Mathf.Tan(rad) - height);
                if (denominator <= 0.01f) continue;
                float speed = Mathf.Sqrt(g * distance * distance / denominator);
                speed = Mathf.Min(speed, maxThrowSpeed);
                return (dir * cos + Vector3.up * Mathf.Sin(rad)) * speed;
            }
            float steep = 60f * Mathf.Deg2Rad;
            return (dir * Mathf.Cos(steep) + Vector3.up * Mathf.Sin(steep)) * maxThrowSpeed * 0.6f;
        }
    }
}
