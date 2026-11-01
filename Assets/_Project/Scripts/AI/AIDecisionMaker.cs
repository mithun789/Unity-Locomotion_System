using UnityEngine;

namespace WarMission
{
    /// <summary>What an AI soldier has decided to do in a fight.</summary>
    public enum AIIntent
    {
        /// <summary>Shoot at the visible enemy from where it stands (strafing).</summary>
        Engage,
        /// <summary>Get into the nearest cover that blocks the threat, hide and peek.</summary>
        TakeCover,
        /// <summary>Bound forward to cover closer to the enemy.</summary>
        Advance,
        /// <summary>Move around the enemy's side to hit it from a new angle.</summary>
        Flank,
        /// <summary>Fire at the enemy's last known position to pin it down.</summary>
        Suppress,
        /// <summary>Fall back away from the threat.</summary>
        Retreat,
        /// <summary>Hunt for the enemy where it was last seen.</summary>
        Search,
    }

    /// <summary>Snapshot of what the soldier knows, gathered by the brain before each decision.</summary>
    public struct AIContext
    {
        public float Health;            // 0..1
        public float Ammo;              // 0..1 of a magazine
        public bool HasTarget;          // an enemy is in sight
        public float TargetDistance;    // to the target, or to the last known position
        public float TimeSinceSeen;     // seconds
        public float Suppression;       // 0..1
        public bool UnderFire;          // hit or near-missed in the last 1.5 s
        public int VisibleEnemies;
        public int NearbyAllies;
        public float Morale;            // 0..1 (squad)
        public bool InCover;            // in position behind cover
        public bool CanUseCover;        // has AICover and the last search did not fail recently
        public bool CanFlank;           // squad flank slot free and no recent failed flank
        public bool CanAdvance;
        public bool CanRetreat;
        public bool CanSearch;          // the last sighting has not been searched yet
        public bool TeamNeedsCover;     // a squad mate is flanking or reloading
        public float CombatTime;        // seconds since this fight started
    }

    /// <summary>
    /// Utility AI: scores every intent from the context and the soldier's personality, then picks the
    /// best with a bonus for the current one (so it commits instead of flickering). A little noise keeps
    /// soldiers from all making the same choice at the same moment.
    /// </summary>
    [RequireComponent(typeof(AIPersonality))]
    public class AIDecisionMaker : MonoBehaviour
    {
        public const int IntentCount = 7;

        [Tooltip("Seconds between decisions (plus a little jitter).")]
        [SerializeField] float thinkInterval = 0.3f;
        [Tooltip("Score bonus for keeping the current intent.")]
        [SerializeField] float commitmentBonus = 0.12f;
        [Tooltip("Minimum seconds to keep an intent before switching (emergencies excepted).")]
        [SerializeField] float minCommitTime = 1.2f;
        [Tooltip("Random score noise per decision, +/-.")]
        [SerializeField] float noise = 0.05f;

        readonly float[] scores = new float[IntentCount];
        AIPersonality personality;
        float nextThink;
        float intentSince;

        public AIIntent Intent { get; private set; } = AIIntent.Engage;
        public float IntentSince => intentSince;
        /// <summary>Last scores, indexed by (int)AIIntent. For the debug overlay.</summary>
        public float[] Scores => scores;

        void Awake() => personality = GetComponent<AIPersonality>();

        /// <summary>Restart a fight: the next Decide call thinks right away and starts from this intent.</summary>
        public void BeginFight(AIIntent intent)
        {
            Intent = intent;
            intentSince = Time.time;
            nextThink = 0f;
        }

        /// <summary>Re-think on the next Decide call (after a move finished or failed).</summary>
        public void ThinkNow() => nextThink = 0f;

        /// <summary>
        /// Returns the intent to follow. Rethinks every thinkInterval; changed is true when the intent switched.
        /// </summary>
        public AIIntent Decide(in AIContext context, out bool changed)
        {
            changed = false;
            if (Time.time < nextThink) return Intent;
            nextThink = Time.time + thinkInterval * Random.Range(0.8f, 1.2f);

            Score(context);
            int current = (int)Intent;
            if (scores[current] > 0f) scores[current] += commitmentBonus;

            int best = 0;
            for (int i = 1; i < IntentCount; i++)
                if (scores[i] > scores[best]) best = i;

            bool emergency = context.Health < 0.35f || context.Suppression > 0.7f;
            if (best != current && (Time.time - intentSince >= minCommitTime || emergency || scores[current] <= 0f))
            {
                Intent = (AIIntent)best;
                intentSince = Time.time;
                changed = true;
            }
            return Intent;
        }

