using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WarMission.EditorTools
{
    /// <summary>
    /// Applies the project's Mixamo import rules.
    /// Character model: humanoid, creates the shared avatar, no animation.
    /// Clips (Rifle_&lt;Action&gt;_&lt;Direction&gt;.fbx): humanoid with the character's avatar,
    /// root motion baked into pose, loop for idle/locomotion, Optimal compression.
    /// No mirrored clips: rifle clips are handed, and mirroring puts the rifle in the wrong hand.
    /// </summary>
    public class MixamoImportProcessor : AssetPostprocessor
    {
        public const string MixamoFolder = "Assets/_Project/Animations/Mixamo/";
        public const string CharacterPath = "Assets/_Project/Characters/Soldier.fbx";

        // Actions (second name token) that loop. Anything else plays once.
        static readonly HashSet<string> LoopActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "AimIdle", "Walk", "Run", "Sprint", "CrouchIdle", "CrouchWalk", "CrouchRun", "Fire", "CrouchFire", "Sneak"
        };

        bool IsCharacter => assetPath == CharacterPath;

        bool IsClip => assetPath.StartsWith(MixamoFolder, StringComparison.Ordinal)
                       && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!IsCharacter && !IsClip) return;

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;

            if (IsCharacter)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                return;
            }

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;

            // Reimport clips when the character (and its avatar) changes.
            context.DependsOnArtifact(CharacterPath);
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(CharacterPath);
            if (avatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
            }
            else
            {
                Debug.LogWarning($"[MixamoImport] No avatar at {CharacterPath}. Import the character first, then reimport {assetPath}.");
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsClip) return;

            var importer = (ModelImporter)assetImporter;
            var defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0) return;

            string name = Path.GetFileNameWithoutExtension(assetPath);
            importer.clipAnimations = new[] { Configure(defaults[0], name) };
        }

        // Actions whose root rotation is NOT baked: the turn is extracted as root motion and
        // SoldierRootMotion applies it to the soldier, so the pose stays forward-facing.
        static readonly HashSet<string> TurnActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TurnInPlace", "CrouchTurn90"
        };

        // Root yaw of each turn clip's first frame (measured), minus the body yaw we want relative to the soldier.
        // The offset cancels it, so the turn starts and ends with the hips where the idles have them.
        // Left: the clip stands bladed 51 degrees (Rifle_Idle_Ready stance); turned square like the relaxed idles.
        static readonly Dictionary<string, float> TurnStartYaw = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            { "Rifle_TurnInPlace_Left", 54f - 51f },
            { "Rifle_TurnInPlace_Right", -4f },
            { "Rifle_CrouchTurn90_Left", 33f },
            { "Rifle_CrouchTurn90_Right", 33f },
        };

        static ModelImporterClipAnimation Configure(ModelImporterClipAnimation source, string name)
        {
            string[] parts = name.Split('_');
            bool turn = parts.Length >= 2 && TurnActions.Contains(parts[1]);
            return new ModelImporterClipAnimation
            {
                name = name,
                takeName = source.takeName,
                firstFrame = source.firstFrame,
                lastFrame = source.lastFrame,
                wrapMode = source.wrapMode,
                loopTime = ShouldLoop(name),
                loopPose = false,

                // Rotation and height are baked into the pose. XZ is extracted as root motion
                // (not baked) so clips downloaded without "In Place" still stay in place;
                // the Animator ignores root motion and the NavMeshAgent moves the soldier.
                lockRootRotation = !turn,
                keepOriginalOrientation = !turn,
                rotationOffset = turn && TurnStartYaw.TryGetValue(name, out float startYaw) ? -startYaw : 0f,
                lockRootHeightY = true,
                keepOriginalPositionY = true,
                heightFromFeet = false,
                lockRootPositionXZ = false,
                keepOriginalPositionXZ = false,
            };
        }

        static bool ShouldLoop(string name)
        {
            string[] parts = name.Split('_');
            return parts.Length >= 2 && LoopActions.Contains(parts[1]);
        }
    }
}
