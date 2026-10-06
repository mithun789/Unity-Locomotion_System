using UnityEngine;

namespace WarMission
{
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;       // world hit point
        public Vector3 Direction;   // normalized travel direction of the shot
        public float Force;         // impulse applied to the ragdoll on death
        public GameObject Source;   // soldier that fired
    }

    public interface IDamageable
    {
        void TakeDamage(in DamageInfo damage);
    }
}
