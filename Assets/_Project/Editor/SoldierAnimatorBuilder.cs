using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WarMission.EditorTools
{
    /// <summary>
    /// Builds Soldier.controller from the sorted Mixamo clips. Safe to re-run after adding clips:
    /// the controller asset is rebuilt in place, so its GUID and prefab references survive.
    /// Blend tree positions are the clips' measured speeds in m/s (see CLAUDE.md), matching SoldierMotor.
    /// </summary>
    public static class SoldierAnimatorBuilder
    {
        const string MixamoFolder = "Assets/_Project/Animations/Mixamo/";
        const string ControllerPath = "Assets/_Project/Animations/Controllers/Soldier.controller";
        const string UpperBodyMaskPath = "Assets/_Project/Animations/Controllers/UpperBody.mask";
        const string ArmsMaskPath = "Assets/_Project/Animations/Controllers/Arms.mask";

        [MenuItem("War Mission/Build Soldier Animator")]
        public static void Build()
        {
            AnimatorController controller = LoadOrCreateController();

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AimBlend", AnimatorControllerParameterType.Float);
            controller.AddParameter("IdleVariant", AnimatorControllerParameterType.Float);
            controller.AddParameter("InCover", AnimatorControllerParameterType.Bool);
            controller.AddParameter("CoverLow", AnimatorControllerParameterType.Bool);
            controller.AddParameter("CoverMirror", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("TurnDir", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "TurnRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f
            });
            controller.AddParameter("ArmsAim", AnimatorControllerParameterType.Float);

            controller.AddLayer("Base Layer");
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // Positions are ground speed in m/s: clip speed x playback scale (see SoldierClipSpeeds).
            const float walk = SoldierClipSpeeds.WalkPlayback;
            BlendTree standTree = CreateLocomotionTree(controller, "StandLocomotion");
            AddChild(standTree, CreateIdleTree(controller), 0f, 0f);
            AddChild(standTree, Clip("Rifle_Walk_Forward"), 0f, SoldierClipSpeeds.WalkForward * walk, walk);
            AddChild(standTree, Clip("Rifle_Walk_Backward"), 0f, -SoldierClipSpeeds.WalkBackward * walk, walk);
            const float walkSide = SoldierClipSpeeds.WalkRight * walk;
            AddChild(standTree, Clip("Rifle_Walk_Right"), walkSide, 0f, walk);
            AddChild(standTree, Clip("Rifle_Walk_Left"), -walkSide, 0f, walkSide / SoldierClipSpeeds.WalkLeft);
            const float run = SoldierClipSpeeds.RunPlayback;
            AddChild(standTree, CreateRunForwardTree(controller), 0f, SoldierClipSpeeds.RunForward * run);
            AddChild(standTree, Clip("Rifle_Run_Backward"), 0f, -SoldierClipSpeeds.RunBackward * run, run);
            AddChild(standTree, Clip("Rifle_Run_Right"), SoldierClipSpeeds.RunRight * run, 0f, run);
            AddChild(standTree, Clip("Rifle_Run_Left"), -SoldierClipSpeeds.RunLeft * run, 0f, run);

            // Crouch clips were recorded at very different speeds. Each is placed at the motor's crouch
            // speed for its direction and its playback is scaled to cover exactly that ground.
            // Straight back has no clip and blends back-left with back-right.
            BlendTree crouchTree = CreateLocomotionTree(controller, "CrouchLocomotion");
            AddChild(crouchTree, Clip("Rifle_CrouchIdle_Kneel"), 0f, 0f);
            AddCrouchChild(crouchTree, Clip("Rifle_CrouchWalk_Forward"), 0f, SoldierClipSpeeds.CrouchWalkForward);
            AddCrouchChild(crouchTree, Clip("Rifle_CrouchWalk_ForwardLeft"), -45f, SoldierClipSpeeds.CrouchWalkForwardDiagonal);
            AddCrouchChild(crouchTree, Clip("Rifle_CrouchWalk_Left"), -90f, SoldierClipSpeeds.CrouchWalkLeft);
            AddCrouchChild(crouchTree, Clip("Rifle_CrouchWalk_Right"), 90f, SoldierClipSpeeds.CrouchWalkRight);
            AddCrouchChild(crouchTree, Clip("Rifle_CrouchWalk_BackLeft"), -135f, SoldierClipSpeeds.CrouchWalkBackDiagonal);
            AddCrouchChild(crouchTree, Clip("Rifle_CrouchWalk_BackRight"), 135f, SoldierClipSpeeds.CrouchWalkBackDiagonal);

            // Direction comes from the takeoff velocity (the motor keeps it while airborne).
            // Points sit at 1 m/s so any moving jump picks its directional clip.
            BlendTree jumpTree = CreateLocomotionTree(controller, "JumpDirectional");
            AddChild(jumpTree, Clip("Rifle_Jump_InPlace"), 0f, 0f);
            AddChild(jumpTree, Clip("Rifle_Jump_Forward"), 0f, 1f);
            AddChild(jumpTree, Clip("Rifle_Jump_Backward"), 0f, -1f);
            AddChild(jumpTree, Clip("Rifle_Jump_Right"), 1f, 0f);
            AddChild(jumpTree, Clip("Rifle_Jump_Left"), -1f, 0f);

            AnimatorState stand = sm.AddState("Stand", new Vector3(300f, 0f));
            stand.motion = standTree;
            AnimatorState crouch = sm.AddState("Crouch", new Vector3(300f, 120f));
            crouch.motion = crouchTree;
            AnimatorState jump = sm.AddState("Jump", new Vector3(560f, 0f));
            jump.motion = jumpTree;
            sm.defaultState = stand;

            AddTransition(stand, crouch, AnimatorConditionMode.If);
            AddTransition(crouch, stand, AnimatorConditionMode.IfNot);

            AnimatorStateTransition toJump = stand.AddTransition(jump);
            toJump.hasExitTime = false;
            toJump.duration = 0.1f;
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            AnimatorStateTransition land = jump.AddTransition(stand);
            land.hasExitTime = true;
            land.exitTime = 0.85f;
            land.duration = 0.2f;

            BuildTurnStates(sm, stand, crouch, jump);
            BuildCoverStates(sm, controller, stand);

            // Traversal moves: played from code (SoldierTraversal) with CrossFade; the exit transition is a fallback.
            Vector3 slot = new Vector3(820f, -120f);
            foreach (TraversalAction action in TraversalActionBuilder.LoadAll())
            {
                AnimatorState state = sm.AddState(action.StateName, slot);
                state.motion = action.Clip;
                state.speed = action.Speed;
                AddExit(state, stand, 1f, action.EndBlend);
                slot.y += 70f;
            }

            BuildRelaxedArmsLayer(controller);
            BuildUpperBodyLayer(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SoldierAnimatorBuilder] Built {ControllerPath}");
        }

        /// <summary>
        /// Upper body override layer (spine, arms, head): Empty passes the base layer through;
        /// Fire, Reload and Hit play on top while the legs keep moving.
        /// </summary>
        static void BuildUpperBodyLayer(AnimatorController controller)
        {
            controller.AddLayer("UpperBody");
            AnimatorControllerLayer[] layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].avatarMask = LoadOrCreateMask(UpperBodyMaskPath, UpperBodyParts);
            layers[index].defaultWeight = 1f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine sm = controller.layers[index].stateMachine;
            AnimatorState empty = sm.AddState("Empty", new Vector3(300f, 0f));
            sm.defaultState = empty;

            AnimatorState fire = sm.AddState("Fire", new Vector3(560f, -100f));
            fire.motion = Clip("Rifle_Shot_Standing");

            AnimationClip reloadClip = Clip("Rifle_Reload_Standing");
            AnimatorState reload = sm.AddState("Reload", new Vector3(560f, 0f));
            reload.motion = reloadClip;
            reload.speed = reloadClip.length / SoldierWeapon.DefaultReloadTime;

            AnimatorState hit = sm.AddState("Hit", new Vector3(560f, 100f));
            hit.motion = Clip("Rifle_Hit_Front");

            AddAnyStateTransition(sm, fire, "Fire", 0.03f, canTransitionToSelf: true);
            AddAnyStateTransition(sm, reload, "Reload", 0.15f, canTransitionToSelf: false);
            AddAnyStateTransition(sm, hit, "Hit", 0.05f, canTransitionToSelf: true);

            // Grenade throw (SoldierGrenades.ThrowStarted). The clip is played at the throw duration.
            AnimationClip throwClip = Clip("Rifle_Grenade_Throw");
            AnimatorState throwState = sm.AddState("Throw", new Vector3(560f, 200f));
            throwState.motion = throwClip;
            if (throwClip != null) throwState.speed = Mathf.Max(1f, throwClip.length / 1.1f);
            AddAnyStateTransition(sm, throwState, "Throw", 0.1f, canTransitionToSelf: false);
            AddExit(throwState, empty, 0.9f, 0.15f);

            AddExit(fire, empty, 0.8f, 0.1f);
            AddExit(reload, empty, 0.92f, 0.15f);
            AddExit(hit, empty, 0.85f, 0.15f);
        }

        static void AddAnyStateTransition(AnimatorStateMachine sm, AnimatorState to, string trigger, float duration, bool canTransitionToSelf)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.canTransitionToSelf = canTransitionToSelf;
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        static void AddExit(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
        }

        /// <summary>
        /// Turn in place: 90 degree clips whose root rotation is not baked (MixamoImportProcessor), so the
        /// body turns through SoldierRootMotion. SoldierAnimator fires "Turn" with TurnDir -1 / +1 while
        /// standing still; moving cancels the turn.
        /// </summary>
        static void BuildTurnStates(AnimatorStateMachine sm, AnimatorState stand, AnimatorState crouch, AnimatorState jump)
        {
            AddTurnState(sm, stand, "TurnLeft", "Rifle_TurnInPlace_Left", -1f, new Vector3(40f, -60f), jump);
            AddTurnState(sm, stand, "TurnRight", "Rifle_TurnInPlace_Right", 1f, new Vector3(40f, 20f), jump);
            AddTurnState(sm, crouch, "CrouchTurnLeft", "Rifle_CrouchTurn90_Left", -1f, new Vector3(40f, 120f), null);
            AddTurnState(sm, crouch, "CrouchTurnRight", "Rifle_CrouchTurn90_Right", 1f, new Vector3(40f, 190f), null);
        }

        static void AddTurnState(AnimatorStateMachine sm, AnimatorState idle, string name, string clip, float dir,
                                 Vector3 position, AnimatorState jump)
        {
            AnimatorState turn = sm.AddState(name, position);
            turn.motion = Clip(clip);
            turn.tag = SoldierAnimator.TurnStateTag;
            turn.speedParameterActive = true;
            turn.speedParameter = "TurnRate";

            AnimatorStateTransition enter = idle.AddTransition(turn);
            enter.hasExitTime = false;
            enter.duration = 0.15f;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Turn");
            enter.AddCondition(dir < 0f ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater, 0f, "TurnDir");

            // Moving cancels the turn; otherwise it plays out.
            AnimatorStateTransition move = turn.AddTransition(idle);
            move.hasExitTime = false;
            move.duration = 0.2f;
            move.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
            AddExit(turn, idle, 0.9f, 0.15f);

            if (jump != null)
            {
                AnimatorStateTransition toJump = turn.AddTransition(jump);
                toJump.hasExitTime = false;
                toJump.duration = 0.1f;
                toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            }
        }

        /// <summary>
        /// Back-to-wall high cover with the rifle. The soldier's transform faces away from the wall (SoldierMotor);
        /// MoveX slides along it (left sneak at -speed, right at +speed). Low cover uses the crouch states
        /// (kneel facing the cover, rifle in hand) instead.
        /// </summary>
        static void BuildCoverStates(AnimatorStateMachine sm, AnimatorController controller, AnimatorState stand)
        {
            // Rifle cover clips: the body faces away from the wall (back against it), looks out to its right
            // and holds the rifle.
            AnimatorState high = sm.AddState("CoverHigh", new Vector3(300f, 260f));
            high.motion = CoverTree(controller, "CoverHighTree", "Cover_Idle_RifleHigh", "Cover_Sneak_RifleRight", "Cover_Sneak_RifleLeft",
                                    SoldierClipSpeeds.CoverSneakHigh);
            // Looking out to the left: the same clips mirrored (Mixamo has one side only). Mirroring swaps the
            // hands, so WeaponGrip moves the rifle to the left hand in this state. Mirrored sneaks travel the
            // other way, so the right sneak sits at -speed.
            AnimatorState mirror = sm.AddState(SoldierAnimator.CoverMirrorState, new Vector3(300f, 330f));
            mirror.mirror = true;
            mirror.motion = CoverTree(controller, "CoverHighMirrorTree", "Cover_Idle_RifleHigh", "Cover_Sneak_RifleLeft", "Cover_Sneak_RifleRight",
                                      SoldierClipSpeeds.CoverSneakHigh);

            AddCoverTransitions(sm, stand, high, AnimatorConditionMode.IfNot);
            AddCoverTransitions(sm, stand, mirror, AnimatorConditionMode.If);
        }

        static void AddCoverTransitions(AnimatorStateMachine sm, AnimatorState stand, AnimatorState state, AnimatorConditionMode mirrored)
        {
            AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.duration = 0.3f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "InCover");
            enter.AddCondition(mirrored, 0f, "CoverMirror");

            AnimatorStateTransition leave = state.AddTransition(stand);
            leave.hasExitTime = false;
            leave.duration = 0.25f;
            leave.AddCondition(AnimatorConditionMode.IfNot, 0f, "InCover");
        }

        /// <summary>1D on MoveX: sidestep one way, idle, sidestep the other way (clips' own travel direction).</summary>
        static BlendTree CoverTree(AnimatorController controller, string name, string idle, string plusX, string minusX, float speed)
        {
            BlendTree tree = Create1DTree(controller, name, "MoveX");
            tree.AddChild(Clip(minusX), -speed);
            tree.AddChild(Clip(idle), 0f);
            tree.AddChild(Clip(plusX), speed);
            return tree;
        }

        /// <summary>
        /// Arms-only override that carries the rifle low (gun down) while moving without aiming.
        /// SoldierAnimator drives its weight from the rifle-ready weight and speed; the spine and legs keep
        /// the locomotion clip. Sits under UpperBody so fire / reload / hit still win.
        /// </summary>
        static void BuildRelaxedArmsLayer(AnimatorController controller)
        {
            controller.AddLayer(SoldierAnimator.RelaxedArmsLayer);
            AnimatorControllerLayer[] layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].avatarMask = LoadOrCreateMask(ArmsMaskPath, ArmParts);
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine sm = controller.layers[index].stateMachine;
            // ArmsAim 0 = gun down (moving relaxed), 1 = aim idle arms (standing turns with the rifle up).
            BlendTree arms = Create1DTree(controller, "ArmsTree", "ArmsAim");
            arms.AddChild(Clip("Rifle_Idle_GunDown"), 0f);
            arms.AddChild(Clip("Rifle_AimIdle"), 1f);
            AnimatorState carry = sm.AddState("GunDown", new Vector3(300f, 0f));
            carry.motion = arms;
            sm.defaultState = carry;
        }

        static readonly AvatarMaskBodyPart[] UpperBodyParts =
        {
            AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK,
        };

        static readonly AvatarMaskBodyPart[] ArmParts =
        {
            AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
            AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK,
        };

        static AvatarMask LoadOrCreateMask(string path, AvatarMaskBodyPart[] activeParts)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, System.Array.IndexOf(activeParts, part) >= 0);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        static AnimatorController LoadOrCreateController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (sub != controller) Object.DestroyImmediate(sub, true);
            }

            // Start empty; Build() adds everything back.
            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            return controller;
        }

        /// <summary>
        /// Standing idle: AimBlend 0 = relaxed idles (IdleVariant picks one), 1 = aim idle (rifle up).
        /// SoldierAim fades AimBlend down when the soldier stands still without aiming.
        /// </summary>
        static BlendTree CreateIdleTree(AnimatorController controller)
        {
            BlendTree relaxed = Create1DTree(controller, "RelaxedIdle", "IdleVariant");
            // Only idles whose body stays square to the facing. Rifle_Idle_Ready is bladed 55 degrees and
            // Rifle_Idle_Relaxed turns the whole body -46..+42 degrees (looking around); both made the standing
            // soldier writhe in place.
            string[] relaxedClips = { "Rifle_Idle_GunDown" };
            if (relaxedClips.Length != SoldierAnimator.RelaxedIdleCount)
                throw new System.InvalidOperationException("Update SoldierAnimator.RelaxedIdleCount");
            for (int i = 0; i < relaxedClips.Length; i++) relaxed.AddChild(Clip(relaxedClips[i]), i);

            BlendTree idle = Create1DTree(controller, "Idle", "AimBlend");
            idle.AddChild(relaxed, 0f);
            idle.AddChild(Clip("Rifle_AimIdle"), 1f);
            return idle;
        }

        /// <summary>
        /// Running forward: AimBlend 0 (sprinting, rifle not aimed) = relaxed carry, 1 = aimed run.
        /// Both play at the speed that covers the same ground.
        /// </summary>
        static BlendTree CreateRunForwardTree(AnimatorController controller)
        {
            float speed = SoldierClipSpeeds.RunForward * SoldierClipSpeeds.RunPlayback;
            BlendTree tree = Create1DTree(controller, "RunForward", "AimBlend");
            tree.AddChild(Clip("Rifle_Sprint_Forward"), 0f);
            tree.AddChild(Clip("Rifle_Run_Forward"), 1f);
            ChildMotion[] children = tree.children;
            children[0].timeScale = speed / SoldierClipSpeeds.SprintForward;
            children[1].timeScale = SoldierClipSpeeds.RunPlayback;
            tree.children = children;
            return tree;
        }

        static BlendTree Create1DTree(AnimatorController controller, string name, string parameter)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        static BlendTree CreateLocomotionTree(AnimatorController controller, string name)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        static void AddChild(BlendTree tree, Motion motion, float x, float y, float timeScale = 1f)
        {
            tree.AddChild(motion, new Vector2(x, y));
            ChildMotion[] children = tree.children;
            children[children.Length - 1].timeScale = timeScale;
            tree.children = children;
        }

        /// <summary>Adds a crouch clip at angle (0 = forward, 90 = right), matched to the motor's crouch speed.</summary>
        static void AddCrouchChild(BlendTree tree, Motion clip, float angle, float clipSpeed)
        {
            float rad = angle * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            float speed = SoldierMotor.Crouch.Evaluate(dir);
            AddChild(tree, clip, dir.x * speed, dir.z * speed, speed / clipSpeed);
        }

        static void AddTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0.2f;
            t.AddCondition(mode, 0f, "Crouch");
        }

        /// <summary>Loads the clip from Mixamo/&lt;file&gt;.fbx (the importer names it after the file).</summary>
        static AnimationClip Clip(string file)
        {
            string path = MixamoFolder + file + ".fbx";
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == file);
            if (clip == null) throw new System.IO.FileNotFoundException($"Clip '{file}' not found in {path}");
            return clip;
        }
    }
}
