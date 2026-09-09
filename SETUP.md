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

## Major redesign: controls, combo, and the drinking loop

This was a big rework of the core loop per your latest direction. Summary of what changed:

**Controls:** LMB = light attack (Punch), RMB = Heavy attack, **E** = leg sweep Kick (a 90 degree
arc - 45 left/45 right of your facing, already what `Kick` was tuned to, just moved off mouse2).
Bottle throw/interact moved off E onto **G** since E was taken. `Input.config` updated accordingly
- this is also where the earlier bootstrap-breaking bug lived (an invalid `GamepadCode` value on
Heavy's old binding), which is now fixed as part of this same rebind.

**Combo is now a pure hit-connect streak.** The old variety-weighted meter is gone
(`ComboSystem.cs` rewritten). Every attack that actually lands on an enemy adds to `HitStreak`;
whiffing does nothing. If you go `GraceTime` (2.2s by default) without landing a hit, the streak
breaks. The HUD's combo bar (now small, unlabeled, moved under the multiplier text) shows that
grace window draining - not the streak count - so its whole job is to visualize "your combo is
about to die," with a quick pop animation and a color shift (green -> yellow -> orange -> red as
the streak climbs) every time you land a hit.

**Drunkenness is now driven entirely by a new "drink meter."** `DrinkMeter.cs` is a new component
on the Player that fills as you kill enemies (never decreases on its own) - killing a Bruiser fills
it faster. When it hits max, every live enemy freezes for a beat, the player plays a "chug"
animation (procedural head-tilt-back fallback, or a real clip if you wire one into
`DrinkMeter.DrinkAnimation` the same way attacks work), and `DrunkennessSystem.DrinkGlass()` adds a
flat 10 drunkenness. Max drunkenness is 150. It's the *only* way drunkenness goes up now - combo no
longer feeds it at all. It decays on its own, 1 point every 15 seconds. There's still no numeric
drunkenness display - it's entirely conveyed through VFX (see below) - but the death screen now
shows "highest drunkenness reached / 150" instead of a percentage.

**Power scaling (the "drunk but powerful" hook):** as drunkenness climbs from 0 to 150, damage you
deal scales up to +100%, damage you take scales down to -50%, move speed up to +30%, attack
cooldown down to -30% - all tunable on `DrunkennessSystem`. Aiming gets slightly heavier
(`PlayerFacing`'s effective turn speed drops up to 35%) and movement gets a sine-wave "stumble"
wobble (`PlayerMovement`) that both scale with how drunk you are, so higher-risk play visibly costs
you precision even as it makes you stronger - that tension is the whole point.

**Blackout warning:** above 130 drunkenness (`LastCallThreshold`), the screen edges progressively
darken into a tunnel-vision "blackout" overlay that intensifies the closer you get to 150. If a
drink pushes you *past* 150 - which can only happen if you're already close and get greedy - it's
instant game over ("BLACKED OUT AT THE BAR" on the death screen), same as before, just retuned.

**VFX added to sell "drunk":** the existing red vignette now shifts color (black -> warm red as you
drink more) and gets a very subtle full-screen sway (a slow sine-wave rotate/drift), on top of the
new blackout tunnel-vision above. A drink event gets its own flourish: a quick white screen flash
and a center-screen "CHEERS! - GLASS #N" toast.

**Enemies get tougher over time, independent of the spawn-rate ramp.** `EnemyBase.ApplyTimeScaling`
now runs on every spawn, scaling that enemy's HP/damage up based on how long the run has gone
(`HpRampPerSecond`/`DamageRampPerSecond`, both tunable per-archetype) - stacks with the existing
spawn-interval/archetype-mix ramp in `HordeSpawner`.

**New top-center survival timer** (MM:SS) on the HUD, bound to `GameManager.SurvivalTime`.

### More ideas for the "drunk but powerful" feel, if you want to keep going

These weren't implemented (either out of MVP scope, or because they'd need an API I can't verify
without the editor) but are worth considering once the above is playtested:

- **Screen-space chromatic aberration / double-vision ghosting** past ~60% drunk - a cheap version
  is possible in pure CSS (a second blurred, offset, low-opacity copy of the vignette), a real
  version would want a post-process shader.
- **Camera "breathing"** - a slow sine pulse on `IsoCameraRig`'s orthographic size/offset, scaled by
  drunkenness, for a woozy zoom-in-zoom-out feel.
- **Player model flush** - tint the Citizen model progressively redder as drunkenness climbs
  (`SkinnedModelRenderer.Tint`), same technique already used for enemy archetype coloring.
  Not wired up here since it needs a body renderer reference on the player - easy add if wanted.
- **Audio low-pass filter** growing with drunkenness, once you have real sound assets - muffled,
  underwater-ish hearing is a very strong "drunk" cue and s&box's audio mixer likely supports it.
- **A "stumble" animation** distinct from the drink chug - a brief stagger clip when drunkenness
  crosses certain thresholds, rather than only the continuous movement wobble.
- **Glass-count HUD flourish** - little pint-glass icons filling up next to the drink bar instead of
  a plain bar, one per glass, for more visual flavor than a progress bar alone.
- **Slow-motion on overdrunk death** - a brief real-time slowdown (careful with `Scene.TimeScale`,
  same caution as always) right as you black out, before cutting to the death screen.

## Crosshair alignment fix

Fixed: the crosshair was using raw `Mouse.Position` (real screen pixels) as panel-space CSS px,
but `ScreenPanel` auto-scales its internal coordinate space to a fixed virtual height, so the two
only lined up if your window happened to be exactly that height. It now converts the mouse
position to a 0-1 fraction of `Screen.Width`/`Screen.Height` and positions the crosshair with `%`
instead of `px` - since `.hud-root` is always 100% of the real screen regardless of scale, this
should track the real cursor exactly now. Let me know if it's still off.

## Resolved: attacks not registering / combo not moving

This turned out to trace back to the same root cause as the bootstrap error: `Input.config` had an
invalid `GamepadCode` value ("RightBumper" isn't a real enum member - `SwitchRightBumper` is),
which made the whole project fail to load in the engine, not just fail to compile. Once that's
fixed the game boots normally, so the earlier "can't attack" reports were very likely just the game
never successfully starting up rather than a combat-logic bug - the input/combo code itself checked
out structurally the whole time. The temporary diagnostic logging that was added to `PlayerCombat.cs`
while chasing this has been removed now that combat's been rewritten for the new control scheme
(see above) - if attacks still don't register after all of this, let me know and I'll add fresh
logging targeted at whatever's actually happening.

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

Current bindings in `ProjectSettings/Input.config`: `Punch`/light attack (mouse1/LMB),
`Heavy`/heavy attack (mouse2/RMB), `Kick`/leg sweep - a 90 degree arc, 45 left/45 right (E).
`Use` (environmental-weapon interact - walk up to a bottle and press it to throw in your facing
direction) is now on **G** since E was taken over by Kick.
