using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Sends movement, stance, idle and weapon events to the Animator. Blend tree positions are in m/s.
    /// </summary>
    [RequireComponent(typeof(SoldierMotor))]
    public class SoldierAnimator : MonoBehaviour
    {
        static readonly int MoveXId = Animator.StringToHash("MoveX");
        static readonly int MoveYId = Animator.StringToHash("MoveY");
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int AimingId = Animator.StringToHash("Aiming");
        static readonly int FireId = Animator.StringToHash("Fire");
        static readonly int ReloadId = Animator.StringToHash("Reload");
        static readonly int HitId = Animator.StringToHash("Hit");
        static readonly int AimBlendId = Animator.StringToHash("AimBlend");
        static readonly int IdleVariantId = Animator.StringToHash("IdleVariant");
        static readonly int InCoverId = Animator.StringToHash("InCover");
        static readonly int CoverLowId = Animator.StringToHash("CoverLow");
        static readonly int CoverMirrorId = Animator.StringToHash("CoverMirror");

        /// <summary>Mirrored high cover state (looks out left, rifle in the left hand). WeaponGrip follows it.</summary>
        public const string CoverMirrorState = "CoverHighMirror";

        /// <summary>Relaxed idle clips in the Animator's RelaxedIdle blend (thresholds 0..count-1).</summary>
        public const int RelaxedIdleCount = 3;
        /// <summary>Arms-only layer that carries the rifle low while moving without aiming.</summary>
        public const string RelaxedArmsLayer = "RelaxedArms";

        [SerializeField] Animator animator;

        [Header("Smoothing")]
        [Tooltip("Damping time for MoveX / MoveY / Speed, in seconds.")]
        [SerializeField] float moveDamping = 0.1f;

        [Header("Relaxed idle")]
        [Tooltip("Seconds between switching relaxed idle clips (min, max).")]
        [SerializeField] Vector2 idleSwitchTime = new Vector2(10f, 20f);
        [Tooltip("Crossfade time between relaxed idle clips, in seconds.")]
        [SerializeField] float idleCrossfade = 0.6f;

        [Header("Relaxed arms while moving")]
        [Tooltip("Speed at which the relaxed arms are fully on, m/s.")]
        [SerializeField] float relaxedArmsSpeed = 0.5f;
        [Tooltip("How fast the relaxed arms fade, weight per second.")]
        [SerializeField] float relaxedArmsBlend = 5f;

        SoldierMotor motor;
        SoldierWeapon weapon;
        SoldierAim aim;
        Health health;
        int idleVariant;
        float nextIdleSwitch;
        int relaxedArmsLayer;
        float relaxedArmsWeight;

        void Awake()
        {
            motor = GetComponent<SoldierMotor>();
            weapon = GetComponent<SoldierWeapon>();
            aim = GetComponent<SoldierAim>();
            health = GetComponent<Health>();
            idleVariant = Random.Range(0, RelaxedIdleCount);
            nextIdleSwitch = Time.time + Random.Range(idleSwitchTime.x, idleSwitchTime.y);
            if (animator == null) animator = GetComponentInChildren<Animator>();
            relaxedArmsLayer = animator.GetLayerIndex(RelaxedArmsLayer);

            // Movement comes from the NavMeshAgent only. Off-screen soldiers keep animating
            // so aim, muzzle and head positions stay correct for AI.
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        void OnEnable()
        {
            if (weapon != null)
            {
                weapon.Fired += OnFired;
                weapon.ReloadStarted += OnReloadStarted;
            }
            if (health != null) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
                weapon.ReloadStarted -= OnReloadStarted;
            }
            if (health != null) health.Damaged -= OnDamaged;
        }

        void OnFired() => animator.SetTrigger(FireId);
        void OnReloadStarted() => animator.SetTrigger(ReloadId);

        void OnDamaged(DamageInfo damage)
        {
            if (!health.IsDead) animator.SetTrigger(HitId);
        }

        public void Tick(in SoldierCommand command, float dt)
        {
            Vector3 v = motor.LocalVelocity;
            animator.SetFloat(MoveXId, v.x, moveDamping, dt);
            animator.SetFloat(MoveYId, v.z, moveDamping, dt);
            animator.SetFloat(SpeedId, v.magnitude, moveDamping, dt);
            // Low cover: kneel facing the cover with the rifle in hand (crouch clips); only high cover uses
            // the back-to-wall cover state.
            animator.SetBool(CrouchId, motor.IsCrouched || motor.IsCoverLow);
            animator.SetBool(InCoverId, motor.IsInCover && !motor.IsCoverLow);
            animator.SetBool(CoverLowId, false);
            animator.SetBool(CoverMirrorId, motor.IsCoverFaceLeft);
            if (motor.JumpedThisFrame) animator.SetTrigger(JumpId);

            bool reloading = weapon != null && weapon.IsReloading;
            animator.SetBool(AimingId, command.Aim && !reloading);
            animator.SetFloat(AimBlendId, aim != null ? aim.ReadyWeight : 1f);

            if (Time.time >= nextIdleSwitch)
            {
                idleVariant = (idleVariant + Random.Range(1, RelaxedIdleCount)) % RelaxedIdleCount;
                nextIdleSwitch = Time.time + Random.Range(idleSwitchTime.x, idleSwitchTime.y);
            }
            animator.SetFloat(IdleVariantId, idleVariant, idleCrossfade * 0.3f, dt);

            // Moving without aiming: carry the rifle low. Standing still, the relaxed idle does that already;
            // sprinting forward uses its own relaxed-carry clip.
            if (relaxedArmsLayer >= 0)
            {
                float speed = motor.Velocity.magnitude;
                bool sprintForward = motor.IsSprinting && motor.LocalVelocity.z > 0.7f * speed;
                float ready = aim != null ? aim.ReadyWeight : 1f;
                // Not while crouched: the crouch clips hold the rifle well, and standing arms on a leaning
                // torso put the rifle behind the back.
                bool crouched = motor.IsCrouched || motor.IsCoverLow;
                float target = (1f - ready) * Mathf.Clamp01(speed / relaxedArmsSpeed) * (sprintForward || crouched ? 0f : 1f);
                // High cover: the rifle cover clips hold the rifle themselves.
                if (motor.IsInCover) target = 0f;
                relaxedArmsWeight = Mathf.MoveTowards(relaxedArmsWeight, target, relaxedArmsBlend * dt);
                animator.SetLayerWeight(relaxedArmsLayer, relaxedArmsWeight);
            }
        }
    }
}
