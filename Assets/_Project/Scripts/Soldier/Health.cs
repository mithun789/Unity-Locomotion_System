using System;
using UnityEngine;

namespace WarMission
{
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHealth = 100f;

        public float MaxHealth => maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(in DamageInfo damage)
        {
            if (IsDead) return;

            Current = Mathf.Max(0f, Current - damage.Amount);
            Damaged?.Invoke(damage);
            if (IsDead) Died?.Invoke(damage);
        }
    }
}