        void Score(in AIContext c)
        {
            float aggression = personality.Aggression;
            float caution = personality.Caution;
            float teamwork = personality.Teamwork;
            float discipline = personality.Discipline;
            float hurt = 1f - c.Health;
            bool outnumbered = c.VisibleEnemies > c.NearbyAllies + 1;
            bool exposed = !c.InCover;

            // Engage: shoot from here. Good when the enemy is in sight and nothing is pinning us down.
            float engage = 0f;
            if (c.HasTarget)
            {
                engage = 0.5f + 0.25f * aggression - 0.45f * c.Suppression - 0.2f * hurt * caution;
                if (c.TargetDistance < 8f) engage += 0.2f;                    // too close to run anywhere
                if (c.UnderFire && exposed) engage -= 0.15f + 0.2f * caution;
                if (c.InCover) engage -= 0.3f;                                // peeks already shoot from cover
                if (c.Ammo <= 0f) engage -= 0.3f;
            }

            // Take cover: under fire, hurt, low on ammo or simply careful.
            float takeCover = 0f;
            if (c.CanUseCover)
            {
                takeCover = (c.UnderFire ? 0.45f : c.HasTarget ? 0.25f : 0.12f)
                            + 0.4f * c.Suppression + 0.3f * hurt + 0.25f * caution - 0.2f * aggression;
                if (c.Ammo < 0.25f) takeCover += 0.3f;
                if (c.UnderFire && exposed) takeCover += 0.15f;
                if (c.InCover) takeCover += 0.2f;                             // already there: stay put
                if (c.TargetDistance < 6f && c.HasTarget) takeCover -= 0.25f; // no time to run
            }

            // Advance: bound to closer cover while winning the fight.
            float advance = 0f;
            if (c.CanAdvance && c.CanUseCover && c.TargetDistance > 12f && c.CombatTime > 3f)
            {
                advance = 0.2f + 0.45f * aggression + 0.2f * c.Morale - 0.6f * c.Suppression - 0.5f * hurt;
                if (outnumbered) advance -= 0.3f;
                if (c.NearbyAllies > 0 && c.TeamNeedsCover) advance -= 0.15f;  // a mate needs our fire instead
                if (c.Ammo < 0.3f) advance -= 0.3f;
            }

            // Flank: break a stalemate from the side. Only one squad member at a time.
            float flank = 0f;
            if (c.CanFlank && c.TargetDistance > 5f && c.TargetDistance < 35f && c.CombatTime > 4f && c.Health > 0.4f)
            {
                flank = 0.15f + 0.45f * aggression - 0.5f * c.Suppression;
                if (c.NearbyAllies > 0) flank += 0.2f * teamwork;            // mates keep the enemy busy
                if (!c.HasTarget && c.TimeSinceSeen > 1.5f && c.TimeSinceSeen < 10f) flank += 0.2f; // enemy hides
                if (c.InCover && c.CombatTime > 10f) flank += 0.1f;          // stuck behind cover too long
                if (c.Ammo < 0.3f) flank -= 0.3f;
            }

            // Suppress: pin the enemy where it was last seen, above all for a flanking or reloading mate.
            float suppress = 0f;
            if (!c.HasTarget && c.TimeSinceSeen < 6f && c.Ammo > 0.3f)
            {
                suppress = 0.2f + 0.15f * discipline - 0.3f * c.Suppression;
                if (c.TeamNeedsCover) suppress += 0.4f * teamwork;
            }

            // Retreat: badly hurt, morale broken or outnumbered.
            float retreat = 0f;
            if (c.CanRetreat)
            {
                retreat = hurt * hurt * (0.4f + 0.6f * caution) + (1f - c.Morale) * 0.5f - 0.3f * aggression;
                if (outnumbered) retreat += 0.2f;
                if (c.Health > 0.7f && c.Morale > 0.6f) retreat -= 0.3f;
            }

            // Search: the enemy vanished; go and find it.
            float search = 0f;
            if (!c.HasTarget && c.CanSearch && c.TimeSinceSeen > 2.5f)
            {
                search = 0.15f + 0.3f * aggression + 0.2f * Mathf.Clamp01(c.TimeSinceSeen / 10f) - 0.5f * c.Suppression;
                if (c.UnderFire) search -= 0.4f;
            }

            Set(AIIntent.Engage, engage);
            Set(AIIntent.TakeCover, takeCover);
            Set(AIIntent.Advance, advance);
            Set(AIIntent.Flank, flank);
            Set(AIIntent.Suppress, suppress);
            Set(AIIntent.Retreat, retreat);
            Set(AIIntent.Search, search);
        }

        void Set(AIIntent intent, float score)
        {
            if (score > 0f) score += Random.Range(-noise, noise);
            scores[(int)intent] = Mathf.Max(0f, score);
        }
    }
}
