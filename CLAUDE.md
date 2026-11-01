# Unity War Mission: Soldier Animation and AI System

> Current state, working method and next steps: see `HANDOFF.md` (read it first after a context reset).

## Project goal

Build a small COD-style war mission in Unity. Ally soldiers and enemy soldiers move, take cover, and fire at each other. The player controls one soldier.

Build every system from scratch. No Asset Store animation or AI frameworks. Unity packages (AI Navigation, Animation Rigging, Input System) are allowed.

## Project setup (resolved)

- Unity version: 6000.6.0f1 (Unity 6.6)
- Render pipeline: HDRP 17.7
- Input: new Input System 1.20 (`Assets/InputSystem_Actions.inputactions`)
- Packages: AI Navigation 2.0.14, Animation Rigging 6.6.0
- Editor control: Unity CLI (`unity command ...`) through `com.unity.pipeline`. Do not remove that package.
  On Windows, run `unity command eval` from Bash; PowerShell splits quoted C# into extra args.

## Core design rule: one body, two brains

Every soldier shares the same body code. Only the input source changes.

- `PlayerSoldierInput` reads keyboard and mouse.
- `AISoldierBrain` decides what to do.
- Both write the same `SoldierCommand` struct every frame.
- The soldier core reads the command and acts. The core never knows who sent the command.

```csharp
public struct SoldierCommand
{
    public Vector2 Move;       // local input, x = strafe, y = forward, range -1..1
    public Vector3 AimPoint;   // world-space point to aim at
    public bool Fire;
    public bool Reload;
    public bool Crouch;
    public bool Sprint;
}

public interface ISoldierInput
{
    SoldierCommand GetCommand();
}
```

## Components

| Component | Responsibility |
| --- | --- |
| `Soldier` | Root component. Holds references, reads `ISoldierInput`, passes the command to other parts. |
| `SoldierMotor` | Movement through `NavMeshAgent`. Player uses `agent.Move()`, AI uses `SetDestination()`. Handles crouch and sprint speed. |
| `SoldierWeapon` | Hitscan raycast rifle. Fire rate, magazine, reload, spread, damage, muzzle flash, tracer. |
| `SoldierAnimator` | Sends speed, direction, crouch, jump, aim blend, idle variant, fire, reload, hit to the Animator. |
| `SoldierAim` | (added) LateUpdate: grip, then spine turns so the muzzle hits AimPoint; ready/relaxed weight; recoil. |
| `WeaponGrip` | (added) Rifle from right palm through left palm each frame (replaces left hand IK). |
| `Health` | Implements `IDamageable`. Takes damage, raises `OnDeath`. |
| `SoldierRagdoll` | On death: disable Animator, agent, colliders. Enable ragdoll rigidbodies. Apply hit force. |
| `TeamMember` | `Team` enum (Ally, Enemy). Registers in `TeamRegistry` for target lookup. |
| `PlayerSoldierInput` | `ISoldierInput` for the player. Camera-relative movement, aim from camera center ray. |
| `AISoldierBrain` | `ISoldierInput` for AI. State machine plus perception. |
| `AIPerception` | Vision cone and hearing. Finds visible enemies through `TeamRegistry`. |
| `CoverPoint` | Marker placed in the level. Stores facing direction and crouch or standing cover. |

## AI brain

States:

- `Patrol`: walk between waypoints.
- `Investigate`: move to last heard gunshot or last seen position.
- `Engage`: aim and fire at a visible enemy.
- `TakeCover`: move to the best nearby `CoverPoint`, crouch, peek, fire.
- `Dead`: no commands.

Perception defaults (tune later):

- Vision: 110 degree cone, 40 m range, line-of-sight raycast to head and chest.
- Hearing: gunshots within 30 m trigger `Investigate`.
- Reaction delay: 0.3 to 0.6 s before first shot.
- AI spread: wider than the player, shrinks while the target stays visible.

Target selection: closest visible enemy, prefer the enemy firing at this soldier.

## Animation setup

Animator layers:

1. Base layer: 2D freeform directional blend tree for 8-way rifle locomotion. Parameters `MoveX`, `MoveY`. Separate blend tree for crouch, switched with `Crouch` bool.
2. Upper body layer: avatar mask on spine, arms, head. States for fire, reload, hit reaction. Triggers `Fire`, `Reload`, `Hit`.

Animation Rigging:

- Multi-Aim Constraint on spine and chest toward `AimPoint`.
- Two Bone IK on left arm to keep the left hand on the rifle grip.

Death: ragdoll, no death animation.

Animator parameters:

