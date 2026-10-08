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
        static readonly int TurnId = Animator.StringToHash("Turn");
        static readonly int TurnDirId = Animator.StringToHash("TurnDir");
        static readonly int TurnRateId = Animator.StringToHash("TurnRate");
        static readonly int ArmsAimId = Animator.StringToHash("ArmsAim");
        static readonly int TurnTag = Animator.StringToHash(TurnStateTag);

        /// <summary>Tag on the turn-in-place states (standing and crouched).</summary>
        public const string TurnStateTag = "Turn";

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

        [Header("Turn in place (standing still)")]
        [Tooltip("Aim this far off the body's forward starts a 90 degree turn clip, relaxed (degrees). Below it the spine aims.")]
        [SerializeField] float turnAngleRelaxed = 60f;
        [Tooltip("Same, with the rifle up (degrees).")]
        [SerializeField] float turnAngleAimed = 45f;
        [Tooltip("Turn clip playback speed, relaxed and with the rifle up.")]
        [SerializeField] Vector2 turnRate = new Vector2(1f, 1.5f);
        [Tooltip("The turn is scaled to end facing the aim, within this fraction of the clip's own angle.")]
        [SerializeField] float turnScaleRange = 0.25f;

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
        float armsAim;
        SoldierRootMotion rootMotion;

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
            rootMotion = animator.GetComponent<SoldierRootMotion>();
            if (rootMotion == null) rootMotion = animator.gameObject.AddComponent<SoldierRootMotion>();
            rootMotion.Init(motor);

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
            float ready = aim != null ? aim.ReadyWeight : 1f;
            animator.SetFloat(AimBlendId, ready);

            bool turning = UpdateTurnInPlace(ready);

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
                // Not while crouched: the crouch clips hold the rifle well, and standing arms on a leaning
                // torso put the rifle behind the back.
                bool crouched = motor.IsCrouched || motor.IsCoverLow;
                float target = (1f - ready) * Mathf.Clamp01(speed / relaxedArmsSpeed) * (sprintForward || crouched ? 0f : 1f);
                // High cover: the rifle cover clips hold the rifle themselves.
                if (motor.IsInCover) target = 0f;
                // Standing turns: the turn clips carry the rifle differently from the idles, so the arms come
                // from the idle (gun down relaxed, aim idle with the rifle up) and the hold stays the same.
                if (turning && !crouched)
                {
                    target = 1f;
                    armsAim = ready;
                }
                else if (target > 0f) armsAim = 0f;
                animator.SetFloat(ArmsAimId, armsAim);
                relaxedArmsWeight = Mathf.MoveTowards(relaxedArmsWeight, target, relaxedArmsBlend * dt);
                animator.SetLayerWeight(relaxedArmsLayer, relaxedArmsWeight);
            }
        }

        /// <summary>
        /// Standing still: start a 90 degree turn clip when the aim is too far off the body's forward.
        /// The clip's root rotation turns the body (SoldierRootMotion). Returns true while a turn plays.
        /// </summary>
        bool UpdateTurnInPlace(float ready)
        {
            bool turning = IsTurnState(animator.GetCurrentAnimatorStateInfo(0))
                           || (animator.IsInTransition(0) && IsTurnState(animator.GetNextAnimatorStateInfo(0)));
            animator.SetFloat(TurnRateId, Mathf.Lerp(turnRate.x, turnRate.y, ready));
            if (!motor.IsStandingStill)
            {
                animator.ResetTrigger(TurnId);
                return turning;
            }
            float limit = Mathf.Lerp(turnAngleRelaxed, turnAngleAimed, ready);
            if (!turning && Mathf.Abs(motor.AimYawDelta) > limit)
            {
                float dir = Mathf.Sign(motor.AimYawDelta);
                animator.SetFloat(TurnDirId, dir);
                animator.SetTrigger(TurnId);
                float clipAngle = motor.IsCrouched ? SoldierClipSpeeds.CrouchTurnAngle
                                : dir < 0f ? SoldierClipSpeeds.TurnLeftAngle : SoldierClipSpeeds.TurnRightAngle;
                rootMotion.YawScale = Mathf.Clamp(Mathf.Abs(motor.AimYawDelta) / clipAngle, 1f - turnScaleRange, 1f + turnScaleRange);
            }
            return turning;
        }

        static bool IsTurnState(AnimatorStateInfo state) => state.tagHash == TurnTag;
    }
}
