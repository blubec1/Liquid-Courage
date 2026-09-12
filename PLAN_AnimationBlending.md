# Bone-Level Override Blending for Attack Animations

## Problem

The original `TryPlaySequenceFull` disabled the entire animgraph (`UseAnimGraph = false`) to play attack animations. This froze the legs mid-stride — the whole skeleton was replaced by the attack clip, including the lower body that should keep walking.

## Solution

Keep the animgraph running for locomotion (legs, hips, feet). Sample attack sequences on a hidden secondary `SceneModel`, then override only upper-body bones each frame, blending between the animgraph pose and the attack pose.

## Architecture

### Phase 1: Setup (`OnAwake` -> `SetupBoneBlending`)

Two things happen at startup:

1. **Resolve upper-body bones** — walks the model's bone collection, finds the 14 bones from `spine_0` up through `hand_R`. These are the only bones we override. Everything below `spine_0` (pelvis, legs, feet) stays fully animgraph-driven.

2. **Create a hidden sample model** — a second `SceneModel` using the same model/mesh as the real body, placed at `(0, 0, -10000)` (offscreen). Its `UseAnimGraph = false` so it plays raw sequences directly without any locomotion graph. This model exists purely as a data source — we never render it.

### Phase 2: Attack trigger (`PlayAttackSwing` -> `TryPlaySequenceBlended`)

When the player attacks:

1. The attack clip name is assigned to the sample model's `CurrentSequence.Name`
2. Sequence time is reset to 0
3. `_attackSequenceActive = true` — makes `DriveAttackBoneOverrides` run every frame
4. `_attackSequenceRevertTime` is set — when this expires, the blend ends

The real body's animgraph is **never touched**. It keeps running the locomotion graph the entire time.

### Phase 3: Per-frame blend (`DriveAttackBoneOverrides`)

Called from `OnUpdate` every frame while `_attackSequenceActive` is true.

**Step 1 — Advance the sample model's animation:**
```csharp
_sequenceTime += Time.Delta;
_sampleModel.CurrentSequence.Time = _sequenceTime;
_sampleModel.Update( 0f );  // Flush! Without this, GetBoneWorldTransform returns
                            // stale bind-pose data because the engine hasn't
                            // recomputed the sample model's bones yet.
```

**Step 2 — Compute blend weight (fade-in/fade-out curve):**
```csharp
fadeIn  = _sequenceTime / BlendInDuration   // clamped 0-1
fadeOut = remaining / BlendOutDuration      // clamped 0-1
weight  = min(fadeIn, fadeOut)
```
This creates a trapezoid: ramp up over `BlendInDuration` (0.08s), hold at 1.0, ramp down over `BlendOutDuration` (0.12s) as the attack ends.

```
Weight
1.0 |        ████████████████
    |       /                 \
    |      /                   \
    |     /                     \
0.0 |████                         ████
    └───────────────────────────────── Time
    |<-- fade in (0.08s) -->|<-- fade out (0.12s) -->|
```

**Step 3 — Sync sample model position:**
```csharp
_sampleModel.RenderingEnabled = false;  // Hide the mesh — only here for bone data
_sampleModel.Transform = _bodyRenderer.SceneModel.Transform;  // Same world position
```
Both models must be at the same world position so their `GetBoneWorldTransform` values are directly comparable. `RenderingEnabled = false` only hides the mesh — it does NOT suppress bone updates.

**Step 4 — Per-bone blend loop:**

For each of the 14 upper-body bones:

```csharp
// a) Read the animgraph pose (world-space):
//    TryGetBoneTransformAnimation wraps GetWorldSpaceAnimationTransform — returns the
//    animation-only output, BEFORE physics/procedural bones and BEFORE any SetBoneOverride
//    effects. Critical: using TryGetBoneTransformLocal here causes a feedback loop because
//    GetBoneLocalTransform returns the FINAL bone state including previous overrides.
if ( !_bodyRenderer.TryGetBoneTransformAnimation( bone, out var animWorldTx ) )
    continue;

// b) Read the attack pose (world-space):
//    Both values are now world-space at the same world position.
var attackWorldTx = _sampleModel.GetBoneWorldTransform( bone.Index );

// c) Lerp in world-space:
var blendedWorld = animWorldTx.LerpTo( attackWorldTx, _currentBlendWeight );

// d) Convert to model-local and write:
//    SetBoneOverride (called by SetBoneTransform) expects model-local coordinates —
//    the bone's position relative to the SceneModel root. Convert via ToLocal.
var blendedLocal = modelWorldTx.ToLocal( blendedWorld );
_bodyRenderer.SetBoneTransform( bone, blendedLocal );
```

