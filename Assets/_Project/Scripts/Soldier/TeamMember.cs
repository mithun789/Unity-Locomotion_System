using System.Collections.Generic;
using UnityEngine;

namespace WarMission
{
    public enum Team
    {
        Ally,
        Enemy,
    }

    /// <summary>Which side a soldier is on, plus the body points others aim and look at.</summary>
    public class TeamMember : MonoBehaviour
    {
        [SerializeField] Team team = Team.Enemy;

        public Team Team => team;
        public Health Health { get; private set; }
        public bool IsAlive => Health == null || !Health.IsDead;
        public Transform Head { get; private set; }
        public Transform Chest { get; private set; }
        public SoldierMotor Motor { get; private set; }
        /// <summary>Last time this soldier fired (firing makes a soldier easy to spot).</summary>
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        /// <summary>How many enemy AI currently have this soldier as their target (they spread their fire).</summary>
        public int TargetedBy { get; set; }
        SoldierWeapon weapon;

        void Awake()
        {
            Health = GetComponent<Health>();
            Motor = GetComponent<SoldierMotor>();
            weapon = GetComponent<SoldierWeapon>();
            var animator = GetComponentInChildren<Animator>();
            Head = Bone(animator, HumanBodyBones.Head);
            Chest = Bone(animator, HumanBodyBones.UpperChest) ?? Bone(animator, HumanBodyBones.Chest) ?? transform;
            Head ??= transform;
        }

        static Transform Bone(Animator animator, HumanBodyBones bone) =>
            animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;

        void OnEnable()
        {
            TeamRegistry.Register(this);
            if (weapon != null) weapon.Fired += OnFired;
        }

        void OnDisable()
        {
            TeamRegistry.Unregister(this);
            if (weapon != null) weapon.Fired -= OnFired;
        }

        void OnFired() => LastShotTime = Time.time;

        public bool IsEnemyOf(TeamMember other) => other != null && other.team != team;
    }

    /// <summary>All active soldiers, for target lookup without FindObjectsOfType.</summary>
    public static class TeamRegistry
    {
        static readonly List<TeamMember> members = new List<TeamMember>();

        public static IReadOnlyList<TeamMember> Members => members;

        public static void Register(TeamMember member)
        {
            if (!members.Contains(member)) members.Add(member);
        }

        public static void Unregister(TeamMember member) => members.Remove(member);

        // Play mode can start without a domain reload; start with an empty list.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => members.Clear();
    }
}
