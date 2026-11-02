using UnityEngine;
using UnityEngine.InputSystem;

namespace WarMission
{
    /// <summary>
    /// ISoldierInput for the player. Keyboard/mouse and gamepad.
    /// Move is relative to the aim direction, which follows the camera, so movement is camera-relative.
    /// </summary>
    [RequireComponent(typeof(PlayerCover))]
    public class PlayerSoldierInput : MonoBehaviour, ISoldierInput
    {
        [Header("Aim")]
        [SerializeField] Camera aimCamera;
        [SerializeField] float maxAimDistance = 200f;
        [SerializeField] LayerMask aimMask = ~0;

        [Tooltip("Seconds the rifle stays raised after the last shot. Right mouse / left trigger holds it up.")]
        [SerializeField] float readyAfterFiring = 3f;

        [Header("Stance")]
        [Tooltip("On: crouch key toggles. Off: hold to crouch.")]
        [SerializeField] bool crouchToggle = true;

        readonly RaycastHit[] aimHits = new RaycastHit[16];
        InputAction move, aim, fire, reload, crouch, sprint, jump, takeCover, grenade;
        PlayerCover cover;
        WeaponScope scope;
        bool crouched;
        float lastFireTime = float.NegativeInfinity;

        /// <summary>Aim button held (camera zoom). Command.Aim also stays on briefly after firing.</summary>
        public bool AimHeld => aim != null && aim.IsPressed();

        void Awake()
        {
            if (aimCamera == null) aimCamera = Camera.main;
            cover = GetComponent<PlayerCover>();
            // Older prefabs: the scope parts are added here until Build Soldier Prefab is re-run.
            if (!TryGetComponent(out scope)) scope = gameObject.AddComponent<WeaponScope>();
            if (!TryGetComponent(out ScopeOverlay _)) gameObject.AddComponent<ScopeOverlay>();

            move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            aim = Button("Aim", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            fire = Button("Fire", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            reload = Button("Reload", "<Keyboard>/r", "<Gamepad>/buttonWest");
            crouch = Button("Crouch", "<Keyboard>/c", "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast");
            sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            takeCover = Button("Cover", "<Keyboard>/q", "<Gamepad>/rightShoulder");
            grenade = Button("Grenade", "<Keyboard>/g", "<Gamepad>/buttonNorth");
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string binding in bindings) action.AddBinding(binding);
            return action;
        }

        void OnEnable()
        {
            move.Enable(); aim.Enable(); fire.Enable(); reload.Enable(); crouch.Enable(); sprint.Enable(); jump.Enable(); takeCover.Enable(); grenade.Enable();
        }

        void OnDisable()
        {
            move.Disable(); aim.Disable(); fire.Disable(); reload.Disable(); crouch.Disable(); sprint.Disable(); jump.Disable(); takeCover.Disable(); grenade.Disable();
        }

        void OnDestroy()
        {
            move.Dispose(); aim.Dispose(); fire.Dispose(); reload.Dispose(); crouch.Dispose(); sprint.Dispose(); jump.Dispose(); takeCover.Dispose(); grenade.Dispose();
        }

        public SoldierCommand GetCommand()
        {
            Vector2 moveInput = move.ReadValue<Vector2>();

            if (crouchToggle)
            {
                if (crouch.WasPressedThisFrame()) crouched = !crouched;
            }
            else
            {
                crouched = crouch.IsPressed();
            }

            bool firing = fire.IsPressed();
            if (firing) lastFireTime = Time.time;

            // Fire or aim cancels the sprint (the rifle cannot aim while sprinting).
            bool sprinting = sprint.IsPressed() && moveInput.sqrMagnitude > 0.01f && !firing && !aim.IsPressed();
            if (sprinting) crouched = false;

            var command = new SoldierCommand
            {
                Move = moveInput,
                AimPoint = GetAimPoint(),
                Aim = aim.IsPressed() || Time.time - lastFireTime < readyAfterFiring,
                Fire = firing,
                Reload = reload.WasPressedThisFrame(),
                Crouch = crouched,
                Sprint = sprinting,
                Jump = jump.WasPressedThisFrame(),
                Grenade = grenade.WasPressedThisFrame(),
            };
            ApplyCover(ref command);
            // Looking through the scope: slow, careful steps.
            if (scope != null) command.Move *= scope.MoveScale;
            return command;
        }

        /// <summary>
        /// Cover key snaps to a wall (move direction, or where the camera looks) or leaves cover. In cover the
        /// soldier keeps its back to the wall; aiming or firing pops up / out and it returns on release.
        /// </summary>
        void ApplyCover(ref SoldierCommand command)
        {
            Vector3 moveWorld = command.MoveWorld(transform.position);
            if (takeCover.WasPressedThisFrame())
            {
                if (cover.IsActive)
                {
                    cover.Exit();
                }
                else
                {
                    Vector3 look = aimCamera.transform.forward;
                    cover.TryEnter(moveWorld.sqrMagnitude > 0.01f ? moveWorld : new Vector3(look.x, 0f, look.z));
                }
            }

            bool poppedOut = aim.IsPressed() || fire.IsPressed();
            if (cover.IsActive)
            {
                // Tall cover: aiming steps out past the nearest edge; releasing steps back in.
                if (poppedOut && !cover.IsLow && !cover.IsPeeking && !cover.IsReturning) cover.BeginPeek();
                if (!poppedOut) cover.EndPeek();
            }

            command.Move = cover.Tick(command.Move, moveWorld, command.Sprint);
            if (!cover.IsActive) return;

            command.Sprint = false;
            command.Crouch = false;
            command.CoverLow = cover.IsLow;
            command.CoverForward = cover.Forward;
            command.CoverAnchor = cover.Anchor;
            command.CoverFaceLeft = cover.FaceLeft;

            if (cover.IsPeeking || cover.IsReturning)
            {
                command.InCover = false;
                command.Move = MoveToward(cover.IsPeeking ? cover.PeekTarget : cover.ReturnTarget, command.AimPoint);
                return;
            }
            // Low cover: pop up to shoot over it. Tall cover away from an edge: stand and aim in place.
            command.InCover = !poppedOut;
        }

        Vector2 MoveToward(Vector3 target, Vector3 aimPoint)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0025f) return Vector2.zero;
            Vector3 dir = to.normalized * Mathf.Clamp01(to.magnitude * 3f);
            return SoldierCommand.WorldToMove(dir, transform.position, aimPoint);
        }

        /// <summary>First thing under the screen center that is not this soldier.</summary>
        Vector3 GetAimPoint()
        {
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, aimHits, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore);

            float closest = maxAimDistance;
            Vector3 point = ray.GetPoint(maxAimDistance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = aimHits[i];
                if (hit.distance < closest && !hit.transform.IsChildOf(transform))
                {
                    closest = hit.distance;
                    point = hit.point;
                }
            }
            return point;
        }
    }
}
