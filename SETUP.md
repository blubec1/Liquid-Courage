# Drunken Bar Fight - MVP build notes

I can't launch the s&box editor from where I run, so none of this has been compiled or
playtested by me. Everything below is written against s&box's current documented APIs, but you
are the first person to actually press Play. Expect to spend a little time here before it's
smooth - that's normal for a fresh engine drop, not a sign anything is fundamentally wrong.

## How to open it

1. Open the project in s&box.
2. Open `Assets/scenes/bar.scene` (not `minimal.scene`).
3. Press Play.

`bar.scene` is hand-written (not built in the editor), so if it fails to load or looks wrong,
see **"If the scene doesn't load"** below - the C# code underneath is unaffected either way and
you can rebuild the scene by hand quickly using that section as a checklist.

## What's implemented

Every system from the spec, organized under `Code/`:

- `Combat/` - Punch/Kick/Heavy data (`AttackDefinition`), 4 finishers (`FinisherDefinition`),
  attack-string recognition, distance/angle-based hit detection (no physics traces - see below),
  hit feedback (screenshake + per-enemy freeze-frame).
- `Combo/` - the combo meter: variety-weighted gain, decay, idle drain, damage penalty.
- `Drunkenness/` - hidden value, threshold-triggered increases, Last Call, power scaling,
  overdrunk = instant loss.
- `Scoring/` - the multiplier formula (combo x drunkenness x style) and the style bonus system.
- `Player/` - movement, mouse-aim facing, HP, the attack/finisher input loop, and the procedural
  attack "animation" (see below).
- `Enemies/` - Brawler, Bruiser (telegraphed heavy attack), Sobering Bartender.
- `Horde/` - ramping spawner + milestone announcements.
- `Pickups/`, `Environment/` - sobering pickups, throwable bottles.
- `Camera/` - fixed isometric camera, follow + screenshake.
- `UI/` - `Hud.razor` (HP/combo/score/multiplier/cooldown-under-crosshair/finisher reference/
  drunkenness vignette/milestone toasts) and `DeathScreen.razor` (stats + leaderboard + restart).
- `Leaderboard/` - `LeaderboardService.cs`, isolated from everything else.
- `GameState/` - `GameManager` (run state, restart), `GameEvents` (the event bus everything else
  talks through instead of holding direct references to each other).

## Things you'll need to do

**1. Register the leaderboard.** Your project is currently org `local` (see
`drunken_bar_fight.sbproj`), which isn't published, so there's no real leaderboard to submit to
yet. `Code/Leaderboard/LeaderboardService.cs` has a `GameIdent` constant at the top - once you
publish under a real org, update that constant to match and the Stats/Leaderboards calls should
start working as-is. Until then, the death screen will just show "Unavailable" for rank.

**2. There's no custom art or audio.** Player and all enemies use the stock Citizen model
(tinted/scaled per archetype instead of separate models - Brawler reddish, Bruiser dark red and
1.35x scale, Sobering Bartender gold). There are no punch/kick/heavy animation clips by default, so
attacks are a procedural lunge + squash/stretch on the model (see `PlayerAnimationDriver.cs`) -
see "Adding real attack animations" below for how to swap in real clips. Sound calls
(`HitFeedback.cs`) point at placeholder paths like `sounds/combat/heavy_impact.sound` that don't
exist yet - they'll just silently no-op until you add real sound assets at those paths (or repoint
the strings).

**3. Eyeball the arena scale.** I sized the floor/walls in `bar.scene` from ratios inferred off
the default template's dev models, not from measuring `plane.vmdl`/`box.vmdl` myself. Collision
(`BoxCollider` sizes) is authoritative and should be correct either way, but the visual mesh
might not line up perfectly with the walls/floor at a glance - a two-minute nudge in the editor
if so.

**4. Melee hit detection is math, not physics.** `HitDetector.cs` checks distance + angle from
the attacker against a registry of live enemies (`EnemyBase.All`), rather than using
`Scene.Trace`. This was a deliberate choice so I wasn't guessing at trace-query syntax I couldn't
test - it's simple, fast, and easy to tune, but it means attacks ignore line-of-sight (an enemy
directly behind a wall from you but within range/arc would still get hit). Given the arena is one
compact open room, that shouldn't come up in practice.

## Adding real attack animations (Mixamo)

Mixamo animations use a different skeleton than Citizen, so they need retargeting before they'll
play on the player/enemies. Two steps:

1. **Get the clip onto the Citizen skeleton.** Export the FBX from Mixamo, then bring it into
   s&box. The stock editor's Model Importer can do this, or the community "s&box Model Importer"
   tool (sboxcool.com) auto-detects Mixamo rigs and maps them onto Citizen if the built-in
   workflow gives you trouble. Either way, you end up with a sequence name on a Citizen-compatible
   model/animation asset - that name is what the code below needs.

2. **Wire the clip name into the attack.** Open `Code/Combat/AttackDefinition.cs` (or
   `FinisherDefinition.cs`) and set the `Animation` field on the relevant entry in `AttackLibrary`/
   `FinisherLibrary`, e.g. `Animation = "punch_01"`. Leave `AnimationDuration` at 0 to let it use
   the existing timing (based on `Recovery`/`StaggerTime`), or set it explicitly once you know the
   clip's real length.

That's it from a data standpoint - `PlayerAnimationDriver.PlayAttackSwing`/`PlayFinisher` already
check for `Animation` and try to play it via `TryPlaySequence`, falling back to the procedural
lunge automatically if `Animation` is empty or if playback fails for any reason.

