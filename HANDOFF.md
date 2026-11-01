# Handoff: War Mission animation + AI project

Read this first after a context reset. `CLAUDE.md` holds the full spec and a detailed progress log;
this file is the short "where are we, how do I work, what is next" version.

Last updated: 2026-09-23.

## Environment and how to work

- Unity 6000.6.0f1, HDRP 17.7, Input System only, AI Navigation 2.0.14, Animation Rigging 6.6.0 (installed, no longer used by the soldier).
- Project: `D:\Unity-Projects\Animation System Scratch`. Code in `Assets/_Project`. Namespace `WarMission` (editor: `WarMission.EditorTools`).
- Drive the open Editor with the Unity CLI from **Bash** (PowerShell breaks quoting):
  - `unity status`, `unity command eval '<C#>'`, `unity command eval_file --file <path.cs>` (long scripts; write to the scratchpad).
  - Compile check: `AssetDatabase.Refresh()` then poll `unity command console_status` for `"compiling":false`, then `recompile_status`.
  - Play tests: `editor_play`, then `eval 'UnityEngine.Application.runInBackground = true;'` (play mode does not tick while the Editor is unfocused). `editor_stop` when done.
  - Keys: `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, ...))`; mouse: `new MouseState().WithButton(MouseButton.Right)`.
  - Screenshots: `capture_game_view --camera TmpCam --save_path "Temp/x.png"` writes to `Assets/Temp`; delete that folder after (`AssetDatabase.DeleteAsset("Assets/Temp")`, may need a retry).
  - The game clock runs fast in the background; use `Time.timeScale` 0.03-0.1 for short moves, or trace per frame with an `Application.onBeforeRender` lambda that writes to a file.
  - **The user often plays while tests run.** Their input mixes with simulated keys. Prefer direct calls (e.g. `PlayerCover.TryEnter`, `SoldierTraversal.TryBegin`, `TraversalProbe.Scan`) and check `Soldier.Command` for foreign input.
- Import clips **one file at a time** (`ImportAsset` per file). A big recursive ForceUpdate reimport crashed Unity once.
- Script API updater dialogs can block compiles; ask the user to click "Yes, just for these files".
- The user downloads Mixamo clips (login needed) into `D:\Downloads\Unity-Mixamo-Animation\<folder>`. The built-in browser can search mixamo.com without login (type in the search box; URL query does not work). Downloads settings: Without Skin, 30 fps, In Place unticked for traversal/cover moves.

## Editor menu tools (War Mission menu) — re-run after changes

| Menu | File | Does |
|---|---|---|
| Build Soldier Animator | `Editor/SoldierAnimatorBuilder.cs` | Rebuilds `Controllers/Soldier.controller` in place |
| Build Soldier Prefab | `Editor/SoldierPrefabBuilder.cs` | Ragdoll, rifle, components on `Soldier.prefab`; variants Player / Ally / Enemy |
| Build Traversal Actions | `Editor/TraversalActionBuilder.cs` | Bakes `Animations/Traversal/*.asset`, then rebuilds the Animator, assigns to prefab |
| Generate Cover Points | `Editor/CoverPointGenerator.cs` | Cover points around boxes under "Environment" (needs NavMesh) |

`MixamoImportProcessor` auto-configures clips in `Animations/Mixamo/`: humanoid, Soldier avatar, rotation + Y baked, XZ root motion extracted (ignored), loop if the 2nd name token is in `LoopActions`, **never mirror**.

## Scenes and prefabs

- `Scenes/TestRange.unity`: player, 3 dummies, Enemy_A/B patrolling, 12 boxes, 73 cover points.
- `Scenes/SquadBattle.unity`: 3 allies vs 3 enemies, BattleCamera + SquadBattleMonitor (Enter restarts).
- Prefabs: `Soldier` (body, Enemy team, used as dummy), `Soldier_Player` (Ally, PlayerSoldierInput/PlayerCover/PlayerHud/PlayerRestart), `Soldier_Ally`, `Soldier_Enemy` (AI: AIPerception, AIPathFollower, AICover, AISoldierBrain).
- NavMesh: agent radius 0.35, height 1.8, step 0.3, surface minRegionArea 0.2, height mesh on.

## Done (user accepted unless noted)

1. M1 locomotion (8-way walk/run, crouch 8-way from user clips, jump directional).
2. M2 aim/fire: hitscan rifle, ragdoll, dummies, recoil, HUD (IMGUI debug).
3. M3 AI: patrol / investigate / engage, perception (sight, hearing, attacker).
4. M4 teams + squads (formation, shared sightings, no friendly fire), SquadBattle scene.
5. Realism: rifle up only on aim (RMB) / fire; relaxed idles, relaxed arms layer while moving, relaxed sprint clip; wall obstruction lowers the rifle; WeaponGrip (rifle from right palm through left palm, no IK).
6. Traversal (GASP style, Space): vault (1-hand clip), mantle (hop stand-in), drop (low hop < 1.05 m, rail clip above), auto step-down off low edges; motion warping via `TraversalAction` curves; rifle slung on the back.
7. M5 cover: CoverPoints, suppression, AI TakeCover (hide / peek / flank / give up). Player cover on **Q** (built, needs playtest).

