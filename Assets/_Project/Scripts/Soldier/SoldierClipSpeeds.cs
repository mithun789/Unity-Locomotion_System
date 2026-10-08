namespace WarMission
{
    /// <summary>
    /// Travel speed of each locomotion clip in m/s at playback 1x, measured from the clips.
    /// SoldierMotor (movement) and SoldierAnimatorBuilder (blend tree positions) both read these,
    /// so the feet stay matched to the ground speed. Rebuild the Animator after changing anything here.
    /// </summary>
    public static class SoldierClipSpeeds
    {
        /// <summary>Walk clips play this much faster than authored; walk speeds scale with it.</summary>
        public const float WalkPlayback = 1.4f;

        public const float WalkForward = 0.95f;
        public const float WalkBackward = 1.05f;
        public const float WalkRight = 0.72f;
        /// <summary>Different clip from walk right (never mirror rifle clips); played slower to match walk right.</summary>
        public const float WalkLeft = 1.32f;

        /// <summary>Run clips play this much faster than authored; run speeds scale with it.</summary>
        public const float RunPlayback = 1.25f;

        public const float RunForward = 2.85f;
        public const float RunBackward = 2.63f;
        public const float RunRight = 3.11f;
        public const float RunLeft = 2.69f;
        /// <summary>Relaxed carry (rifle not aimed), used for sprinting forward. Scaled to match RunForward.</summary>
        public const float SprintForward = 2.28f;

        /// <summary>
        /// Crouch ground speed in every direction except straight back. Each crouch clip's playback
        /// is scaled to hit it, because the Mixamo crouch clips were recorded at very different speeds.
        /// </summary>
        public const float CrouchSpeed = 1.0f;
        /// <summary>Straight back has no clip; it blends back-left and back-right, which cover less ground.</summary>
        public const float CrouchBackward = 0.7f;

        public const float CrouchWalkForward = 0.82f;
        public const float CrouchWalkLeft = 1.01f;
        public const float CrouchWalkRight = 0.98f;
        public const float CrouchWalkForwardDiagonal = 1.69f;
        public const float CrouchWalkBackDiagonal = 1.75f;

        /// <summary>Back-to-wall sidestep along high cover with the rifle (Mixamo "Left/Right Cover Sneak W/ Rifle").</summary>
        public const float CoverSneakHigh = 1.08f;
        /// <summary>Crouched back-to-wall sidestep along low cover ("crouched sneaking").</summary>
        public const float CoverSneakLow = 1.0f;

        /// <summary>Body turn of the turn-in-place clips, degrees (root rotation, measured).</summary>
        public const float TurnLeftAngle = 90f;
        public const float TurnRightAngle = 84f;
        public const float CrouchTurnAngle = 90f;
    }
}
