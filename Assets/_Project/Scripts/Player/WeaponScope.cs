using UnityEngine;
using UnityEngine.InputSystem;

namespace WarMission
{
    /// <summary>
    /// Rifle scope for the player. Holding aim (right mouse / left trigger) looks through the scope:
    /// ThirdPersonCamera moves to the eye and narrows the field of view to the magnification, ScopeOverlay
    /// draws the lens and reticle, and the body is hidden (shadows stay).
    /// Scroll wheel / D-pad up-down changes magnification. Shift / left stick press holds breath to steady
    /// the sway for a few seconds. V / D-pad left switches between scope and plain shoulder aim.
    /// While scoped: tighter spread, slower movement, and a camera kick per shot.
    /// </summary>
    [RequireComponent(typeof(PlayerSoldierInput))]
    public class WeaponScope : MonoBehaviour
    {
        [Header("Optics")]
        [SerializeField] float[] magnifications = { 2f, 4f, 6f };
        [SerializeField] int startMagnification = 1;
        [Tooltip("Seconds to raise the scope to the eye.")]
        [SerializeField] float scopeInTime = 0.2f;
        [Tooltip("Scope blend (0..1) at which the overlay replaces the third-person view.")]
        [Range(0.3f, 1f)] [SerializeField] float overlayStart = 0.75f;
        [SerializeField] bool scopeOnAim = true;

        [Header("Sway (degrees at the eye)")]
        [SerializeField] float swayAmplitude = 0.4f;
        [SerializeField] float swaySpeed = 0.35f;
        [SerializeField] float movingSwayMultiplier = 2.5f;
        [SerializeField] float crouchedSwayMultiplier = 0.6f;
        [SerializeField] float holdBreathSwayMultiplier = 0.1f;
        [Tooltip("Sway after running out of breath, until it recovers.")]
        [SerializeField] float exhaustedSwayMultiplier = 1.8f;

        [Header("Breath (seconds)")]
        [SerializeField] float breathHoldTime = 4f;
        [SerializeField] float breathRecoverTime = 3f;
        [SerializeField] float exhaustedTime = 2f;

        [Header("Shooting while scoped")]
        [Tooltip("Weapon spread multiplier at full scope.")]
        [SerializeField] float scopedSpreadScale = 0.25f;
        [Tooltip("Camera kick per shot (pitch, random yaw), degrees.")]
        [SerializeField] Vector2 scopedRecoil = new Vector2(0.45f, 0.15f);
        [Tooltip("Move input multiplier at full scope.")]
        [SerializeField] float scopedMoveScale = 0.55f;

        InputAction zoom, zoomIn, zoomOut, holdBreath, toggle;
        PlayerSoldierInput input;
        SoldierWeapon weapon;
        SoldierMotor motor;
        SoldierGrenades grenades;
        SoldierTraversal traversal;
        Health health;
        ScopeBodyHider hider;
        int zoomIndex;
        float nextZoomStep;
        float breath = 1f;
        float exhaustedUntil = float.NegativeInfinity;
        float swayTime;
        float swayScale = 1f;
        Vector2 pendingRecoil;
        float noiseSeed;

        /// <summary>Scope on the rifle in use (V toggles to plain shoulder aim).</summary>
        public bool ScopeEnabled => scopeOnAim;
        /// <summary>0 = third person, 1 = looking through the scope.</summary>
        public float Blend { get; private set; }
        /// <summary>Blend eased for camera position and field of view.</summary>
        public float EasedBlend => Blend * Blend * (3f - 2f * Blend);
        public bool IsScoped => Blend >= overlayStart;
        public float OverlayStart => overlayStart;
        public float Magnification => magnifications.Length > 0 ? magnifications[zoomIndex] : 1f;
        public Vector2 Sway { get; private set; }
        public bool IsHoldingBreath { get; private set; }
        public float BreathRemaining => breath;
        public bool IsExhausted => Time.time < exhaustedUntil;
        /// <summary>Look sensitivity multiplier at full scope (slower at higher magnification).</summary>
        public float SensitivityScale => Mathf.Clamp(1.4f / Magnification, 0.15f, 1f);
        public float MoveScale => Mathf.Lerp(1f, scopedMoveScale, EasedBlend);

        /// <summary>Field of view through the scope for a camera whose normal field of view is baseFov.</summary>
        public float FieldOfView(float baseFov)
        {
            float half = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Magnification);
            return 2f * Mathf.Atan(half) * Mathf.Rad2Deg;
        }

        /// <summary>Camera kick accumulated since the last call (x = yaw, y = pitch up), degrees.</summary>
        public Vector2 ConsumeRecoil()
        {
            Vector2 kick = pendingRecoil;
            pendingRecoil = Vector2.zero;
            return kick;
        }

        void Awake()
        {
            input = GetComponent<PlayerSoldierInput>();
            weapon = GetComponent<SoldierWeapon>();
            motor = GetComponent<SoldierMotor>();
            traversal = GetComponent<SoldierTraversal>();
            health = GetComponent<Health>();
            hider = new ScopeBodyHider(transform);
            zoomIndex = Mathf.Clamp(startMagnification, 0, Mathf.Max(0, magnifications.Length - 1));
            noiseSeed = Random.value * 50f;

            zoom = new InputAction("ScopeZoom", InputActionType.Value, "<Mouse>/scroll/y");
            zoomIn = new InputAction("ScopeZoomIn", InputActionType.Button, "<Gamepad>/dpad/up");
            zoomOut = new InputAction("ScopeZoomOut", InputActionType.Button, "<Gamepad>/dpad/down");
            holdBreath = new InputAction("HoldBreath", InputActionType.Button);
            holdBreath.AddBinding("<Keyboard>/leftShift");
            holdBreath.AddBinding("<Gamepad>/leftStickPress");
            toggle = new InputAction("ScopeToggle", InputActionType.Button);
            toggle.AddBinding("<Keyboard>/v");
            toggle.AddBinding("<Gamepad>/dpad/left");
        }

