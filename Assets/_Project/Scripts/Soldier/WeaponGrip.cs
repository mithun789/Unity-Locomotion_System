using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Places the rifle from both animated hands each frame: grip at the right palm, barrel through the
    /// left palm. Each Mixamo clip holds its (invisible) rifle with a different wrist angle, so a rifle
    /// fixed to the right hand points off in some clips; following both palms fits every clip and keeps
    /// the left hand on the rifle without IK. With weight 0 (reload, left hand away) it falls back to
    /// the fixed right hand hold. In the mirrored high cover pose the hands swap: grip at the left palm,
    /// barrel through the right palm (blended with the Animator's crossfade). Called by SoldierAim before
    /// it aims the spine.
    /// </summary>
    public class WeaponGrip : MonoBehaviour
    {
        static readonly int MirrorStateId = Animator.StringToHash(SoldierAnimator.CoverMirrorState);

        [SerializeField] Animator animator;
        [Tooltip("Rifle root, a child of the right hand bone.")]
        [SerializeField] Transform rifle;
        [Tooltip("Palms closer than this give no usable barrel direction, in metres.")]
        [SerializeField] float minHandGap = 0.12f;

        Transform rightHand, rightMiddle, leftHand, leftMiddle;
        Quaternion fixedLocalRotation;
        Vector3 fixedLocalPosition;
        Vector3 gripLocal; // right palm relative to the rifle root, in rifle axes (unscaled): the grip point

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            rightMiddle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            leftMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            if (rifle != null)
            {
                fixedLocalRotation = rifle.localRotation;
                fixedLocalPosition = rifle.localPosition;
                gripLocal = Quaternion.Inverse(rifle.rotation) * ((rightHand.position + rightMiddle.position) * 0.5f - rifle.position);
            }
        }

        /// <summary>0..1: how much of the Animator's base layer is in the mirrored cover state.</summary>
        public float LeftHandWeight
        {
            get
            {
                float current = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == MirrorStateId ? 1f : 0f;
                if (!animator.IsInTransition(0)) return current;
                float next = animator.GetNextAnimatorStateInfo(0).shortNameHash == MirrorStateId ? 1f : 0f;
                return Mathf.Lerp(current, next, animator.GetAnimatorTransitionInfo(0).normalizedTime);
            }
        }

        /// <param name="twoHandWeight">1 = follow both palms, 0 = fixed right hand hold.</param>
        public void Solve(float twoHandWeight)
        {
            if (rifle == null || rifle.parent != rightHand) return; // dropped
            rifle.localPosition = fixedLocalPosition;
            Quaternion fixedRotation = rightHand.rotation * fixedLocalRotation;
            if (twoHandWeight <= 0f)
            {
                rifle.rotation = fixedRotation;
                return;
            }

            Vector3 rightPalm = (rightHand.position + rightMiddle.position) * 0.5f;
            Vector3 leftPalm = (leftHand.position + leftMiddle.position) * 0.5f;
            Vector3 barrel = leftPalm - rightPalm;
            if (barrel.sqrMagnitude < minHandGap * minHandGap)
            {
                rifle.rotation = fixedRotation;
                return;
            }

            // Roll comes from the right hand so the sights stay on top of the grip hand.
            Quaternion twoHand = Quaternion.LookRotation(barrel, fixedRotation * Vector3.up);
            rifle.rotation = Quaternion.Slerp(fixedRotation, twoHand, twoHandWeight);

            float left = LeftHandWeight * twoHandWeight;
            if (left <= 0f) return;
            // Mirrored pose: grip in the left palm, barrel through the right palm, sights up.
            Quaternion leftRotation = Quaternion.LookRotation(-barrel, Vector3.up);
            Vector3 leftPosition = leftPalm - leftRotation * gripLocal;
            rifle.SetPositionAndRotation(Vector3.Lerp(rifle.position, leftPosition, left),
                                         Quaternion.Slerp(rifle.rotation, leftRotation, left));
        }
    }
}