### Phase 4: Cleanup (attack ends or throws)

```csharp
_attackSequenceActive = false;
_currentBlendWeight = 0f;
_sampleModel.Transform = new Transform( new Vector3( 0, 0, -10000 ) );  // Back offscreen
_bodyRenderer.ClearPhysicsBones();  // Clears all SetBoneOverride overrides,
                                    // animgraph resumes full control
```

## s&box Bone API — Coordinate Spaces

| Method | Space | Use |
|--------|-------|-----|
| `GetWorldSpaceAnimationTransform` | World | Read animgraph output (before overrides) |
| `GetBoneWorldTransform` | World | Read final bone state |
| `GetBoneLocalTransform` | Model-local | Read bone relative to SceneModel root |
| `GetParentSpaceBone` | Parent | Read bone relative to parent bone |
| `SetBoneOverride` | Model-local | **Persistent** override until `ClearBoneOverrides` |
| `SetBoneWorldTransform` | World | **One-shot** override (overwritten by animgraph next frame) |
| `Transform.ToLocal(child)` | — | Converts child from world to caller's local space |

Key distinction: `SetBoneOverride` persists across frames. `SetBoneWorldTransform` is immediately overwritten by the animgraph on the next frame. Always use `SetBoneOverride` (via `SetBoneTransform`) for blending.

Key distinction: `TryGetBoneTransformAnimation` reads the **animation-only** output (before overrides). `TryGetBoneTransformLocal` reads the **final** bone state (including previous overrides). Using the latter creates a feedback loop.

## Bugs We Hit (and why each iteration failed)

### Bug 1: Narnia teleport (original code)

```csharp
var attackTx = _sampleModel.GetBoneWorldTransform( bone.Index );  // at z=-10000!
var blended = animTx.LerpTo( attackTx, _currentBlendWeight );
_bodyRenderer.SetBoneTransform( bone, blended );
```

`GetBoneWorldTransform` returns absolute world-space positions. The sample model sits at `(0, 0, -10000)`, so every bone's world-space position includes that -10000 Z offset. The lerp blended from the real body's position (e.g. `z=36`) toward `z=-9964`. The bone teleported to Narnia.

### Bug 2: Feedback loop (switch to `TryGetBoneTransformLocal`)

```csharp
_bodyRenderer.TryGetBoneTransformLocal( bone, out var animTx )   // includes previous overrides!
var attackTx = _sampleModel.GetBoneLocalTransform( bone.Index );
var blended = animTx.LerpTo( attackTx, _currentBlendWeight );
_bodyRenderer.SetBoneTransform( bone, blended );
```

`TryGetBoneTransformLocal` wraps `GetBoneLocalTransform`, which returns the **current final bone state** — including any `SetBoneOverride` we applied last frame. The pipeline became:

- Frame 0: read animgraph pose -> blend toward attack -> write override A0
- Frame 1: read bone state -> this is A0 (our override!), NOT the animgraph -> blend again -> write A1
- Frame 2: read bone state -> this is A1 -> blend again -> write A2

Each frame, the "animgraph" input was actually the previous frame's blended result. The attack contribution compounded exponentially. Instead of a smooth 8% fade-in, the blend raced to full attack pose in a few frames.

Additionally, `GetBoneLocalTransform` returns parent-space (per `GetBoneTransforms` doc: "parent space" when `world=false`), while `SetBoneOverride` expects model-local. So there was also a **coordinate space mismatch**.

### Bug 3: Stale sample model data (`ToLocal` conversion, missing `Update`)

```csharp
_sampleModel.CurrentSequence.Time = _sequenceTime;
// _sampleModel.Update( 0f ) was MISSING
// ...
var attackWorldTx = _sampleModel.GetBoneWorldTransform( bone.Index );  // returns bind pose!
```

