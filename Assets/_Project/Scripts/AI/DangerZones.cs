using System.Collections.Generic;
using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Places a team has learned to avoid: where its soldiers died recently. Live grenades count for everyone.
    /// AICover adds the penalty to cover costs, so soldiers stop running into the same kill zone.
    /// </summary>
    public static class DangerZones
    {
        struct Zone
        {
            public Team Team;
            public Vector3 Position;
            public float Time;
        }

        const int MaxZones = 32;
        static readonly List<Zone> zones = new List<Zone>();

        /// <summary>Seconds a death spot stays dangerous.</summary>
        public static float Memory = 30f;
        public static float DeathRadius = 4f;
        public static float DeathPenalty = 8f;
        public static float GrenadePenalty = 40f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => zones.Clear();

        public static void AddDeath(Team team, Vector3 position)
        {
            if (zones.Count >= MaxZones) zones.RemoveAt(0);
            zones.Add(new Zone { Team = team, Position = position, Time = Time.time });
        }

        /// <summary>Extra cost (metres) for using a position: near a recent team death or a live grenade.</summary>
        public static float Penalty(Team team, Vector3 position)
        {
            float penalty = 0f;
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                Zone z = zones[i];
                if (Time.time - z.Time > Memory)
                {
                    zones.RemoveAt(i);
                    continue;
                }
                if (z.Team != team) continue;
                float d = Vector3.Distance(z.Position, position);
                if (d < DeathRadius) penalty += DeathPenalty * (1f - d / DeathRadius);
            }

            var grenades = Grenade.Live;
            for (int i = 0; i < grenades.Count; i++)
                if (grenades[i] != null && Vector3.Distance(grenades[i].Position, position) < grenades[i].Radius + 1f)
                    penalty += GrenadePenalty;
            return penalty;
        }
    }
}
