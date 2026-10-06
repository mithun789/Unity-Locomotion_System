using System;
using UnityEngine;

namespace WarMission
{
    /// <summary>Hitscan rifle. Fires from the muzzle toward the command's aim point, with spread.</summary>
    [RequireComponent(typeof(SoldierMotor))]
    public class SoldierWeapon : MonoBehaviour
    {
        /// <summary>Reload length in seconds. The Animator builder speeds the reload clip up to match.</summary>
        public const float DefaultReloadTime = 2.4f;

        [Header("References")]
        [SerializeField] Transform muzzle;
        [SerializeField] WeaponEffects effects;

        [Header("Fire")]
        [SerializeField] float roundsPerMinute = 600f;
        [SerializeField] float damage = 25f;
        [SerializeField] float range = 200f;
        [Tooltip("Impulse given to a ragdoll killed by this shot.")]
        [SerializeField] float hitForce = 60f;
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Magazine")]
        [SerializeField] int magazineSize = 30;
        [SerializeField] float reloadTime = DefaultReloadTime;

        [Header("Spread (degrees)")]
        [SerializeField] float baseSpread = 0.3f;
        [Tooltip("Extra spread at full run speed.")]
        [SerializeField] float moveSpread = 1.5f;
        [SerializeField] float spreadPerShot = 0.3f;
        [SerializeField] float maxShotSpread = 2.5f;
        [Tooltip("How fast shot spread recovers, degrees per second.")]
        [SerializeField] float spreadRecovery = 5f;
        [SerializeField] float crouchSpreadMultiplier = 0.6f;

        readonly RaycastHit[] hits = new RaycastHit[16];
        SoldierMotor motor;
        float nextFireTime;
        float reloadEndTime;
        float shotSpread;

        public int Ammo { get; private set; }
        public int MagazineSize => magazineSize;
        public bool IsReloading { get; private set; }
        public float CurrentSpread { get; private set; }
        public Transform Muzzle => muzzle;

        public event Action Fired;
        public event Action ReloadStarted;
        /// <summary>A shot hit something that has Health. The bool is true when the shot killed it.</summary>
        public event Action<Health, bool> HitTarget;

        /// <summary>Any weapon fired. AI hearing listens to this.</summary>
        public static event Action<SoldierWeapon> AnyFired;

        /// <summary>Any shot's path (weapon, start, end). AI uses it to feel bullets passing close (suppression).</summary>
        public static event Action<SoldierWeapon, Vector3, Vector3> AnyShotPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AnyFired = null;
            AnyShotPath = null;
        }

        void Awake()
        {
            motor = GetComponent<SoldierMotor>();
            Ammo = magazineSize;
        }

        public void Tick(in SoldierCommand command, float dt)
        {
            shotSpread = Mathf.Max(0f, shotSpread - spreadRecovery * dt);
            float moveFactor = Mathf.Clamp01(motor.Velocity.magnitude / 3f);
            CurrentSpread = (baseSpread + moveSpread * moveFactor + shotSpread) * (motor.IsCrouched ? crouchSpreadMultiplier : 1f);

            if (IsReloading)
            {
                if (Time.time >= reloadEndTime)
                {
                    IsReloading = false;
                    Ammo = magazineSize;
                }
                return;
            }

            if (command.Reload && Ammo < magazineSize)
            {
                StartReload();
                return;
            }

            if (command.Fire && Time.time >= nextFireTime)
            {
                if (Ammo > 0) Shoot(command.AimPoint);
                else StartReload();
            }
        }

        void StartReload()
        {
            IsReloading = true;
            reloadEndTime = Time.time + reloadTime;
            ReloadStarted?.Invoke();
        }

        void Shoot(Vector3 aimPoint)
        {
            nextFireTime = Time.time + 60f / roundsPerMinute;
            Ammo--;

            Vector3 origin = muzzle.position;
            Vector3 dir = aimPoint - origin;
            // Aim point behind or on top of the muzzle: shoot straight down the barrel.
            dir = dir.sqrMagnitude > 0.25f && Vector3.Dot(dir, muzzle.forward) > 0f ? dir.normalized : muzzle.forward;
            dir = ApplySpread(dir, CurrentSpread);
            shotSpread = Mathf.Min(shotSpread + spreadPerShot, maxShotSpread);

            Vector3 end = origin + dir * range;
            if (TryGetFirstHit(origin, dir, out RaycastHit hit))
            {
                end = hit.point;
                ApplyHit(hit, dir);
            }

            if (effects != null) effects.PlayShot(origin, end);
            Fired?.Invoke();
            AnyFired?.Invoke(this);
            AnyShotPath?.Invoke(this, origin, end);
        }

        bool TryGetFirstHit(Vector3 origin, Vector3 dir, out RaycastHit closest)
        {
            int count = Physics.RaycastNonAlloc(origin, dir, hits, range, hitMask, QueryTriggerInteraction.Ignore);
            closest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance < best && !hits[i].transform.IsChildOf(transform))
                {
                    best = hits[i].distance;
                    closest = hits[i];
                }
            }
            return best < float.MaxValue;
        }

        void ApplyHit(in RaycastHit hit, Vector3 dir)
        {
            var health = hit.collider.GetComponentInParent<Health>();
            if (health == null)
            {
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                    hit.rigidbody.AddForceAtPosition(dir * hitForce * 0.2f, hit.point, ForceMode.Impulse);
                return;
            }
            if (health.IsDead) return;

            var zone = hit.collider.GetComponent<HitZone>();
            var info = new DamageInfo
            {
                Amount = damage * (zone != null ? zone.DamageMultiplier : 1f),
                Point = hit.point,
                Direction = dir,
                Force = hitForce,
                Source = gameObject,
            };
            health.TakeDamage(info);
            HitTarget?.Invoke(health, health.IsDead);
        }

        static Vector3 ApplySpread(Vector3 dir, float degrees)
        {
            if (degrees <= 0f) return dir;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * degrees;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            return Quaternion.AngleAxis(offset.x, Vector3.up) * Quaternion.AngleAxis(offset.y, right.normalized) * dir;
        }
    }
}