## In progress right now

**AI decision layer (2026-11-01, cloud session, NOT compiled in Unity yet):** utility AI on top of the brain.
`AIPersonality`, `AIDecisionMaker`, `AITactics`, `AIDebugLabel` (F3) are new; `AISoldierBrain`, `AICover`,
`AIPerception`, `Squad` changed. Details in CLAUDE.md progress log. First steps next time: open Unity, read the
console, fix compile errors, run War Mission > Build Soldier Prefab, play SquadBattle with F3 on, tune the
scores in `AIDecisionMaker.Score` (M5 rule: AI under fire in the open must still go to cover).
Pass 2 (same day, also not compiled in Unity): grenades (player G + AI throw + AI dodge), gradual spotting with
suspicion, footstep hearing, threat-based target choice, danger zones, callouts (F4). Also run Build Soldier
Animator (new `Throw` state). Check: grenade arc lands on target, blast hurts through no walls, AI runs from it.

**Cover polish after the user's screenshots** (code done and compiled, **not yet play-tested**):
- Low cover = kneel facing the cover with the rifle (crouch states), not back-to-wall. Animator only keeps `CoverHigh` (back-to-wall, `InCover` bool); `CoverLow` param is always false.
- RelaxedArms off while crouched (fixed rifle behind the back).
- Wall-obstruction check ignores low cover tops below the chest (can shoot over low cover).
- Player tall-cover auto-peek: aim/fire → `PlayerCover.BeginPeek` steps out past the nearest edge (left or right), release → steps back (`IsPeeking` / `IsReturning`, `PlayerSoldierInput.ApplyCover`).

