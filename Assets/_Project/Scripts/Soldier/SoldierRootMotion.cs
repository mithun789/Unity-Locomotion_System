using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Sits on the Animator's object. Root position is ignored (the motor moves the soldier); root rotation
    /// is passed to the motor so turn-in-place clips turn the body as the feet step.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class SoldierRootMotion : MonoBehaviour
    {
        static readonly int TurnTag = Animator.StringToHash(SoldierAnimator.TurnStateTag);

        Animator animator;
        SoldierMotor motor;

        /// <summary>Scales the turn clip's rotation so the turn ends facing the aim (set when a turn starts).</summary>
        public float YawScale { get; set; } = 1f;

        public void Init(SoldierMotor soldierMotor)
        {
            animator = GetComponent<Animator>();
            motor = soldierMotor;
        }

        void OnAnimatorMove()
        {
            if (motor == null) return;
            Quaternion delta = animator.deltaRotation;
            float yaw = delta.eulerAngles.y;
            if (yaw > 180f) yaw -= 360f;
            if (Mathf.Abs(yaw) < 0.0001f) return;

            // Crossfades weight the turn clip's rotation down; undo that so a full turn stays a full turn.
            float weight = 1f;
            if (animator.IsInTransition(0))
            {
                bool fromTurn = animator.GetCurrentAnimatorStateInfo(0).tagHash == TurnTag;
                bool toTurn = animator.GetNextAnimatorStateInfo(0).tagHash == TurnTag;
                float t = animator.GetAnimatorTransitionInfo(0).normalizedTime;
                if (toTurn && !fromTurn) weight = t;
                else if (fromTurn && !toTurn) weight = 1f - t;
            }
            motor.ApplyRootYaw(Quaternion.AngleAxis(yaw * YawScale / Mathf.Max(weight, 0.25f), Vector3.up));
        }
    }
}