| Name | Type |
| --- | --- |
| `MoveX` | float |
| `MoveY` | float |
| `Speed` | float |
| `Crouch` | bool |
| `Aiming` | bool |
| `Fire` | trigger |
| `Reload` | trigger |
| `Hit` | trigger |

Smooth `MoveX` and `MoveY` with damping (`animator.SetFloat(name, value, 0.1f, Time.deltaTime)`) to avoid snapping.

## Mixamo animations

Download settings (set by the user in the browser):

- In Place: ticked for all locomotion clips.
- Trim: 0 to 100.
- Mirror: unticked. Download both sides; rifle clips must never be mirrored (the rifle ends up in the wrong hand).
- Format: FBX Binary.
- Skin: With Skin for the first file only (character model). Without Skin for all clips.
- FPS: 30 for locomotion, idle, crouch, reload. 60 allowed for fire, recoil, hit reactions.
- Keyframe Reduction: none.

Clip list (rifle versions):

- Rifle idle, rifle aiming idle
- Walk and run: forward, back, left, right, four diagonals
- Crouch idle, crouch walk in 8 directions
- Fire standing, fire crouched
- Reload
- Hit reaction front, hit reaction back
- Grenade throw (optional)

File naming: `Rifle_<Action>_<Direction>.fbx`, for example `Rifle_Run_Forward.fbx`, `Rifle_CrouchWalk_BackLeft.fbx`.

## Import rules (automate with an editor script)

Write `Editor/MixamoImportProcessor.cs` (an `AssetPostprocessor`) for files under `Assets/_Project/Animations/Mixamo/`:

- Rig: Humanoid. Copy avatar from the skinned character model.
- Loop Time: on for idle and locomotion clips.
- Root Transform Rotation, Position Y: bake into pose. Position XZ: NOT baked (extracted as root motion,
  ignored because `applyRootMotion` is off). This keeps clips downloaded without "In Place" in place.
  The Basic Shooter Pack was downloaded without In Place.
- Anim. Compression: Optimal.
- Mirror: never. Mirroring a rifle clip swaps the hands (seen in playtest: walk left held the rifle by the face).
  Missing side: use a different real clip and match its speed with playback scale, or let the blend tree cover it.

## Folder structure

```
Assets/_Project/
  Animations/
    Mixamo/          raw FBX files
    Controllers/     Animator controllers, avatar masks
  Characters/        soldier model, prefabs
  Scripts/
    Soldier/         Soldier, Motor, Weapon, Animator, Health, Ragdoll, Team
    AI/              Brain, Perception, CoverPoint, states
    Player/          PlayerSoldierInput, camera
    Mission/         objectives, spawners, game state
  Editor/            import processors, setup tools
  Scenes/
  Prefabs/
```

## Milestones

Work on one milestone at a time. After each one, stop and ask the user to playtest in Unity.

1. Player soldier with 8-way rifle locomotion, crouch, third-person camera.
   Done when: the soldier strafes in all directions while facing the aim direction, with no foot sliding at walk speed.
2. Aim and fire. Raycast shots, muzzle flash, damage, ragdoll death, target dummies.
   Done when: the player shoots and kills a dummy, and the dummy ragdolls.
3. AI brain on the same soldier prefab. Patrol, perception, engage the player.
   Done when: an enemy spots the player, turns, and fires.
4. Teams. Ally AI and enemy AI fight each other.
   Done when: two squads of 3 fight without player input until one side dies.
5. Cover system. Cover points, move to cover, crouch, peek, fire.
   Done when: AI under fire moves to cover instead of standing in the open.
6. Mission. Objectives, enemy waves, win and lose conditions, simple HUD.
   Done when: the mission plays start to finish.

## Working rules for Claude Code

- Use the Unity CLI (`unity status`, `unity command ...`) for scene and asset work when the Editor is connected. Otherwise write scripts and editor tools and give the user exact editor steps.
- Keep scripts small, one responsibility each.
- No `FindObjectOfType` in `Update`. Cache references.
- Expose tuning values with `[SerializeField]` and `[Header]` for playtesting.
- Read the Unity console after changes and fix errors before handing back.
- The user judges animation quality. Ask for feedback after visual changes.

## Future option

Motion matching for locomotion: https://github.com/JLPM22/MotionMatching (MIT, Unity 6+). The package reads BVH files, so Mixamo FBX clips need a Blender batch conversion to BVH. Not part of the current scope.

## Imported clips

Character: `Assets/_Project/Characters/Soldier.fbx` (avatar `SoldierAvatar`, source of all clips).
Clips in `Assets/_Project/Animations/Mixamo/`. Speeds measured from the clips (use for motor speeds and blend tree thresholds):