**Rifle cover clips: wired, waiting for the user's playtest.** From `D:\Downloads\Unity-Mixamo-Animation\cover`:
`Cover_Idle_RifleHigh` (Mixamo "Taking Cover Idle", the user's "left" download, rifle stance 44 deg, loops),
`Cover_Sneak_RifleLeft` / `Cover_Sneak_RifleRight` (In Place, 1.08 m/s at -88 / +88 deg), `Cover_Enter_Rifle`
("Taking Cover", turns 176 deg over 1.3 m, not used yet). All hold the rifle (palm gap 0.26 m).
- High cover: `SoldierMotor` faces AWAY from the wall (-CoverForward) because these clips face out; low cover
  faces the cover (kneel). Animator `CoverHigh` = 1D MoveX: RifleLeft @ -1.08, idle @ 0, RifleRight @ +1.08.
  RelaxedArms is 0 in cover. `SoldierClipSpeeds.CoverSneakHigh` = 1.08.
- User playtest (2026-09-23): low cover kneel + pop-up works, vault from cover works; asked for a right-side pose.
- DONE (waiting for playtest): **right-side tall cover** without a new clip. Animator state `CoverHighMirror`
  (same clips, `mirror = true`, sneaks swapped) entered when bool `CoverMirror` is on. `SoldierCommand.CoverFaceLeft`
  -> `SoldierMotor.IsCoverFaceLeft` -> `CoverMirror`. `WeaponGrip.LeftHandWeight` follows the Animator's blend into
  that state and moves the rifle grip to the left palm, barrel through the right palm (rifle stays parented to the
  right hand; `localPosition` is reset each Solve). Side: player = last slide / peek direction, on entry the
  nearest edge (`PlayerCover.FaceLeft`, `FindEdge` prefers the current side); AI = `CoverPoint.PeekSide > 0`.
  Verified: Block_9 left end -> CoverHigh looking left edge, right end -> CoverHighMirror, rifle grip 0 cm from the
  left palm; SquadBattle AI uses both states. If the mirrored look is not good enough, a real right-side clip
  (Mixamo mirror download or Kimodo) can replace the mirrored idle in `BuildCoverStates`.
- Video review 2 (2026-09-23) fixes: the tall drop (`Traverse_Drop`, rail clip) now stops at the landing squat
  (PlayUntil 0.285, EndBlend 0.4; the clip rolled forward and kneeled after landing, which looked like lying on
  the ground). `ThirdPersonCamera` follows the hips height while `SoldierTraversal.IsActive` (root stays at the start
  height during climbs/drops, so the soldier left the frame). Verified: drop off 2.13 m lands (head min 1.12 m),
  stands at 1.6 s; high climb camera rises with the body. Open: sling looks muzzle-down on the climb.
- Test gotcha: `Application.runInBackground` can reset during a session (frames stop); set it before each step.

**Turn in place (2026-10-08, waiting for playtest):** standing still, the feet stay planted and the spine aims; past
60 deg (45 aimed) a 90 deg turn clip plays and its root rotation turns the body (`SoldierRootMotion` ->
`SoldierMotor.ApplyRootYaw`, scaled to end on the aim, crossfade-compensated). Clips `Rifle_TurnInPlace_Left/Right`,
`Rifle_CrouchTurn90_Left/Right` import with rotation NOT baked + a start-yaw offset (`MixamoImportProcessor.TurnStartYaw`).
Standing turns take the arms from the idles (RelaxedArms layer is a 1D blend on `ArmsAim`). The right turn clip starts
with the rifle across the chest; a Mixamo "Rifle Turn Right" would be better. AI fires when the muzzle is on target.

**Real models (2026-10-08):** source files in `D:\Unity-Projects\soldires` (Blender work, exports in `export/`).
- Polish soldier = enemies (swapped 2026-10-08: Ghost is the hero). Its GLB rig was renamed to mixamorig names in Blender (hips pivot moved onto the
  pelvis, Beryl rifle removed, rest A-pose) -> `Characters/Polish/Polish_Soldier.fbx`.
- M4 (kit's assembled carbine + flash hider) -> `Weapons/M4/M4.fbx` with empties RightPalm / LeftPalm / Muzzle.
  The FBX root holds the axis conversion; the builder works in the asset's space (bore = +Z).
- **War Mission > Build Imported Assets** (`Editor/ImportedAssetBuilder.cs`): HDRP Lit materials from each folder's
  `materials.json` (glTF metallic-roughness / ORM -> generated `_mask.png`), FBX material remaps, Humanoid avatar with
  an explicit Spine/Chest mapping (auto-map skipped Spine and took helmet/mask for eyes/jaw).
- **Build Soldier Prefab** now puts the M4 on every soldier and `ApplyBody` adds the Polish body to Soldier_Player /
  Soldier_Ally (Ghost) and Soldier_Enemy (Polish) as child `Body` (base `Model` switched off) so scene references survive.
- Ghost = player + allies. Mixamo's auto-rigger refused it; the user rigged it with AccuRig (CC_Base bones) ->
  `Characters/Ghost/Ghost_Soldier.fbx`; Unity auto-maps it fully. Do NOT import that FBX into Blender (its importer
  lays the mesh flat); textures come from the original GLB (`Characters/Ghost/materials.json`).
- Left hand fingers stay open on the handguard (clip pose); a finger curl would help.
- Unity crashed once in `UpdateAsyncShaderVariants` while importing ~60 large textures; reopening fixed it.

## Still to do after that

- DONE: high climb. `Traverse_Climb_Box` (Mixamo "box Climbing", 3.8 s, climbs onto ~2.2 m) is action
  `Traverse_ClimbHigh` (Mantle, ScaleToEnd, 1.4-2.4 m, speed 1.15). Verified: climbs the 2.13 m Block_9, ends on top
  (toe 2.14), rifle back in hand. Also imported, unused: `Traverse_Climb_Sprint` (Mixamo "Sprint To Wall Climb",
  runs 3.2 m then climbs 2.5 m; candidate for a moving high climb). "Climb little fence and look down" skipped
  (no height change). The low mantle (0.3-1.4 m) is still the rifle hop; a real low climb clip would improve it.
- Option for clips Mixamo lacks (e.g. the right-facing rifle cover idle): NVIDIA Kimodo text-to-motion is
  installed at `D:\Nvidia-Kumdo` (read `D:\Nvidia-Kumdo\SESSION_PROGRESS.md`: env vars TEXT_ENCODER_DEVICE=cpu,
  HF_HOME=D:\Nvidia-Kumdo\hf-cache; needs ~16 GB free RAM; `kimodo_gen "prompt" --model Kimodo-SOMA-RP-v1.1
  --duration "N" --num_samples 3 --bvh --output path\stem`). Pipeline: BVH -> Blender -> FBX on a Mixamo-named rig
  -> Unity Humanoid (auto-mapping should work). No rifle in generated motion; WeaponGrip follows the palms.
  Existing generated FBX (from the user's UE5 project) live in `D:\Animation-Hogwards\`.
- Optional polish: cover enter/exit clips (`Cover_Enter_*`, `Cover_Exit_*`, "Taking Cover", "Emerging"), HUD prompt
  "Q: Cover", Mixamo "Emerging Right From Cover To Aimed Rifle Walk".
- **Milestone 6 (last):** mission — objectives, enemy waves / spawners, win & lose conditions, real HUD (health,
  ammo, objective, hit markers, cover prompt), mission start to finish. Replace IMGUI debug HUD and
  PlayerRestart/DummyRespawner test helpers.

## Things the user may need to do manually

- Download Mixamo clips (login). Click Unity dialogs (API updater, Safe Mode). If Unity crashes: reopen from the Hub
  (it once dropped the project from the Hub list; `unity projects add "<path>"` fixes that).
- Graphics driver reset (Win+Ctrl+Shift+B) if the screen goes black under HDRP.
