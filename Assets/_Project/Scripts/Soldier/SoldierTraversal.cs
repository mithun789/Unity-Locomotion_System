using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// Vault / mantle / drop with motion warping, GASP style. Jump near an obstacle asks TraversalProbe what
    /// is ahead, picks a TraversalAction for its height, then for the clip's duration moves the soldier from
    /// start to end along the clip's forward progress and lifts the model so the clip's feet match the real
    /// obstacle height. The NavMeshAgent is off during the move and warped to the end point afterwards.
    /// The rifle is slung on the back while the hands are busy.
    /// </summary>
    [RequireComponent(typeof(SoldierMotor), typeof(NavMeshAgent))]
    public class SoldierTraversal : MonoBehaviour
    {
        [SerializeField] TraversalAction[] actions;
        [SerializeField] Animator animator;

        [Header("Detection")]
        [Tooltip("How far ahead an obstacle can be, in metres.")]
        [SerializeField] float reach = 1.3f;
        [SerializeField] float maxHeight = 2.3f;
        [Tooltip("Tops thinner than this can be vaulted, in metres.")]
        [SerializeField] float maxVaultDepth = 1.3f;
        [Tooltip("Vault only when moving at least this fast (m/s); standing still climbs on top instead.")]
        [SerializeField] float minVaultSpeed = 0.8f;
        [SerializeField] float minDrop = 0.3f;
        [SerializeField] float maxDrop = 2.6f;
        [Tooltip("Walking off an edge this low hops down on its own (no Jump needed), in metres.")]
        [SerializeField] float autoDropMax = 1.0f;
        [Tooltip("How close to the edge the auto hop-down starts, in metres.")]
        [SerializeField] float autoDropEdge = 0.45f;
        [SerializeField] LayerMask obstacleMask = ~0;

        [Header("Warp")]
        [Tooltip("Extra height the feet clear over a vaulted obstacle, in metres.")]
        [SerializeField] float vaultClearance = 0.15f;
        [SerializeField] float startBlend = 0.1f;

        [Header("Slung rifle: diagonal across the back, muzzle up behind the right shoulder")]
        [Tooltip("Grip position relative to the upper chest, in the soldier's frame (x right, y up, z forward).")]
        [SerializeField] Vector3 slingOffset = new Vector3(-0.12f, -0.28f, -0.2f);
        [Tooltip("How far the barrel leans toward the right shoulder (0 = straight up).")]
        [SerializeField] float slingTilt = 0.45f;

        [Header("Debug")]
        [Tooltip("Log each traversal decision to the console.")]
        [SerializeField] bool logMoves;

        NavMeshAgent agent;
        SoldierMotor motor;
        Transform model;
        Transform chest;
        Transform rightHand;
        Transform rifle;
        Vector3 rifleLocalPosition;
        Quaternion rifleLocalRotation;
        Vector3 rifleLocalScale;

        TraversalAction current;
        Vector3 start, end;
        float obstacleHeight;
        float elapsed;
        float settleElapsed, settleTime, settleOffset;
        bool settling;

        /// <summary>A move is playing (or its landing is still settling). Movement, aim and fire are off.</summary>
        public bool IsActive => current != null || settling;
        public TraversalKind? CurrentKind => current != null ? current.Kind : null;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            motor = GetComponent<SoldierMotor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            model = animator.transform;
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var effects = GetComponentInChildren<WeaponEffects>(true);
            if (effects != null)
            {
                rifle = effects.transform;
                rifleLocalPosition = rifle.localPosition;
                rifleLocalRotation = rifle.localRotation;
                rifleLocalScale = rifle.localScale;
            }
        }

        /// <summary>
        /// Tries a traversal toward direction (world, flat). False: nothing to traverse, jump instead.
        /// preferVault: vault even when standing still (jumping out of cover goes over it, not onto it).
        /// </summary>
        public bool TryBegin(Vector3 direction, bool preferVault = false)
        {
            bool moving = preferVault || motor.Velocity.sqrMagnitude >= minVaultSpeed * minVaultSpeed;
            if (IsActive || actions == null || !agent.enabled || !agent.isOnNavMesh) return false;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            Vector3 feet = transform.position;

            if (TraversalProbe.Scan(transform, feet, direction, reach, maxHeight, maxVaultDepth, obstacleMask, out TraversalScan scan))
            {
                if (logMoves)
                    Debug.Log($"[Traversal] {name}: obstacle h={scan.Height:F2} depth={(scan.Depth > 100f ? "deep" : scan.Depth.ToString("F2"))} vault={scan.CanVault} mantle={scan.CanMantle}");
                if (scan.CanVault && (moving || !scan.CanMantle) && TryGetAction(TraversalKind.Vault, scan.Height, out TraversalAction vault))
                    return Begin(vault, scan.VaultLanding, scan.Height + vaultClearance);
                if (scan.CanMantle && TryGetAction(TraversalKind.Mantle, scan.Height, out TraversalAction mantle))
                    return Begin(mantle, scan.MantleEnd, scan.Height);
            }

            if (TraversalProbe.FindDrop(transform, feet, direction, 1.2f, minDrop, maxDrop, obstacleMask, out Vector3 landing, out float drop)
                && TryGetAction(TraversalKind.Drop, drop, out TraversalAction dropAction))
                return Begin(dropAction, landing, drop);

            return false;
        }

        float nextAutoCheck;

        /// <summary>
        /// Walking into a low edge (up to autoDropMax) hops down without pressing Jump, like stepping off a
        /// ledge in a real game. Higher drops still need Jump. Checked a few times per second.
        /// </summary>
        public bool TryAutoStepDown(Vector3 direction)
        {
            if (IsActive || actions == null || !agent.enabled || !agent.isOnNavMesh || Time.time < nextAutoCheck) return false;
            nextAutoCheck = Time.time + 0.1f;
            if (direction.sqrMagnitude < 0.01f) return false;

            if (TraversalProbe.FindDrop(transform, transform.position, direction, autoDropEdge, minDrop, autoDropMax, obstacleMask,
                                        out Vector3 landing, out float drop)
                && TryGetAction(TraversalKind.Drop, drop, out TraversalAction dropAction))
                return Begin(dropAction, landing, drop);
            return false;
        }

        bool TryGetAction(TraversalKind kind, float height, out TraversalAction action)
        {
            foreach (TraversalAction a in actions)
            {
                if (a != null && a.Kind == kind && height >= a.MinHeight && height <= a.MaxHeight)
                {
                    action = a;
                    return true;
                }
            }
            action = null;
            return false;
        }

        bool Begin(TraversalAction action, Vector3 endPoint, float height)
        {
            if (logMoves) Debug.Log($"[Traversal] {name}: {action.name} height {height:F2} to {endPoint}");
            current = action;
            start = transform.position;
            end = endPoint;
            obstacleHeight = height;
            elapsed = 0f;

            Vector3 flat = end - start;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat);

            agent.enabled = false;
            motor.StopMoving();
            SlingRifle(true);

            animator.CrossFadeInFixedTime(action.StateName, startBlend, 0);
            int upperBody = animator.GetLayerIndex("UpperBody");
            if (upperBody >= 0) animator.CrossFadeInFixedTime("Empty", startBlend, upperBody);
            return true;
        }

        /// <summary>Advances the move. Called by Soldier instead of the motor while IsActive.</summary>
        public void Tick(float dt)
        {
            if (current == null)
            {
                Settle(dt);
                return;
            }

            elapsed += dt;
            float u = Mathf.Clamp01(elapsed / current.Duration);
            float forward = current.ForwardAt(u);
            float foot = current.FootAt(u);

            // Unclamped: some clips overshoot forward and settle back.
            Vector3 position = Vector3.LerpUnclamped(start, end, forward);
            position.y = start.y;
            transform.position = position;
            // The clip's own start height (FootBase) fades in with the start crossfade; cancel it the same way.
            float baseWeight = startBlend > 0f ? Mathf.Clamp01(elapsed / startBlend) : 1f;
            model.localPosition = Vector3.up * (VerticalOffset(forward, foot) - current.FootBase * baseWeight);

            if (u >= 1f) Finish();
        }

        /// <summary>Extra model height on top of the clip's own (baked) foot height.</summary>
        float VerticalOffset(float forward, float foot)
        {
            float rise = end.y - start.y;
            switch (current.Warp)
            {
                case TraversalWarp.ScaleToPeak:
                    return foot * (obstacleHeight / SafeKey(current.KeyHeight)) - foot + rise * Mathf.Clamp01(forward);
                case TraversalWarp.ScaleToEnd:
                    // Works for climbs (key > 0) and drops (key < 0): the clip's end height maps to the real one.
                    return foot * (rise / SafeKey(current.KeyHeight)) - foot;
                default:
                    return rise * Mathf.SmoothStep(0f, 1f, forward);
            }
        }

        static float SafeKey(float key) => Mathf.Abs(key) >= 0.05f ? key : (key < 0f ? -0.05f : 0.05f);

        void Finish()
        {
            // The clip's last frame may still hold the feet above the root; that fades out over the
            // crossfade, so push the model down by the same amount and ease it back.
            settleOffset = -(current.FootBase + current.FootAt(1f));
            settleTime = current.EndBlend;
            settleElapsed = 0f;
            settling = true;

            transform.position = end;
            model.localPosition = Vector3.up * settleOffset;
            agent.enabled = true;
            agent.Warp(end);
            SlingRifle(false);
            animator.CrossFadeInFixedTime("Stand", current.EndBlend, 0);
            current = null;
        }

        void Settle(float dt)
        {
            if (!settling) return;
            settleElapsed += dt;
            float w = settleTime > 0f ? Mathf.Clamp01(settleElapsed / settleTime) : 1f;
            model.localPosition = Vector3.up * settleOffset * (1f - w);
            if (w >= 1f) settling = false;
        }

        void SlingRifle(bool slung)
        {
            if (rifle == null) return;
            if (slung)
            {
                if (rifle.parent != rightHand) return;
                rifle.SetParent(chest, true);
                rifle.position = chest.position + transform.rotation * slingOffset;
                Vector3 barrel = (transform.up + transform.right * slingTilt).normalized;
                rifle.rotation = Quaternion.LookRotation(barrel, -transform.forward);
            }
            else if (rifle.parent == chest)
            {
                rifle.SetParent(rightHand, false);
                rifle.localPosition = rifleLocalPosition;
                rifle.localRotation = rifleLocalRotation;
                rifle.localScale = rifleLocalScale;
            }
        }
    }
}
