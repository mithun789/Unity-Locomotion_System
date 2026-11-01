using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Turns the spine after animation so the rifle barrel points at the aim point.
    /// Works from the muzzle direction, not the chest, because the Mixamo rifle stance holds
    /// the chest about 45 degrees off the barrel. Also adds a small recoil kick per shot.
    /// Runs in LateUpdate after the Animator: first WeaponGrip lines the rifle up with both hands,
    /// then the spine turns, carrying both hands and the rifle together.
    /// The rifle is only up while aiming or firing; otherwise the aim fades out and the relaxed idle /
    /// relaxed arms show. It also stays down while a wall is right in front of the chest.
    /// </summary>
    [RequireComponent(typeof(Soldier), typeof(SoldierMotor))]
    public class SoldierAim : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] SoldierWeapon weapon;
        [Tooltip("Places the rifle between the hands before the spine is aimed.")]
        [SerializeField] WeaponGrip grip;
        [SerializeField] WeaponShoulder shoulder;

        [Header("Spine share of the correction (last bone finishes it)")]
        [SerializeField] float spineWeight = 0.3f;
        [SerializeField] float chestWeight = 0.5f;
        [SerializeField] float upperChestWeight = 1f;

        [Header("Limits")]
        [Tooltip("Largest correction applied, in degrees.")]
        [SerializeField] float maxAngle = 70f;
        [Tooltip("Aim points closer than this to the muzzle are ignored, in metres.")]
        [SerializeField] float minDistance = 1f;
        [Tooltip("How fast aim fades in and out for reloads, weight per second.")]
        [SerializeField] float blendSpeed = 5f;

        [Header("Ready / relaxed (rifle up only when aiming or firing, as in COD / GASP)")]
        [Tooltip("How fast the rifle comes up, weight per second.")]
        [SerializeField] float raiseSpeed = 7f;
        [SerializeField] float lowerSpeed = 2.5f;

        [Header("Wall obstruction (rifle lowers when the barrel would go into a wall)")]
        [SerializeField] float obstructionDistance = 0.8f;
        [SerializeField] float obstructionRadius = 0.08f;
        [SerializeField] LayerMask obstructionMask = ~0;

        [Header("Recoil")]
        [SerializeField] float recoilKick = 1.5f;
        [SerializeField] float recoilRecovery = 15f;

        readonly RaycastHit[] obstructionHits = new RaycastHit[8];
        Soldier soldier;
        SoldierMotor motor;
        SoldierGrenades grenades;
        SoldierTraversal traversal;
        Transform[] bones;
        float[] weights;
        float reloadWeight = 1f;
        float recoil;

        /// <summary>0 = relaxed idle, 1 = rifle up. Drives the Animator's idle blend.</summary>
        public float ReadyWeight { get; private set; } = 1f;
        /// <summary>The rifle is up far enough to shoot.</summary>
        public bool IsReady => ReadyWeight > 0.95f;
        /// <summary>A wall right in front of the chest keeps the rifle down.</summary>
        public bool IsObstructed { get; private set; }

        void Awake()
        {
            soldier = GetComponent<Soldier>();
            motor = GetComponent<SoldierMotor>();
            traversal = GetComponent<SoldierTraversal>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (weapon == null) weapon = GetComponent<SoldierWeapon>();
            if (grip == null) grip = GetComponent<WeaponGrip>();
            if (shoulder == null) shoulder = GetComponent<WeaponShoulder>();

            var candidates = new[]
            {
                (animator.GetBoneTransform(HumanBodyBones.Spine), spineWeight),
                (animator.GetBoneTransform(HumanBodyBones.Chest), chestWeight),
                (animator.GetBoneTransform(HumanBodyBones.UpperChest), upperChestWeight),
            };
            var boneList = new System.Collections.Generic.List<Transform>();
            var weightList = new System.Collections.Generic.List<float>();
            foreach (var (bone, weight) in candidates)
            {
                if (bone == null) continue;
                boneList.Add(bone);
                weightList.Add(weight);
            }
            weightList[weightList.Count - 1] = 1f;
            bones = boneList.ToArray();
            weights = weightList.ToArray();
        }

        void OnEnable()
        {
            if (weapon != null) weapon.Fired += OnFired;
        }

        void OnDisable()
        {
            if (weapon != null) weapon.Fired -= OnFired;
        }

        void OnFired() => recoil += recoilKick;

        bool Obstructed(Vector3 aimPoint)
        {
            Vector3 origin = bones[bones.Length - 1].position;
            Vector3 dir = aimPoint - origin;
            if (dir.sqrMagnitude < 0.01f) return false;
            int count = Physics.SphereCastNonAlloc(origin, obstructionRadius, dir.normalized, obstructionHits, obstructionDistance,
                                                   obstructionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = obstructionHits[i];
                // Scenery only: not this soldier, not other soldiers' hitboxes or loose rifles.
                if (hit.distance <= 0f || hit.rigidbody != null || hit.transform.IsChildOf(transform)) continue;
                // A top edge below the chest is low cover being shot over, not a wall in the way.
                if (hit.point.y < origin.y - 0.1f && hit.normal.y > 0.5f) continue;
                if (hit.point.y < origin.y - 0.1f && hit.collider.bounds.max.y < origin.y) continue;
                return true;
            }
            return false;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            // Traversal owns the body (rifle slung): no grip, no aim; the rifle comes back lowered.
            if (traversal != null && traversal.IsActive)
            {
                ReadyWeight = 0f;
                return;
            }
            SoldierCommand command = soldier.Command;
            // Rifle up only on aim / fire. Sprinting carries it as the run clip does; a wall in front lowers it.
            IsObstructed = (command.Aim || command.Fire) && Obstructed(command.AimPoint);
            bool ready = !motor.IsSprinting && !IsObstructed && (command.Aim || command.Fire);
            ReadyWeight = Mathf.MoveTowards(ReadyWeight, ready ? 1f : 0f, (ready ? raiseSpeed : lowerSpeed) * dt);
            if (grenades == null) TryGetComponent(out grenades);
            // Reloading or throwing a grenade: the hands leave the aim pose (fixed rifle hold, no spine aim).
            bool handsBusy = (weapon != null && weapon.IsReloading) || (grenades != null && grenades.IsThrowing);
            reloadWeight = Mathf.MoveTowards(reloadWeight, handsBusy ? 0f : 1f, blendSpeed * dt);
            float aimWeight = ReadyWeight * reloadWeight;

            if (grip != null) grip.Solve(reloadWeight);

            recoil = Mathf.MoveTowards(recoil, 0f, recoilRecovery * dt);
            if (weapon == null || weapon.Muzzle == null) return;

            Transform muzzle = weapon.Muzzle;
            Vector3 target = command.AimPoint;

            if (aimWeight > 0f)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    Vector3 wanted = target - muzzle.position;
                    if (wanted.sqrMagnitude < minDistance * minDistance) break;

                    Quaternion delta = Quaternion.FromToRotation(muzzle.forward, wanted.normalized);
                    delta.ToAngleAxis(out float angle, out Vector3 axis);
                    if (angle > 180f) angle -= 360f;
                    angle = Mathf.Clamp(angle, -maxAngle, maxAngle) * weights[i] * aimWeight;
                    bones[i].rotation = Quaternion.AngleAxis(angle, axis) * bones[i].rotation;
                }
            }

            // Shouldered rifle while aiming (not in the mirrored cover pose, rifle in the left hand there).
            float shoulderWeight = shoulder != null && shoulder.enabled && grip != null
                ? aimWeight * (1f - grip.LeftHandWeight) : 0f;
            if (shoulderWeight > 0.001f)
            {
                shoulder.Solve(target, shoulderWeight, recoil);
            }
            else if (recoil > 0f)
            {
                Transform top = bones[bones.Length - 1];
                top.rotation = Quaternion.AngleAxis(-recoil, transform.right) * top.rotation;
            }
        }
    }
}
