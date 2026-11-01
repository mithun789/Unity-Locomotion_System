using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// Character traits that bias an AI soldier's decisions. Each soldier rolls its own values (unless
    /// randomize is off), so a squad mixes pushers, careful riflemen and steady shooters.
    /// All traits are 0..1.
    /// </summary>
    public class AIPersonality : MonoBehaviour
    {
        [Tooltip("Roll new traits in Awake inside the ranges below.")]
        [SerializeField] bool randomize = true;

        [Header("Traits (0..1)")]
        [Tooltip("Pushes, flanks and stands in the open.")]
        [Range(0f, 1f)] [SerializeField] float aggression = 0.5f;
        [Tooltip("Seeks cover early, retreats when hurt.")]
        [Range(0f, 1f)] [SerializeField] float caution = 0.5f;
        [Tooltip("Tighter aim, shorter bursts, faster reaction.")]
        [Range(0f, 1f)] [SerializeField] float discipline = 0.5f;
        [Tooltip("Suppresses for flanking or reloading squad mates.")]
        [Range(0f, 1f)] [SerializeField] float teamwork = 0.5f;

        [Header("Random ranges")]
        [SerializeField] Vector2 aggressionRange = new Vector2(0.2f, 0.9f);
        [SerializeField] Vector2 cautionRange = new Vector2(0.2f, 0.9f);
        [SerializeField] Vector2 disciplineRange = new Vector2(0.3f, 0.9f);
        [SerializeField] Vector2 teamworkRange = new Vector2(0.4f, 1f);

        public float Aggression => aggression;
        public float Caution => caution;
        public float Discipline => discipline;
        public float Teamwork => teamwork;

        /// <summary>Short label for the debug overlay.</summary>
        public string Archetype
        {
            get
            {
                if (aggression > 0.65f && aggression > caution) return "Assault";
                if (caution > 0.65f && caution > aggression) return "Careful";
                if (discipline > 0.7f) return "Marksman";
                return "Rifleman";
            }
        }

        void Awake()
        {
            if (!randomize) return;
            aggression = Random.Range(aggressionRange.x, aggressionRange.y);
            caution = Random.Range(cautionRange.x, cautionRange.y);
            discipline = Random.Range(disciplineRange.x, disciplineRange.y);
            teamwork = Random.Range(teamworkRange.x, teamworkRange.y);
        }
    }
}