We set the sequence time but never called `_sampleModel.Update(0f)`. The s&box engine doesn't recompute a SceneModel's bone transforms until `Update()` is called. So `GetBoneWorldTransform` returned **bind-pose data** — the default T-pose, not the attack animation at the current time. The lerp blended the real body's animgraph pose toward a T-pose, producing distorted mush.

### Bug 4: One-shot override (`SetBoneWorldTransform`)

```csharp
_bodyRenderer.SceneModel.SetBoneWorldTransform( bone.Index, blendedWorld );
```

`SetBoneWorldTransform` is a one-shot — it sets the bone's world transform for that frame only. The animgraph runs again on the next frame and **overwrites** it completely. The override never persists. The body renderer's bones stayed in their animgraph pose the whole time.

In contrast, `SetBoneOverride` (via `SetBoneTransform`) **persists** across frames until `ClearBoneOverrides` / `ClearPhysicsBones` is called.

### Bug 5: Visible duplicate citizen (removed `RenderingEnabled = false`)

We removed `RenderingEnabled = false` theorizing it might suppress bone updates. Without it, the sample model (now synced to the player's position) rendered a second citizen model on top of the real one.

Testing proved `RenderingEnabled = false` does NOT suppress bone updates — it only controls mesh visibility. The bone computation runs regardless.

## What Finally Works

```csharp
_sampleModel.CurrentSequence.Time = _sequenceTime;
_sampleModel.Update( 0f );                                    // Fresh bone data
_sampleModel.RenderingEnabled = false;                        // Hidden, bones still update
_sampleModel.Transform = _bodyRenderer.SceneModel.Transform;  // Same world position

var animWorldTx = ...;    // TryGetBoneTransformAnimation — animation-only, no feedback
var attackWorldTx = ...;  // GetBoneWorldTransform — world-space, same position
var blendedWorld = animWorldTx.LerpTo( attackWorldTx, weight );
var blendedLocal = modelWorldTx.ToLocal( blendedWorld );      // World -> model-local
_bodyRenderer.SetBoneTransform( bone, blendedLocal );         // Persistent override
```

Five fixes stacked together:
1. `Update(0f)` — fresh bone data from sample model
2. `RenderingEnabled = false` — hidden but still updating
3. `TryGetBoneTransformAnimation` — pure animation output, no feedback loop
4. Position sync — world-space values are comparable
5. `SetBoneTransform` / `SetBoneOverride` — persistent override that survives animgraph ticks

## Files

- `Code/Player/PlayerAnimationDriver.cs` — the only file with bone override logic

## Citizen Rig Bone Reference

| Region | Bone Names |
|--------|-----------|
| Hips | `pelvis` |
| Spine | `spine_0`, `spine_1`, `spine_2`, `spine_3` |
| Neck/Head | `neck_0`, `head` |
| Clavicles | `clavicle_L`, `clavicle_R` |
| Upper Arms | `arm_upper_L`, `arm_upper_R` |
| Lower Arms | `arm_lower_L`, `arm_lower_R` |
| Hands | `hand_L`, `hand_R` |
| Upper Legs | `leg_upper_L`, `leg_upper_R` |
| Lower Legs | `leg_lower_L`, `leg_lower_R` |
| Feet | `ankle_L`, `ankle_R` |
| Toes | `ball_L`, `ball_R` |

**Override set** (upper body): `spine_0` through `hand_R` — everything from spine up.
**Animgraph-driven** (lower body): `pelvis`, `leg_upper_*`, `leg_lower_*`, `ankle_*`, `ball_*` — walking continues.

## Edge Cases Handled

- **Sample model creation fails** — falls back to procedural lunge (existing behavior)
- **Bone not found on model** — filtered out during `OnAwake` resolution, skipped at runtime
- **`TryGetBoneTransformAnimation` returns false** — skip that bone this frame
- **Quick successive attacks** — sample model just switches sequence, blend weight resets
- **Sequence name not found** — caught by try/catch, returns false

## Testing Approach

1. Attack while standing still — should play attack animation on upper body
2. Attack while walking — legs should continue walking, upper body plays attack
3. Attack while running — same blending behavior
4. Rapid successive attacks — no popping or visual glitches
5. Check that the procedural lunge still works as fallback when no animation name is set
