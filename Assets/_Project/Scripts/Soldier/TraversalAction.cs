using UnityEngine;

namespace WarMission
{
    public enum TraversalKind
    {
        Vault,   // over a thin obstacle, land on the far side at the same level
        Mantle,  // up onto the top of an obstacle
        Drop,    // down off an edge
    }

    /// <summary>How the clip's own height is matched to the real obstacle.</summary>
    public enum TraversalWarp
    {
        /// <summary>The clip's highest foot point is scaled to clear the obstacle (vaults).</summary>
        ScaleToPeak,
        /// <summary>The clip ends standing higher; its end height is scaled to the obstacle top (climb clips).</summary>
        ScaleToEnd,
        /// <summary>The clip is a hop; the height change is added along the forward progress (hop up, drop down).</summary>
        AddRise,
    }

    /// <summary>
    /// One traversal move: which Animator state to play, which obstacles it suits, and the clip's
    /// motion profile baked in the editor (War Mission > Build Traversal Actions) for motion warping.
    /// </summary>
    public class TraversalAction : ScriptableObject
    {
        public TraversalKind Kind;
        public TraversalWarp Warp;
        [Tooltip("Animator state on the base layer that plays this move.")]
        public string StateName;

        [Header("Obstacle height it suits (m, relative to the feet)")]
        public float MinHeight = 0.4f;
        public float MaxHeight = 1.3f;

        [Header("Timing")]
        [Tooltip("Clip playback speed.")]
        public float Speed = 1f;
        [Tooltip("Stop the move at this normalized clip time (skips idle tails).")]
        [Range(0.1f, 1f)] public float PlayUntil = 1f;
        [Tooltip("Crossfade back to locomotion at the end, in seconds.")]
        public float EndBlend = 0.15f;

        [Header("Baked from the clip")]
        public AnimationClip Clip;
        public float ClipLength;
        [Tooltip("Lowest foot height at the clip's first frame, relative to the root. Non-zero when the clip starts " +
                 "standing on something (e.g. a railing); the model is lowered by this much.")]
        public float FootBase;
        [Tooltip("Lowest foot height above the start, in metres, over normalized time.")]
        public AnimationCurve FootHeight = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("Forward travel 0..1 over normalized time.")]
        public AnimationCurve Forward = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Peak foot height (ScaleToPeak) or foot height at PlayUntil (ScaleToEnd, negative for drops), in metres.")]
        public float KeyHeight;

        public float Duration => ClipLength * PlayUntil / Mathf.Max(0.01f, Speed);

        /// <summary>Foot height at move progress u (0..1 of the played part).</summary>
        public float FootAt(float u) => FootHeight.Evaluate(u * PlayUntil);

        /// <summary>Forward progress at move progress u, 1 at the end of the played part.</summary>
        public float ForwardAt(float u)
        {
            float end = Forward.Evaluate(PlayUntil);
            return Forward.Evaluate(u * PlayUntil) / (Mathf.Abs(end) > 0.01f ? end : 1f);
        }
    }
}
