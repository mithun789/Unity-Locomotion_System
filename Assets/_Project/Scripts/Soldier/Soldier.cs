using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Root of every soldier. Reads the command from whichever ISoldierInput is attached
    /// (player or AI) and passes it to the body parts. The body never knows who sent it.
    /// </summary>
    [RequireComponent(typeof(SoldierMotor))]
    public class Soldier : MonoBehaviour
    {
        SoldierMotor motor;
        SoldierWeapon weapon;
        SoldierAim aim;
        SoldierAnimator soldierAnimator;
        SoldierTraversal traversal;
        ISoldierInput input;

        public SoldierCommand Command { get; private set; }

        void Awake()
        {
            motor = GetComponent<SoldierMotor>();
            weapon = GetComponent<SoldierWeapon>();
            aim = GetComponent<SoldierAim>();
            soldierAnimator = GetComponent<SoldierAnimator>();
            traversal = GetComponent<SoldierTraversal>();
            input = GetComponent<ISoldierInput>();
            Command = SoldierCommand.Idle(transform);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            SoldierCommand command = input != null ? input.GetCommand() : SoldierCommand.Idle(transform);
            Command = command;

            // Jump next to an obstacle or an edge vaults, mantles or drops instead.
            // In cover, Jump vaults over the cover (toward the wall).
            Vector3 traverseDir = command.InCover ? command.CoverForward : command.MoveWorld(transform.position);
            if (traversal != null && command.Jump && !traversal.IsActive && !motor.IsJumping && !motor.IsCrouched
                && traversal.TryBegin(traverseDir, preferVault: command.InCover))
            {
                command.Jump = false;
                command.InCover = false;
            }
            else if (traversal != null && !command.Jump && command.Move.sqrMagnitude > 0.1f && !motor.IsJumping)
                traversal.TryAutoStepDown(command.MoveWorld(transform.position));

            if (traversal != null && traversal.IsActive)
            {
                traversal.Tick(dt);
                if (soldierAnimator != null) soldierAnimator.Tick(command, dt);
                return;
            }

            motor.Tick(command, dt);

            // Hold the trigger until the rifle is raised from a relaxed idle.
            if (aim != null && !aim.IsReady) command.Fire = false;
            if (weapon != null) weapon.Tick(command, dt);

            if (soldierAnimator != null) soldierAnimator.Tick(command, dt);
        }
    }
}
