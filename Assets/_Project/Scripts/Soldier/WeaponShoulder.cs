using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Shouldered aim (COD / GASP style): while the rifle is up, the sight line runs from just under the right eye
    /// to the aim point and the stock follows into the shoulder; both arms reach the rifle with two-bone IK and the head leans onto
    /// the stock. Relaxed carry stays with the animation (WeaponGrip). Called by SoldierAim after the spine aim.
    /// </summary>
    public class WeaponShoulder : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [Tooltip("Rifle root (child of the right hand). Its M4 holds the StockButt / LeftPalm markers.")]
        [SerializeField] Transform rifle;

        [Header("Sight line (metres): the rear sight sits just under the right eye, on the eye-target line")]
        [Tooltip("Eye above the head bone.")]
        [SerializeField] float eyeHeight = 0.08f;
        [Tooltip("Eye in front of the head bone, toward the target.")]
        [SerializeField] float eyeForward = 0.07f;
        [Tooltip("Right eye to the right of the head centre.")]
        [SerializeField] float eyeRight = 0.03f;
        [Tooltip("Rear sight in front of the eye.")]
        [SerializeField] float eyeRelief = 0.12f;
        [Tooltip("Rear sight below the eye line (cheek on the stock).")]
        [SerializeField] float cheekDrop = 0.03f;
        [Tooltip("Rifle rolled toward the body (cant), degrees.")]
        [SerializeField] float cant = 4f;

        [Header("Head on the stock")]
        [Tooltip("Head pitch down toward the sights, degrees at full aim.")]
        [SerializeField] float headPitch = 6f;
        [Tooltip("Head roll toward the stock, degrees at full aim.")]
        [SerializeField] float headRoll = 8f;

        [Header("Elbows")]
        [Tooltip("Right elbow hint: out to the side and down from the shoulder, metres.")]
        [SerializeField] Vector2 rightElbowHint = new Vector2(0.35f, -0.35f);
        [Tooltip("Left elbow hint: down and slightly out from the left hand, metres.")]
        [SerializeField] Vector2 leftElbowHint = new Vector2(0.15f, -0.45f);

        Transform rUpper, rLower, rHand, lUpper, lLower, lHand, lMiddle, head;
        Vector3 sightLocal, leftPalmLocal;
        bool ready;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            rUpper = B(HumanBodyBones.RightUpperArm); rLower = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
            lUpper = B(HumanBodyBones.LeftUpperArm); lLower = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
            lMiddle = B(HumanBodyBones.LeftMiddleProximal);
            head = B(HumanBodyBones.Head);

            Transform sight = Find(rifle, "RearSight"), leftPalm = Find(rifle, "LeftPalm");
            if (rifle == null || sight == null || leftPalm == null || lMiddle == null || head == null) return;
            sightLocal = rifle.InverseTransformPoint(sight.position);
            leftPalmLocal = rifle.InverseTransformPoint(leftPalm.position);
            ready = true;
        }

        static Transform Find(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <param name="weight">0 = animated hold, 1 = fully shouldered.</param>
        /// <param name="recoil">Muzzle climb in degrees.</param>
        public void Solve(Vector3 aimPoint, float weight, float recoil)
        {
            if (!ready || weight <= 0.001f || rifle.parent != rHand) return;

            // Sight line from the right eye to the target; the stock follows behind it into the shoulder.
            Vector3 toTarget = aimPoint - head.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1f) return;
            Vector3 flat = toTarget.normalized;
            Vector3 flatRight = Vector3.Cross(Vector3.up, flat);
            Vector3 eye = head.position + Vector3.up * eyeHeight + flat * eyeForward + flatRight * eyeRight;
            Vector3 aimDir = (aimPoint - eye).normalized;
            Quaternion rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            rotation = Quaternion.AngleAxis(-recoil, rotation * Vector3.right) * Quaternion.AngleAxis(-cant, aimDir) * rotation;
            Vector3 sightPoint = eye + aimDir * eyeRelief - (rotation * Vector3.up) * cheekDrop;
            Vector3 position = sightPoint - rotation * sightLocal;

            // Blend from the animated rifle pose.
            Quaternion targetRotation = Quaternion.Slerp(rifle.rotation, rotation, weight);
            Vector3 targetPosition = Vector3.Lerp(rifle.position, position, weight);

            // Left hand keeps its grip on the rifle from the animation; record before moving anything.
            Quaternion leftRelative = Quaternion.Inverse(rifle.rotation) * lHand.rotation;
            Vector3 leftPalmOffset = lHand.InverseTransformPoint((lHand.position + lMiddle.position) * 0.5f);

            // Head leans onto the stock (before the arms, so the arms follow the final chest).
            head.rotation = Quaternion.AngleAxis(headPitch * weight, flatRight) *
                            Quaternion.AngleAxis(headRoll * weight, flat) * head.rotation;

            // Right hand: the rifle is its child, so place the hand where the rifle ends at the target.
            Matrix4x4 rifleInHand = Matrix4x4.TRS(rifle.localPosition, rifle.localRotation, rifle.localScale);
            Matrix4x4 handWorld = Matrix4x4.TRS(targetPosition, targetRotation, rifle.lossyScale) * rifleInHand.inverse;
            Vector3 rHandPos = handWorld.GetColumn(3);
            Quaternion rHandRot = handWorld.rotation;
            Vector3 rHint = rUpper.position + flatRight * rightElbowHint.x + Vector3.up * rightElbowHint.y;
            TwoBoneIK.Solve(rUpper, rLower, rHand, rHandPos, rHandRot, rHint);

            // Left hand: palm on the handguard marker of the placed rifle.
            Quaternion lHandRot = rifle.rotation * leftRelative;
            Vector3 palm = rifle.TransformPoint(leftPalmLocal);
            Vector3 lHandPos = palm - lHandRot * Vector3.Scale(leftPalmOffset, lHand.lossyScale);
            Vector3 lHint = lHandPos - flatRight * leftElbowHint.x + Vector3.up * leftElbowHint.y;
            TwoBoneIK.Solve(lUpper, lLower, lHand, lHandPos, lHandRot, lHint);
        }
    }
}
