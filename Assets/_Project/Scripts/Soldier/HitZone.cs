using UnityEngine;

namespace WarMission
{
    /// <summary>Put on a hitbox collider to scale damage (for example 3x on the head).</summary>
    public class HitZone : MonoBehaviour
    {
        [SerializeField] float damageMultiplier = 1f;

        public float DamageMultiplier => damageMultiplier;
    }
}
