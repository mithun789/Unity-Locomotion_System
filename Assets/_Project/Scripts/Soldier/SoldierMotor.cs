using System;
using UnityEngine;
using UnityEngine.AI;

namespace WarMission
{
    /// <summary>
    /// Moves the soldier through its NavMeshAgent and turns it to face the aim point.
    /// Movement is relative to the aim direction, so the soldier strafes while facing the target.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class SoldierMotor : MonoBehaviour
    {
        [Header("Speeds (m/s). Defaults come from SoldierClipSpeeds so feet do not slide.")]
        [SerializeField] DirectionalSpeed walk = DirectionalSpeed.Scaled(SoldierClipSpeeds.WalkPlayback,
            SoldierClipSpeeds.WalkForward, SoldierClipSpeeds.WalkBackward, SoldierClipSpeeds.WalkRight, SoldierClipSpeeds.WalkRight);
        [SerializeField] DirectionalSpeed run = DirectionalSpeed.Scaled(SoldierClipSpeeds.RunPlayback,
            SoldierClipSpeeds.RunForward, SoldierClipSpeeds.RunBackward, SoldierClipSpeeds.RunRight, SoldierClipSpeeds.RunLeft);
        [SerializeField] DirectionalSpeed crouch = Crouch;

        /// <summary>Crouch speeds the Animator blend tree is built for.</summary>
        public static DirectionalSpeed Crouch => new DirectionalSpeed(SoldierClipSpeeds.CrouchSpeed,
            SoldierClipSpeeds.CrouchBackward, SoldierClipSpeeds.CrouchSpeed, SoldierClipSpeeds.CrouchSpeed);

        [Header("Response")]
        [Tooltip("How fast velocity changes, m/s per second.")]
        [SerializeField] float acceleration = 8f;
        [Tooltip("How fast the body turns toward the aim point, degrees per second.")]
        [SerializeField] float turnSpeed = 540f;

        [Header("Turn in place")]
        [Tooltip("Standing still, the body keeps its facing (feet planted) and the spine aims. SoldierAnimator plays a turn clip past its angle; the clip's root rotation turns the body.")]
        [SerializeField] bool turnInPlace = true;
        [Tooltip("Below this speed with no move input the soldier counts as standing still, m/s.")]
        [SerializeField] float standStillSpeed = 0.2f;

        [Header("Cover")]
        [Tooltip("How strongly the soldier is pulled to the cover anchor's distance from the wall.")]
        [SerializeField] float coverPull = 5f;

        [Header("Jump")]
        [Tooltip("Seconds after takeoff during which move input is ignored and velocity is kept. The jump height comes from the animation.")]
        [SerializeField] float jumpLockTime = 0.65f;

        NavMeshAgent agent;
        float jumpEndTime;

        /// <summary>Current world-space velocity.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Velocity in the soldier's local space (x = right, z = forward).</summary>
        public Vector3 LocalVelocity { get; private set; }

        public bool IsCrouched { get; private set; }

        public bool IsJumping { get; private set; }

        /// <summary>Running with Sprint held (not crouched, actually moving).</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>Back against cover (from the command), facing the cover.</summary>
        public bool IsInCover { get; private set; }
        public bool IsCoverLow { get; private set; }
        /// <summary>High cover looking out to the left: mirrored cover pose, rifle in the left hand.</summary>
        public bool IsCoverFaceLeft { get; private set; }

        /// <summary>No move input and (almost) no velocity: the feet stay planted and turn clips turn the body.</summary>
        public bool IsStandingStill { get; private set; }

        /// <summary>Signed yaw from the body's forward to the aim point, degrees (positive = right).</summary>
        public float AimYawDelta { get; private set; }

        /// <summary>True only on the frame a jump starts.</summary>
        public bool JumpedThisFrame { get; private set; }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
        }

