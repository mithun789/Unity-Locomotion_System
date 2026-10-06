using UnityEngine;

namespace WarMission
{
    /// <summary>Muzzle flash (mesh + light) and a short tracer line per shot.</summary>
    public class WeaponEffects : MonoBehaviour
    {
        [SerializeField] GameObject muzzleFlash;
        [SerializeField] Light flashLight;
        [SerializeField] LineRenderer tracer;

        [Header("Timing (seconds)")]
        [SerializeField] float flashTime = 0.04f;
        [SerializeField] float tracerTime = 0.05f;

        float flashOffTime;
        float tracerOffTime;

        void Awake()
        {
            SetFlash(false);
            if (tracer != null)
            {
                tracer.useWorldSpace = true;
                tracer.enabled = false;
            }
        }

        public void PlayShot(Vector3 from, Vector3 to)
        {
            SetFlash(true);
            if (muzzleFlash != null)
                muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flashOffTime = Time.time + flashTime;

            if (tracer != null)
            {
                tracer.SetPosition(0, from);
                tracer.SetPosition(1, to);
                tracer.enabled = true;
                tracerOffTime = Time.time + tracerTime;
            }
        }

        void Update()
        {
            if (Time.time >= flashOffTime) SetFlash(false);
            if (tracer != null && tracer.enabled && Time.time >= tracerOffTime) tracer.enabled = false;
        }

        void SetFlash(bool on)
        {
            if (muzzleFlash != null && muzzleFlash.activeSelf != on) muzzleFlash.SetActive(on);
            if (flashLight != null) flashLight.enabled = on;
        }
    }
}