        void Start() => grenades = GetComponent<SoldierGrenades>();

        void OnEnable()
        {
            zoom.Enable(); zoomIn.Enable(); zoomOut.Enable(); holdBreath.Enable(); toggle.Enable();
            if (weapon != null) weapon.Fired += OnFired;
        }

        void OnDisable()
        {
            zoom.Disable(); zoomIn.Disable(); zoomOut.Disable(); holdBreath.Disable(); toggle.Disable();
            if (weapon != null) weapon.Fired -= OnFired;
            if (weapon != null) weapon.SpreadScale = 1f;
            hider.Show();
        }

        void OnDestroy()
        {
            zoom.Dispose(); zoomIn.Dispose(); zoomOut.Dispose(); holdBreath.Dispose(); toggle.Dispose();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (toggle.WasPressedThisFrame()) scopeOnAim = !scopeOnAim;

            bool busy = (weapon != null && weapon.IsReloading) || (grenades != null && grenades.IsThrowing)
                        || (traversal != null && traversal.IsActive) || (health != null && health.IsDead)
                        || (motor != null && motor.IsSprinting);
            bool want = scopeOnAim && input.AimHeld && !busy;
            Blend = Mathf.MoveTowards(Blend, want ? 1f : 0f, dt / Mathf.Max(0.01f, scopeInTime));

            if (IsScoped) UpdateZoom();
            UpdateBreath(dt);
            UpdateSway(dt);

            if (weapon != null) weapon.SpreadScale = Mathf.Lerp(1f, scopedSpreadScale, EasedBlend);
            if (IsScoped) hider.Hide();
            else hider.Show();
        }

        void UpdateZoom()
        {
            float scroll = zoom.ReadValue<float>();
            int step = 0;
            if (Time.time >= nextZoomStep && Mathf.Abs(scroll) > 0.01f) step = scroll > 0f ? 1 : -1;
            if (zoomIn.WasPressedThisFrame()) step = 1;
            if (zoomOut.WasPressedThisFrame()) step = -1;
            if (step == 0) return;
            nextZoomStep = Time.time + 0.12f;
            zoomIndex = Mathf.Clamp(zoomIndex + step, 0, magnifications.Length - 1);
        }

        void UpdateBreath(float dt)
        {
            bool wantHold = IsScoped && holdBreath.IsPressed() && !IsExhausted;
            if (wantHold && breath > 0f)
            {
                IsHoldingBreath = true;
                breath -= dt / breathHoldTime;
                if (breath <= 0f)
                {
                    breath = 0f;
                    IsHoldingBreath = false;
                    exhaustedUntil = Time.time + exhaustedTime;
                }
                return;
            }
            IsHoldingBreath = false;
            if (!IsExhausted) breath = Mathf.Min(1f, breath + dt / breathRecoverTime);
        }

        /// <summary>Slow figure-eight drift of the aim plus a breathing bob; smaller crouched or holding breath.</summary>
        void UpdateSway(float dt)
        {
            float target = 1f;
            if (motor != null && motor.Velocity.sqrMagnitude > 0.1f) target *= movingSwayMultiplier;
            if (motor != null && (motor.IsCrouched || motor.IsCoverLow)) target *= crouchedSwayMultiplier;
            if (IsHoldingBreath) target *= holdBreathSwayMultiplier;
            else if (IsExhausted) target *= exhaustedSwayMultiplier;
            swayScale = Mathf.Lerp(swayScale, target, 1f - Mathf.Exp(-4f * dt));

            swayTime += dt * swaySpeed;
            float x = (Mathf.PerlinNoise(swayTime, noiseSeed) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(noiseSeed, swayTime) - 0.5f) * 2f;
            float bob = Mathf.Sin(Time.time * Mathf.PI * 0.5f) * 0.35f;   // one breath every 4 s
            Sway = new Vector2(x, y + bob) * (swayAmplitude * swayScale);
        }

        void OnFired()
        {
            if (Blend < 0.5f) return;
            float steady = IsHoldingBreath ? 0.8f : 1f;
            pendingRecoil += new Vector2(Random.Range(-scopedRecoil.y, scopedRecoil.y), scopedRecoil.x) * steady;
        }
    }

    /// <summary>
    /// Hides the soldier's own meshes while the camera sits at the scope (shadows keep rendering).
    /// Tracers and the muzzle flash stay visible.
    /// </summary>
    public class ScopeBodyHider
    {
        readonly Transform root;
        Renderer[] renderers;
        UnityEngine.Rendering.ShadowCastingMode[] modes;
        bool hidden;

        public ScopeBodyHider(Transform root) => this.root = root;

        public void Hide()
        {
            if (hidden) return;
            Collect();
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            hidden = true;
        }

        public void Show()
        {
            if (!hidden) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].shadowCastingMode = modes[i];
            hidden = false;
        }

        void Collect()
        {
            var all = root.GetComponentsInChildren<Renderer>();
            var keep = new System.Collections.Generic.List<Renderer>(all.Length);
            foreach (Renderer r in all)
            {
                if (r is LineRenderer || IsUnder(r.transform, "MuzzleFlash")) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                keep.Add(r);
            }
            renderers = keep.ToArray();
            modes = new UnityEngine.Rendering.ShadowCastingMode[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) modes[i] = renderers[i].shadowCastingMode;
        }

        bool IsUnder(Transform t, string name)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name == name) return true;
            return false;
        }
    }
}