        public void Tick(in SoldierCommand command, float dt)
        {
            Vector3 coverForward = command.CoverForward;
            coverForward.y = 0f;
            IsInCover = command.InCover && !IsJumping && coverForward.sqrMagnitude > 0.01f;
            IsCoverLow = IsInCover && command.CoverLow;
            IsCoverFaceLeft = IsInCover && !IsCoverLow && command.CoverFaceLeft;
            IsCrouched = command.Crouch && !command.Sprint && !IsInCover;

            Vector3 toAim = command.AimPoint - transform.position;
            toAim.y = 0f;
            Quaternion aimRotation = toAim.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toAim) : transform.rotation;
            // Low cover: kneel facing the cover. High cover: back against it (the rifle cover clips face away).
            Quaternion facing = !IsInCover ? aimRotation
                              : Quaternion.LookRotation(IsCoverLow ? coverForward : -coverForward);
            AimYawDelta = Vector3.SignedAngle(transform.forward, aimRotation * Vector3.forward, Vector3.up);
            IsStandingStill = turnInPlace && !IsInCover && !IsJumping && !command.Jump
                              && command.Move.sqrMagnitude < 0.01f && Velocity.sqrMagnitude < standStillSpeed * standStillSpeed;
            // Standing still: no sliding turn. ApplyRootYaw (turn clips) turns the body instead.
            if (!IsStandingStill)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * dt);

            JumpedThisFrame = false;
            if (IsJumping && Time.time >= jumpEndTime) IsJumping = false;
            if (command.Jump && !IsJumping && !IsCrouched)
            {
                IsJumping = true;
                JumpedThisFrame = true;
                jumpEndTime = Time.time + jumpLockTime;
            }

            if (IsInCover)
            {
                Velocity = Vector3.MoveTowards(Velocity, CoverVelocity(command, aimRotation, coverForward.normalized), acceleration * dt);
                IsSprinting = false;
            }
            // Airborne: keep the takeoff velocity.
            else if (!IsJumping)
            {
                Vector2 move = Vector2.ClampMagnitude(command.Move, 1f);
                Vector3 desired = Vector3.zero;
                if (move.sqrMagnitude > 0.0001f)
                {
                    Vector3 localDir = new Vector3(move.x, 0f, move.y).normalized;
                    DirectionalSpeed speeds = IsCrouched ? crouch : command.Sprint ? run : walk;
                    desired = aimRotation * localDir * (speeds.Evaluate(localDir) * move.magnitude);
                }
                Velocity = Vector3.MoveTowards(Velocity, desired, acceleration * dt);
                IsSprinting = command.Sprint && !IsCrouched && desired.sqrMagnitude > 0.01f;
            }
            if (agent.isOnNavMesh && Velocity.sqrMagnitude > 0.000001f)
                agent.Move(Velocity * dt);

            LocalVelocity = transform.InverseTransformDirection(Velocity);
        }

        /// <summary>Slide along the wall (Move projected on the wall) and stay at the anchor's distance from it.</summary>
        Vector3 CoverVelocity(in SoldierCommand command, Quaternion aimRotation, Vector3 coverForward)
        {
            Vector3 along = Vector3.Cross(Vector3.up, coverForward);
            Vector2 move = Vector2.ClampMagnitude(command.Move, 1f);
            float side = Vector3.Dot(aimRotation * new Vector3(move.x, 0f, move.y), along);
            float speed = IsCoverLow ? SoldierClipSpeeds.CoverSneakLow : SoldierClipSpeeds.CoverSneakHigh;
            float gap = Vector3.Dot(command.CoverAnchor - transform.position, coverForward);
            return along * (Mathf.Clamp(side, -1f, 1f) * speed) + coverForward * Mathf.Clamp(gap * coverPull, -2f, 2f);
        }

        /// <summary>Root rotation from the Animator (turn-in-place clips; every other clip has it baked).</summary>
        public void ApplyRootYaw(Quaternion deltaRotation)
        {
            float yaw = deltaRotation.eulerAngles.y;
            if (yaw > 180f) yaw -= 360f;
            if (Mathf.Abs(yaw) > 0.0001f)
                transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * transform.rotation;
        }

        /// <summary>Zero the velocity and end any jump (used when traversal takes over).</summary>
        public void StopMoving()
        {
            Velocity = Vector3.zero;
            LocalVelocity = Vector3.zero;
            IsJumping = false;
            IsSprinting = false;
        }

        /// <summary>Top speed per direction. Values between the four axes follow an ellipse.</summary>
        [Serializable]
        public struct DirectionalSpeed
        {
            public float Forward;
            public float Backward;
            public float Right;
            public float Left;

            public DirectionalSpeed(float forward, float backward, float right, float left)
            {
                Forward = forward;
                Backward = backward;
                Right = right;
                Left = left;
            }

            public static DirectionalSpeed Scaled(float scale, float forward, float backward, float right, float left) =>
                new DirectionalSpeed(forward * scale, backward * scale, right * scale, left * scale);

            /// <summary>Speed for a normalized local direction (x = right, z = forward).</summary>
            public float Evaluate(Vector3 localDir)
            {
                float side = localDir.x >= 0f ? Right : Left;
                float front = localDir.z >= 0f ? Forward : Backward;
                float x = localDir.x / Mathf.Max(side, 0.01f);
                float z = localDir.z / Mathf.Max(front, 0.01f);
                return 1f / Mathf.Sqrt(x * x + z * z);
            }
        }
    }
}