| Clip | Speed m/s | Notes |
| --- | --- | --- |
| Rifle_AimIdle | 0 | also used as plain idle |
| Rifle_Walk_Forward / Backward | 0.95 / 1.05 | |
| Rifle_Walk_Right / Left | 0.72 / 1.32 | Left is the pack's `strafe left`, played at 0.76x |
| Rifle_Run_Forward / Backward | 2.85 / 2.63 | |
| Rifle_Run_Right / Left | 3.11 / 2.69 | |
| Rifle_CrouchWalk_Forward | 0.82 | |
| Rifle_CrouchWalk_Left / Right | 1.01 / 0.98 | from crouch sets |
| Rifle_CrouchWalk_ForwardLeft | 1.69 | diagonal, played at 0.59x (forward-right is blended from Forward + Right) |
| Rifle_CrouchWalk_BackLeft / BackRight | 1.75 | diagonal, played at 0.46x |
| Rifle_Fire_Standing | loop | 1.17 s burst |
| Rifle_Shot_Standing | once | 0.27 s single shot |
| Rifle_Reload_Standing / Kneeling | once | 3.3 s / 6.9 s |
| Rifle_Hit_Front | once | direction not verified |
| Rifle_Grenade_Throw | once | |

Unused extras (prone, turns, jumps, stops, transitions, death) are in `Mixamo/_Unsorted/`.

Still missing from Mixamo: rifle crouch idle (a generated kneel is used), straight crouch back, crouched fire, second hit reaction.

## Progress log

- Setup: done. Packages installed, folder structure, `MixamoImportProcessor`, clips imported and sorted.
- Milestone 1: DONE (accepted by user 2026-09-23).
- Extra (user request): rifle jump. `SoldierCommand.Jump` (Space / gamepad South). `SoldierMotor` keeps the
  takeoff velocity for `jumpLockTime` (0.65 s); no jump while crouched. Height comes from the clips; the
  agent stays on the NavMesh, so it is a hop, not a climb. Animator state `Jump` (trigger `Jump`) is a 2D
  blend: Rifle_Jump_InPlace (0,0), _Forward (0,1), _Backward (0,-1), _Right (1,0), _Left (-1,0); exits at 0.85.
  Unused from the jump folder: `J_Rifle_Jump_1` (0.93 s wind-up), `J_Run_Strafing_Jump` (direction unclear).
  Verified all five in slow motion.
