using UnityEngine;

namespace WarMission
{
    /// <summary>What a soldier wants to do this frame. Written by the player or the AI, read by the soldier core.</summary>
    public struct SoldierCommand
    {
        public Vector2 Move;       // local input, x = strafe, y = forward, range -1..1
        public Vector3 AimPoint;   // world-space point to aim at
        public bool Aim;           // rifle up and ready; false lets an idle soldier relax
        public bool Fire;
        public bool Reload;
        public bool Crouch;
        public bool Sprint;
        public bool Jump;          // true on the frame the jump starts
        public bool Grenade;       // true on the frame a grenade throw starts
        public Vector3 GrenadeTarget; // where the grenade should land; zero = AimPoint

        // Cover: back against a wall. The body faces CoverForward (into the cover; the cover clips turn the
        // pose around), slides along the wall and is held at CoverAnchor's distance from it.
        public bool InCover;
        public bool CoverLow;      // crouched behind waist-high cover
        public Vector3 CoverForward;
        public Vector3 CoverAnchor;
        // High cover: look out to the soldier's own left (mirrored cover clips, rifle in the left hand).
        public bool CoverFaceLeft;

        /// <summary>
        /// Converts a world-space move direction into Move input. The motor moves relative to the
        /// aim direction, so AI that walks one way while aiming another uses this.
        /// </summary>
        public static Vector2 WorldToMove(Vector3 worldDirection, Vector3 position, Vector3 aimPoint)
        {
            Vector3 toAim = aimPoint - position;
            toAim.y = 0f;
            if (toAim.sqrMagnitude < 0.0001f) return new Vector2(worldDirection.x, worldDirection.z);
            Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(toAim)) * worldDirection;
            return new Vector2(local.x, local.z);
        }

        /// <summary>Move input as a flat world direction (inverse of WorldToMove). Zero when not moving.</summary>
        public Vector3 MoveWorld(Vector3 position)
        {
            Vector3 toAim = AimPoint - position;
            toAim.y = 0f;
            Quaternion frame = toAim.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(toAim);
            return frame * new Vector3(Move.x, 0f, Move.y);
        }

        /// <summary>Stand still and look straight ahead.</summary>
        public static SoldierCommand Idle(Transform soldier) => new SoldierCommand
        {
            AimPoint = soldier.position + soldier.forward * 10f + Vector3.up * 1.5f,
        };
    }

    public interface ISoldierInput
    {
        SoldierCommand GetCommand();
    }
}
