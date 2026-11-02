using UnityEngine;
using UnityEngine.InputSystem;

namespace WarMission
{
    /// <summary>Over-the-shoulder orbit camera with collision. Esc frees the cursor, click locks it again.</summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [Tooltip("Optional. Lowers the camera while the soldier crouches.")]
        [SerializeField] SoldierMotor targetMotor;

        [Header("Framing")]
        [SerializeField] float pivotHeight = 1.6f;
        [SerializeField] float crouchPivotHeight = 1.15f;
        [SerializeField] float shoulderOffset = 0.55f;
        [SerializeField] float distance = 2.6f;
        [Tooltip("How tightly the pivot follows the soldier. Higher = stiffer.")]
        [SerializeField] float followSharpness = 20f;
        [Tooltip("While climbing / dropping the pivot follows the hips (the root stays put): pivot height above the hips.")]
        [SerializeField] float pivotAboveHips = 0.65f;
        [Tooltip("How tightly the pivot follows the hips height during traversal.")]
        [SerializeField] float traversalSharpness = 8f;

        [Header("Aim zoom")]
        [SerializeField] float aimFieldOfView = 45f;
        [Tooltip("How fast the field of view changes, per second (exponential).")]
        [SerializeField] float zoomSharpness = 12f;
        [Tooltip("Look sensitivity multiplier while zoomed.")]
        [SerializeField] float aimSensitivityScale = 0.7f;

        [Header("Scope")]
        [Tooltip("Eye position through the scope, from the pivot in camera space (x right, y up, z forward).")]
        [SerializeField] Vector3 scopeEyeOffset = new Vector3(0.1f, 0.02f, 0.18f);

        [Header("Look")]
        [Tooltip("Degrees per mouse pixel.")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per second at full stick.")]
        [SerializeField] float gamepadSensitivity = 180f;
        [SerializeField] float minPitch = -60f;
        [SerializeField] float maxPitch = 70f;
        [SerializeField] bool invertY;

        [Header("Collision")]
        [SerializeField] float collisionRadius = 0.2f;
        [SerializeField] LayerMask collisionMask = ~0;

        readonly RaycastHit[] hits = new RaycastHit[16];
        Camera viewCamera;
        PlayerSoldierInput targetInput;
        WeaponScope targetScope;
        float thirdPersonFov;
        SoldierTraversal targetTraversal;
        Transform targetHips;
        float normalFieldOfView;
        InputAction mouseLook, stickLook;
        float yaw, pitch, currentPivotHeight;
        Vector3 pivot;

        void Awake()
        {
            mouseLook = new InputAction("MouseLook", InputActionType.Value, "<Mouse>/delta");
            stickLook = new InputAction("StickLook", InputActionType.Value, "<Gamepad>/rightStick");
            if (targetMotor == null && target != null) targetMotor = target.GetComponent<SoldierMotor>();
            if (target != null)
            {
                targetInput = target.GetComponent<PlayerSoldierInput>();
                targetTraversal = target.GetComponent<SoldierTraversal>();
                Animator animator = target.GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman) targetHips = animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            viewCamera = GetComponent<Camera>();
            if (viewCamera != null) normalFieldOfView = viewCamera.fieldOfView;
            thirdPersonFov = normalFieldOfView;
        }

        void OnEnable() { mouseLook.Enable(); stickLook.Enable(); LockCursor(true); }
        void OnDisable() { mouseLook.Disable(); stickLook.Disable(); LockCursor(false); }
        void OnDestroy() { mouseLook.Dispose(); stickLook.Dispose(); }

        void Start()
        {
            if (target == null) return;
            yaw = target.eulerAngles.y;
            currentPivotHeight = pivotHeight;
            pivot = target.position + Vector3.up * pivotHeight;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

            // Scope: added at runtime on older prefabs, so look it up until found.
            if (targetScope == null && targetInput != null) targetInput.TryGetComponent(out targetScope);
            float scope = targetScope != null ? targetScope.EasedBlend : 0f;

            bool zoomed = targetInput != null && targetInput.AimHeld;
            if (viewCamera != null)
            {
                float wantedFov = zoomed ? aimFieldOfView : normalFieldOfView;
                thirdPersonFov = Mathf.Lerp(thirdPersonFov, wantedFov, 1f - Mathf.Exp(-zoomSharpness * dt));
                viewCamera.fieldOfView = scope > 0f
                    ? Mathf.Lerp(thirdPersonFov, targetScope.FieldOfView(normalFieldOfView), scope)
                    : thirdPersonFov;
            }

            UpdateCursorLock();
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float sensitivity = zoomed ? aimSensitivityScale : 1f;
                if (scope > 0f) sensitivity = Mathf.Lerp(sensitivity, targetScope.SensitivityScale, scope);
                Vector2 look = (mouseLook.ReadValue<Vector2>() * mouseSensitivity
                                + stickLook.ReadValue<Vector2>() * (gamepadSensitivity * dt)) * sensitivity;
                yaw += look.x;
                pitch += invertY ? look.y : -look.y;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            if (targetScope != null)
            {
                // Scoped shots kick the view up (the player pulls it back down).
                Vector2 kick = targetScope.ConsumeRecoil();
                yaw += kick.x;
                pitch = Mathf.Clamp(pitch - kick.y, minPitch, maxPitch);
            }

            float blend = 1f - Mathf.Exp(-followSharpness * dt);
            float wantedHeight = targetMotor != null && targetMotor.IsCrouched ? crouchPivotHeight : pivotHeight;
            currentPivotHeight = Mathf.Lerp(currentPivotHeight, wantedHeight, blend * 0.5f);
            Vector3 wanted = target.position + Vector3.up * currentPivotHeight;
            if (targetTraversal != null && targetTraversal.IsActive && targetHips != null)
            {
                // Climbs and drops move the body, not the root: follow the hips so the soldier stays in frame.
                float y = Mathf.Lerp(pivot.y, targetHips.position.y + pivotAboveHips, 1f - Mathf.Exp(-traversalSharpness * dt));
                pivot = Vector3.Lerp(pivot, wanted, blend);
                pivot.y = y;
            }
            else
            {
                pivot = Vector3.Lerp(pivot, wanted, blend);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 shoulder = pivot + rotation * Vector3.right * CastDistance(pivot, rotation * Vector3.right, shoulderOffset);
            Vector3 back = -(rotation * Vector3.forward);
            Vector3 position = shoulder + back * CastDistance(shoulder, back, distance);

            if (scope > 0f)
            {
                // Move to the eye behind the scope; sway moves the view (and so the aim point) around.
                Vector2 sway = targetScope.Sway * scope;
                Quaternion scoped = Quaternion.Euler(pitch - sway.y, yaw + sway.x, 0f);
                position = Vector3.Lerp(position, pivot + rotation * scopeEyeOffset, scope);
                rotation = Quaternion.Slerp(rotation, scoped, scope);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>How far the camera can go from origin along dir before hitting something that is not the target.</summary>
        float CastDistance(Vector3 origin, Vector3 dir, float maxDistance)
        {
            int count = Physics.SphereCastNonAlloc(origin, collisionRadius, dir, hits, maxDistance, collisionMask, QueryTriggerInteraction.Ignore);
            float result = maxDistance;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(target) || hits[i].distance <= 0f) continue;
                result = Mathf.Min(result, hits[i].distance);
            }
            return result;
        }

        void UpdateCursorLock()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LockCursor(false);
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) LockCursor(true);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