- Milestone 2: DONE (accepted by user 2026-09-23, after fixing mirrored walk-left rifle hold).
- Milestone 3: DONE (accepted by user 2026-09-23: patrol/engage look natural, difficulty right, hearing works).
- Polish after M3 (user requests), waiting for playtest:
  - Relaxed idle. `SoldierCommand.Aim` added. Player: right mouse / left trigger, and 3 s after firing.
    AI: Aim in Investigate/Engage, relaxed on patrol. `SoldierAim.ReadyWeight` rises (7/s) when aiming, firing,
    moving, crouched or jumping; falls (2/s) otherwise. `Soldier` holds Fire until `SoldierAim.IsReady`.
    Animator: Stand tree idle child = 1D `Idle` blend on `AimBlend` (0 = `RelaxedIdle`, 1 = Rifle_AimIdle);
    `RelaxedIdle` = 1D on `IdleVariant`: Rifle_Idle_Relaxed, Rifle_Idle_GunDown, Rifle_Idle_Ready
    (from the user's "Riffile idle" folder); variant changes every 10-20 s. Crouch idle = real clip
    Rifle_CrouchIdle_Kneel (the generated single-frame crouch idle and `Animations/Generated` were removed).
  - Camera zooms to 45° FOV while the aim button is held (`PlayerSoldierInput.AimHeld`).
  - Run 25% faster: `SoldierClipSpeeds.RunPlayback = 1.25` (run forward 3.56 m/s).
  - `WeaponGrip` replaces the left hand IK: each frame the rifle runs from the right palm through the left palm
    (every Mixamo clip holds its invisible rifle at a different wrist angle; a fixed right-hand hold pointed
    62° up while running and the left hand could not reach). Falls back to the fixed hold while reloading.
    Animation Rigging is no longer used by the soldier.
  - Sprint (Shift) carries the rifle as the run clip does: no spine aim, no firing. Fire or aim cancels sprint
    (so aiming while holding Shift walks).
  - Relaxed sprint (user's "idle rifle runnning" folder): Rifle_Sprint_Forward (in place, 2.28 m/s, rifle carried
    low). Stand tree's run-forward point is a 1D `RunForward` blend on AimBlend: 0 = Rifle_Sprint_Forward at
    1.56x, 1 = Rifle_Run_Forward at 1.25x, both covering 3.56 m/s. Side/back sprint keep the aimed run clips.
  - User accepted the polish (relaxed sprint and leg speed look right).
- Milestone 4: built, waiting for user playtest.
  - `Soldier_Ally.prefab` (Ally + AI brain). Team colours on the `Beta_Surface` body mesh: `Materials/Team_Ally`
    (blue, also the player) and `Team_Enemy` (red). Dummies keep the default skin. All via Build Soldier Prefab.
  - `Squad` (parent of its AI members): members report sightings (every 0.5 s max); squad mates that are not
    engaged investigate the report. Formation slots 0, -3, +3 m sideways from the route (and around
    investigate spots so they do not stack).
  - Brain holds fire when the first thing along the shot is a living teammate (`LineOfFireClear`).
  - Scene `Scenes/SquadBattle.unity` (in Build Settings): no player; AllySquad starts at z -24, EnemySquad at
    z +24, each walks a 2-point route toward the middle. `SquadBattleMonitor` shows alive counts / time / winner
    (Enter restarts). `BattleCamera` frames all living soldiers.
  - Verified: 5 battles, all ended with one side dead in 8-10 s; enemies won 3, allies 2.
- Traversal (user request, GASP-style; not the uproomgames full procedural wall climbing, which needs FinalIK):
  - Jump (Space) near an obstacle: `TraversalProbe.Scan` finds wall, top height, top depth, landing room,
    NavMesh at the end. Moving into a thin top (<= 1.3 m deep) vaults; standing still (or deep top) mantles
    on top. At a NavMesh edge with ground 0.5-2.6 m below, Jump drops down. Otherwise a normal jump.
  - `SoldierTraversal` runs the move: agent off, soldier moved start->end along the clip's Forward curve,
    model child lifted (`TraversalWarp`: ScaleToPeak for vaults, ScaleToEnd for real climb clips, AddRise
    for hops/drops), rifle slung on UpperChest, then agent.Warp(end) and CrossFade to Stand with a settle
    offset. Soldier skips motor/weapon while `IsActive`; SoldierAim skips grip/aim.
  - `TraversalAction` assets (Animations/Traversal) baked by **War Mission > Build Traversal Actions**
    (`Editor/TraversalActionBuilder.cs`: foot height + forward progress per clip; also rebuilds the Animator
    with one state per action and assigns them to Soldier.prefab). Stand-ins: all three use
    Rifle_Jump_Forward (vault 0.4-1.25 m, mantle 0.4-1.4 m, drop 0.5-2.6 m).
  - NavMesh: Humanoid agent radius 0.3 / height 1.8 (was 0.5 / 2.0), NavMeshSurface minRegionArea 0.2, so every
    box top is walkable. SquadBattle now has its own NavMesh asset (Scenes/SquadBattle/NavMesh.asset).
  - Verified in the editor (probe calls): vault/mantle/drop detection on all test boxes; in play: mantle onto a
    1.24 m box lands feet exactly on top, rifle slung and back in hand.
  - Real clips (user's "climb and jump" folder, no rifle in the clips; rifle is slung on the back):
    - Traverse_Vault_OneHand (Mixamo "Jumping - Male Jumping Over An Obstacle With 1 Hand Planted", 35 f,
      feet peak 1.15 m) = vault. Verified on the 1.24 m box: hand planted, clears, lands, rifle back in hands.
    - Traverse_Drop_Rail (Mixamo "Jumping Down - Over A Railing", 6 s, starts standing on a 1.12 m rail)
      = drop, PlayUntil 0.6, speed 1.4, ScaleToEnd with negative key. `TraversalAction.FootBase` cancels the
      clip's own start height (faded in with the start crossfade). Verified: lands, stands, agent back on NavMesh.
    - Traverse_Vault_Hide (Mixamo "Vault Over Box And Hide", ends crouched behind the box): kept for M5 cover.
    - Unused in _Unsorted: T_Jump_Over (dive roll 8.5 m), T_Jumping_1, T_Jumping (drifts 30°),
      T_Jumping_Over_Into_Combat. Action Adventure Pack.zip (cover clips) not imported yet: use for M5.
  - Sling: rifle diagonal across the back, muzzle up behind the right shoulder (`slingOffset`, `slingTilt`).
  - Still needed: a climb-onto clip (mantle still uses the rifle hop) and a high climb (1.4-2.3 m). Candidates:
    Mixamo "Climbing - Starting To Climb Onto A Box Or Short Fence", "Sprint To Wall Climb", "Climbing -
    Pulling Up To A Ledge". Add to `TraversalActionBuilder.Definitions` with ScaleToEnd.
  - Test traversal with `scratchpad`-style direct calls: warp the player, fake motor velocity, call
    `SoldierTraversal.TryBegin(dir)`, capture a fixed TmpCam at Time.timeScale 0.06.
- Realism pass (user video review 2026-09-23; reference: COD and UE5 GASP):
  - Rifle up ONLY on aim (right mouse / left trigger) or fire (+3 s after firing). Walking, running, crouching
    carry it low. `SoldierAim` ready = !sprinting && !obstructed && (Aim || Fire).
  - Animator layer `RelaxedArms` (mask `Controllers/Arms.mask`, arms + fingers only, clip Rifle_Idle_GunDown)
    between Base and UpperBody. `SoldierAnimator` weight = (1 - ReadyWeight) * speed factor, 0 when sprinting
    forward (that clip is already relaxed) and when standing (relaxed idles cover it).
  - Wall obstruction: sphere cast from UpperChest toward AimPoint (0.8 m); a wall keeps the rifle down and
    blocks firing (COD style). Low boxes do not trigger it when the chest is above them.
  - NavMesh: agent radius 0.35 (was 0.3, body clipped walls), step height 0.3 (was 0.75, the soldier popped
    onto 0.65-0.71 m boxes through their sides). Prefab agent radius 0.35. All tops walkable except Block_6.
  - Drops: Traverse_DropLow (hop down, 0.3-1.05 m) and Traverse_Drop (rail clip, 1.05-2.6 m). Walking into an
    edge of up to 1 m hops down on its own (`SoldierTraversal.TryAutoStepDown`, edge within 0.45 m).
    Mantle now from 0.3 m. Probe wall rays at 0.15/0.45/0.8/1.2/1.7 m.
  - Verified: relaxed walk (arms 1, muzzle -16°), aim walk (ready 1), obstruction at 0.6 m from a 2.1 m wall,
    blocked by a 0.65 m box, mantle onto it, auto hop-down off it, Space rail-drop off 1.68 m; squad battle OK.
  - The user plays while tests run; check `Soldier.Command` for foreign input before trusting key tests.
- Milestone 4: DONE implicitly (user moved on after the battle; "progress good").
- AI decision layer (user request 2026-11-01: "enemy can think and make decisions"), built, NOT compiled in
  Unity yet (cloud session, checked with mcs against API stubs only), waiting for playtest:
  - `AIPersonality`: per-soldier traits rolled in Awake (aggression, caution, discipline, teamwork; label
    Assault / Careful / Marksman / Rifleman). Discipline: faster reaction, tighter aim, shorter bursts.
  - `AIDecisionMaker` (utility AI): every ~0.3 s scores Engage, TakeCover, Advance, Flank, Suppress, Retreat,
    Search from an `AIContext` (health, ammo, target in sight, distance, time since seen, suppression, under fire,
    visible enemies vs nearby allies, squad morale, in cover, cooldowns) times the traits. Commitment bonus 0.12,
    min commit 1.2 s (skipped when health < 0.35 or suppression > 0.7), +/-0.05 noise.
  - `AISoldierBrain`: calm states unchanged (Patrol, Investigate on gunshot / squad report). Fight mode while an
    enemy was seen or shot at this soldier within `combatMemory` (10 s). New states Flank, Suppress, Retreat.
    Engage strafes (NavMesh.Raycast flips at edges) and careful soldiers kneel past 15 m. Suppress fires at the
    predicted last known position (also from cover peeks). Leaving the fight searches the last sighting once.
  - `AICover.TryTakeCover(threat, CoverSearch)`: Nearest / Advance (gain >= 3 m on the threat) / Fallback.
    `HideScale` / `PeekScale` set by the brain (suppressed soldiers stay down longer), `Duck()` cuts a peek.
  - `AIPerception`: `Suppression` 0..1 (near miss +0.2, hit +0.35, decays 0.3/s), `VisibleEnemyCount`,
    `LastSeenVelocity` + `PredictedTargetPosition`. Suppression also widens AI aim error.
  - `AITactics` (static): flank spots 75-125 deg around the enemy with line of sight and a NavMesh path <= 35 m;
    retreat spots away from the threat, preferring hidden ones.
  - `Squad`: member list, one flanker at a time (token), reload callouts (`SoldierWeapon.ReloadStarted`) and the
    flanker make mates score Suppress higher, morale (-1/size per death, +0.15 per kill, recovers to 0.8).
  - `AIDebugLabel` (F3): state, intent, top 3 scores, traits, HP, ammo, suppression, morale above each AI.
  - The brain adds AIPersonality / AIDecisionMaker / AIDebugLabel at runtime if the prefab lacks them; Build
    Soldier Prefab now adds them to Soldier_Ally and Soldier_Enemy.
- AI features pass 2 (user request 2026-11-01: "improve further, add more features"), NOT compiled in Unity yet
  (mcs + API stubs only), waiting for playtest:
  - Grenades for every soldier: `SoldierGrenades` (2 each, G / gamepad North for the player, HUD "G n"),
    `SoldierCommand.Grenade` + `GrenadeTarget`. Release after 0.55 s on a 35 deg ballistic arc (steeper when
    needed), max 28 m. `Grenade` (runtime primitive, no prefab): 3 s fuse, 6 m radius, 160 dmg falling to 10%,
    blocked by scenery, ragdoll force, orange blast ball, static `Live` list and `AnyExploded` event.
    Soldier blocks fire/reload while throwing; SoldierAim drops the aim pose (as when reloading).
    Animator: trigger `Throw`, UpperBody state `Throw` (Rifle_Grenade_Throw). Re-run Build Soldier Animator.
  - AI grenades: intent `Grenade` (score up when the enemy hides or crouches, bunches up, or the fight stalls
    behind cover; never within 7 m of a team mate, 8-28 m, 12 s cooldown). State `ThrowGrenade` (face, throw),
    or from cover on the next peek.
  - Grenade dodge: `AIPerception.DangerGrenade` (live grenade within radius + 1.5 m, any team) -> state `Evade`
    (runs to a hidden spot away from it, shouts "Grenade!"), before any other logic.
  - Gradual spotting: awareness 0..1 per enemy. Rate 2.5/s close, x0.25 at 40 m, x0.45 peripheral (> 25 deg),
    x0.55 crouched, x1.5 sprinting, x4 if it fired in the last 2 s, x5 while already fighting. Target only at 1.
    Suspicion (>= 0.3): patrol stops and looks ("Huh?"), investigate walks slowly toward it.
  - Footsteps: walking enemies heard within 6 m, sprinting 14 m, crouched silent (sets suspicion and LastHeard).
    Explosions heard within 60 m; a blast within 10 m counts as under fire and suppresses.
  - Target choice: threat score = distance x attacker 0.5, aiming at me 0.75, wounded 0.8, x1.25 per other AI
    already on that target (`TeamMember.TargetedBy`), keep current 0.85.
  - `DangerZones`: team death spots (30 s, 4 m) and live grenades raise cover costs in `AICover`.
  - `AICallouts` (F4 toggles): "Contact!", "Reloading!", "Flanking!", "Moving up!", "Falling back!",
    "Covering fire!", "Frag out!", "Grenade!", "Man down!" (closest mate), "Target down!", "Lost him...",
    "Taking cover!", "Huh?". 6 s repeat cooldown. `SpokeLine` event is the hook for voice clips.
  - Debug label (F3) now also shows grenade, suspicion. Build Soldier Prefab adds SoldierGrenades (base) and
    AICallouts (AI variants); runtime fallbacks add them on older prefabs.
- Milestone 5: built, waiting for user playtest.
  - `CoverPoint` (+ static registry): Low (crouch behind, stand to shoot over) or High (corner, step out
    `PeekSide` 0.9 m to shoot). Forward faces the cover; `Protects(threat)` within 65°. One occupant.
  - **War Mission > Generate Cover Points** (`Editor/CoverPointGenerator.cs`): boxes under "Environment",
    0.9-1.25 m = low points every 1.4 m along each side, >= 1.25 m = high points at side corners; 0.55 m from
    the face, must be on the NavMesh. 73 points in each scene (17 low, 56 high).
  - Suppression: `SoldierWeapon.AnyShotPath` -> `AIPerception` counts an enemy shot passing within 2.5 m of
    the chest (or a hit) as under fire (`LastUnderFireTime`, `LastThreatPosition`).
  - `AICover`: best free point that protects from the threat and has scenery between the hiding head and the
    threat; cost = distance + penalty for moving toward the threat. Phases Moving (sprint if > 3 m) -> Hiding
    (1-2.2 s, crouched on low) -> Peeking (1.4-2.6 s, stand / step out, shoot after 0.35 s). Releases on death.
  - `AISoldierBrain` state TakeCover: entered when under fire (1.5 s window, search every 1 s). Shoots only
    while peeking (aim error, bursts, friendly-fire check), reloads while hidden (< 1/3 mag). Flanked ->
    new cover or Engage; nothing seen/heard for 8 s -> Investigate.
  - Verified: squad battle, all 5 survivors took cover under fire; battle lasted 41 s (was 8 s).
  - Cover animations (Action Adventure Pack, extracted to Downloads/.../climb and jump/Action Adventure Pack):
    Cover_Idle_Low (idle 4, crouched back to wall), Cover_Idle_High (idle 5, standing back to wall),
    Cover_Sneak_{High,Low}{PlusX,MinusX} (sidesteps along the wall, ~1.13 / 1.0 m/s, "Sneak" loops),
    Cover_Enter_{High,Low}, Cover_Exit_{High,Low} (imported, not used yet). The clips turn the pose ~145°
    (back to wall) themselves, so the soldier's transform faces the cover.
  - `SoldierCommand.InCover / CoverLow / CoverForward / CoverAnchor`. Motor in cover: faces CoverForward,
    slides along the wall (Move projected on it), is pulled to the anchor's distance. Animator states
    CoverHigh / CoverLow (1D on MoveX, AnyState on InCover + CoverLow, back to Stand when !InCover).
    RelaxedArms weight 1 in cover (the clips have empty hands; rifle held low). Jump in cover vaults
    (`TryBegin(dir, preferVault: true)`).
  - AI: hiding phase uses the cover pose (and slides back to the point after a corner peek).
  - Player cover (`PlayerCover`, key Q / gamepad right shoulder): snaps to a wall in the move direction (or camera
    direction) within 1.4 m, 0.45 m off it; low if the wall is under 1.45 m (crouched), else standing. Slides
    with A/D and stops 0.25 m before the edge; aim/fire pops up / out and returns on release; Space vaults;
    Q again, pulling away or sprint leaves.
  - Verified: player low cover, slide to edge, pop-up aim and return, high cover, vault out of cover;
    AI hides in CoverHigh / CoverLow poses and steps out to shoot.
  - Not yet: enter/exit transition clips, HUD prompt for cover.
  - Tall drop cut at the landing squat (PlayUntil 0.285, `Definition.EndBlend` 0.4); camera follows the hips during
    traversal (`ThirdPersonCamera.pivotAboveHips`).
  - Right-side tall cover (user request): mirrored state `CoverHighMirror` (bool `CoverMirror`, from
    `SoldierCommand.CoverFaceLeft`); WeaponGrip moves the grip to the left palm while in it. Player side follows
    the last slide / peek direction; AI follows `CoverPoint.PeekSide`. Mirroring is allowed here only because the
    grip follows the swapped hands.
  - Testing note: the user often plays during automated tests; test detection with direct probe calls
    (eval) instead of simulated keys when possible, and check `Soldier.Command` for foreign input.
  - Verified: gun-down idle points 41° down; walk aim error 0.0°; left palm on the barrel (0 mm) in walk and
    all four sprint directions.
  - `TeamMember` + static `TeamRegistry` (pulled forward from M4; perception needs teams). Head/Chest bones exposed.
    Base `Soldier.prefab` = Enemy team (dummies). `Soldier_Player` = Ally + PlayerSoldierInput/PlayerHud/PlayerRestart.
    `Soldier_Enemy` = Enemy + AIPerception/AIPathFollower/AISoldierBrain. All built by **Build Soldier Prefab**.
  - `AIPerception`: 110° cone, 40 m, LOS raycast to head or chest, scan every 0.2 s; hears `SoldierWeapon.AnyFired`
    within 30 m; remembers who damaged it and prefers that attacker as target.
  - `AIPathFollower`: NavMesh.CalculatePath corners -> move direction. Deviation from spec: AI does not use
    `agent.SetDestination()`; the brain converts the path direction into `SoldierCommand.Move`
    (`SoldierCommand.WorldToMove`), so AI and player go through the same motor code.
  - `AISoldierBrain`: Patrol (PatrolRoute children, wait 1-2.5 s), Investigate (run if > 8 m, sweep view 4 s),
    Engage (reaction 0.3-0.6 s, fire only when facing within 20°, bursts 3-6 with 0.3-0.8 s pauses, Perlin aim
    error 1.2 m shrinking to 0.15 m over 2.5 s of sight), Dead. Loses target after 1.5 s -> investigate last seen.
  - Scene renamed `Scenes/TestRange.unity` (added to Build Settings): Enemy_A and Enemy_B with patrol routes.
  - Verified: Enemy_A patrols, spots the player at 29 m when its route turns toward them, faces them, fires,
    kills in 6 shots. `PlayerRestart`: Enter reloads the scene after death.
  - Menu **War Mission > Build Soldier Prefab** (`Editor/SoldierPrefabBuilder.cs`) rebuilds on `Soldier.prefab`:
    custom ragdoll (11 bodies, kinematic hitboxes while alive, head `HitZone` x3), placeholder rifle
    (primitives, placed from the Rifle_AimIdle pose: grip at right palm, barrel through left palm),
    left hand Two Bone IK (Animation Rigging) + elbow hint, muzzle flash / light / tracer, and wiring.
    Unity's RagdollBuilder is not reachable by reflection in 6.6, so the ragdoll is our own.
  - Aim: `SoldierAim` rotates Spine/Chest/UpperChest in LateUpdate so the muzzle points at AimPoint
    (not Multi-Aim: the Mixamo rifle stance holds the chest ~45° off the barrel). Plus recoil kick.
  - `SoldierWeapon`: hitscan from the muzzle to AimPoint, 600 RPM, 25 dmg, 30 rounds, 2.4 s reload,
    spread from movement + sustained fire (smaller crouched). `WeaponEffects`, `Health`, `HitZone`,
    `SoldierRagdoll` (drops the rifle), `PlayerHud` (IMGUI debug HUD), `DummyRespawner` (test helper).
  - Animator layer `UpperBody` (mask `Controllers/UpperBody.mask`): Empty / Fire / Reload / Hit.
  - Scene (now TestRange) has 3 dummies (Soldier.prefab, no input) with respawn.
  - Verified: aim error 0.0°, left hand on grip (0 gap), kills + ragdoll + respawn, reload cycle.
  - Scripts: `Soldier`, `SoldierMotor`, `SoldierAnimator`, `SoldierCommand`, `PlayerSoldierInput`, `ThirdPersonCamera`.
  - Animator: built by menu **War Mission > Build Soldier Animator** (`Editor/SoldierAnimatorBuilder.cs`). Re-run after adding clips.
    Crouch idle is generated (`Animations/Generated/Rifle_CrouchIdle.anim`) from frame 0 of `Rifle_CrouchToStand` (a kneel).
  - Prefabs: `Prefabs/Soldier.prefab` (body), `Prefabs/Soldier_Player.prefab` (variant + PlayerSoldierInput).
  - Scene: `Scenes/M1_Locomotion.unity` (renamed to `TestRange.unity` in M3). NavMeshSurface uses Build Height Mesh (without it feet float 8 cm).
  - Verified in play mode: walk right 0.72, run forward 2.85, back-left diagonal, crouch toggle, feet on ground.
  - Playtest 1 feedback (user): crouch left/right/back looked like moving in place; walk too slow; idle fine.
    - Walk now plays at 1.4x (`SoldierClipSpeeds.WalkPlayback`): walk forward 1.33 m/s.
    - Generated crouch clips were tried and rejected (feet high, strides too long). Replaced by the user's
      Mixamo "crouch sets" folder: CrouchWalk Left, Right, ForwardLeft (ForwardRight mirrored), BackLeft,
      BackRight. Straight back blends BackLeft + BackRight (no straight-back clip).
    - Crouch moves at 1.0 m/s in all directions (0.7 straight back). Each crouch clip's playback is scaled
      so it covers that ground (`AddCrouchChild`). User playtest: crouch "better, beautiful".
  - Crouch extras imported, not used yet: Rifle_CrouchRun_Forward, Rifle_CrouchTurn90_Left/Right.
  - Unity crashed once during a large recursive ForceUpdate reimport of `Mixamo/`. Import clips one at a
    time (`ImportAsset` per file) instead. After the crash the project dropped out of the Hub list;
    re-added with `unity projects add`.
  - All clip speeds live in `Scripts/Soldier/SoldierClipSpeeds.cs` (motor defaults + blend tree positions).
    After changing it: rebuild the Animator and reset the `SoldierMotor` values on `Soldier.prefab`.
  - Known placeholders: no turn-in-place animation; no rifle model.

## Testing from the CLI

- Play mode does not tick while the Editor is unfocused. After `unity command editor_play`, run
  `eval 'UnityEngine.Application.runInBackground = true;'` (runtime only, not saved).
- Simulate keys: `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.LeftShift))`; pass no keys to release.
- `capture_game_view --save_path` only writes under `Assets/`; delete the file afterwards.
- Short actions (jumps) finish between evals. Set `Time.timeScale = 0.03f` in the same eval as runInBackground.
- Do not test with the Editor paused + `EditorApplication.Step()`: InputActions do not update in that mode.
- Ask the user not to press keys during automated tests; their input mixes with simulated keys.