**Important caveat:** playing a one-shot sequence on top of the Citizen anim graph from code is a
known rough edge in s&box - there wasn't a simple first-class "just play this clip" API at the
time this was written, so `TryPlaySequence` uses the current community workaround (toggling
`SceneModel.UseAnimGraph` off and setting `CurrentSequence.Name` directly, then reverting after
the attack's duration). This is wrapped in a try/catch and silently falls back to the procedural
version if it doesn't work, so a bad clip name or an API mismatch won't break combat - but expect
to iterate on this once you're testing against real clips, and tell me what actually happens
(does it play but not blend back into locomotion cleanly? not play at all? wrong clip?) so I can
adjust `TryPlaySequence`/`DriveSequenceRevert` in `PlayerAnimationDriver.cs` to match what your
version of the engine actually does.

Enemies (`EnemyBase.cs`) don't have an animation driver yet - only the player does. If you want
enemy attacks animated too, that's a small extension of the same pattern (they already have an
`AnimHelper` reference for locomotion; giving them a `TryPlaySequence`-style hook for their own
`ResolveAttack` would look identical to the player's).

## Crosshair alignment fix

Fixed: the crosshair was using raw `Mouse.Position` (real screen pixels) as panel-space CSS px,
but `ScreenPanel` auto-scales its internal coordinate space to a fixed virtual height, so the two
only lined up if your window happened to be exactly that height. It now converts the mouse
position to a 0-1 fraction of `Screen.Width`/`Screen.Height` and positions the crosshair with `%`
instead of `px` - since `.hud-root` is always 100% of the real screen regardless of scale, this
should track the real cursor exactly now. Let me know if it's still off.

## Still investigating: attacks not registering / combo not moving

I've re-read the whole input -> attack -> combo chain (`PlayerCombat.cs`, `ComboSystem.cs`,
`AttackStringSystem.cs`, `HitDetector.cs`, the scene file, `Input.config`) and structurally it all
looks correct - the components are all attached to the Player in `bar.scene`, the bindings exist,
none of the early-return guards should be blocking a fresh run. I can't launch the editor myself to
reproduce this, so rather than guess again I've added temporary diagnostic logging to
`PlayerCombat.cs` (look for `[PlayerCombat]` lines in the console):

- One line prints the moment it sees *any* Punch/Kick/Heavy input, before any of our own logic runs.
- Another prints at the end of a successful attack, after combo is added.

Next time you test, please attack a few times and paste back what shows up in the console:

- **Nothing at all prints** - the click isn't reaching `Input.Pressed` at all. This points at
  something outside our code, most likely input focus: s&box has a known editor quirk where the
  Game view doesn't grab mouse capture correctly in some cases. Try clicking once directly inside
  the Game view (without holding a button while your cursor enters it) before attacking, and see if
  that changes anything.
- **The "input fired" line prints but "completed" never does** - one of the guards is blocking it or
  an exception is being thrown in between; the printed `IsReady`/`IsDead`/`State` values will show
  which, and any red exception line right after it is the smoking gun.
- **Both lines print but nothing happens on screen** - then the bug is purely visual (HUD not
  reflecting state), which the `BuildHash()` fix should have already resolved - let me know if this
  is the case, that's a different problem than input.

Once I know which of these it is I can fix it directly instead of guessing further. Remove the two
`Log.Info` calls in `PlayerCombat.cs` once this is confirmed working.

## If the scene doesn't load

If `bar.scene` fails to open cleanly, build it by hand - all the gameplay code works the same
regardless of how the scene was assembled:

1. Floor (plane) + 4 walls (boxes) forming a room, all with `BoxCollider`, no `Rigidbody`.
2. A `Player` GameObject with `CharacterController` + these components (all under `DrunkenBarFight`
   namespace): `PlayerMovement`, `PlayerFacing`, `PlayerStats`, `PlayerCombat`,
   `AttackStringSystem`, `ComboSystem`, `DrunkennessSystem`, `ScoreSystem`, `StyleSystem`,
   `PlayerAnimationDriver`. Give it a child GameObject named exactly `ModelPivot` holding a
   `SkinnedModelRenderer` (`models/citizen/citizen.vmdl`) + `CitizenAnimationHelper` - the code
   finds this child by name automatically, no manual wiring needed.
3. A `Camera` GameObject: `CameraComponent` (Orthographic on) + `IsoCameraRig`. Rotate it to
   whatever isometric angle looks good - the rig only ever translates it, never rotates it.
4. One GameObject with `GameManager`, `MilestoneSystem`, `HordeSpawner`, `PickupSpawner` on it.
5. Two GameObjects for UI, each with a `ScreenPanel` + one of `Hud` / `DeathScreen`.
6. Optional: a few GameObjects with `ThrowableBottle` + a child named `Visual` holding a
   `ModelRenderer`, scattered around the room.

Enemies and pickups are never placed by hand - `HordeSpawner`/`PickupSpawner` create them
entirely from code at runtime, so there's nothing to prefab.

## Tuning

Nearly every number (damage, cooldowns, combo decay, drunkenness thresholds, spawn rates, score
weights) is a `[Property]` on its component, editable in the inspector without touching code.
Starting values are my best judgment for "fun in the first 60 seconds" per the spec's priorities
- expect to retune spawn rate, drunkenness gain-per-threshold, and finisher frequency the most
once you can actually feel them.

## Input

Added three new bindings to `ProjectSettings/Input.config`: `Punch` (mouse1), `Kick` (mouse2),
`Heavy` (F). `Use` (E) is environmental-weapon interact (walk up to a bottle, press E to throw it
in your facing direction).
