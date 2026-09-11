# Plan: Bone-Level Override Blending for Attack Animations

## Problem

`TryPlaySequence` in `PlayerAnimationDriver.cs` sets `UseAnimGraph = false` which completely disables the citizen locomotion animgraph. The attack animation replaces the entire skeleton — legs stop walking. No blending occurs.

## Solution

Keep the animgraph running for locomotion. Sample attack sequences on a hidden secondary `SceneModel`, then override upper-body bones each frame using `SetBoneTransform`, blending between the animgraph pose and the attack pose.

## Files to Modify

- `Code/Player/PlayerAnimationDriver.cs` — the only file that needs changes

## Implementation Steps

### Step 1: Add new fields

```csharp
SceneModel _sampleModel;           // Hidden model for sampling attack sequences
float _sequenceTime;               // Elapsed time in the current sequence
float _blendInDuration = 0.08f;   // Seconds to blend from 0 to 1
float _blendOutDuration = 0.12f;  // Seconds to blend from 1 to 0
float _currentBlendWeight;        // Current 0-1 blend weight

// Upper-body bone names to override (Citizen rig names)
static readonly string[] UpperBodyBones = new[]
{
    "spine_0", "spine_1", "spine_2", "spine_3",
    "neck_0", "head",
    "clavicle_L", "clavicle_R",
    "arm_upper_L", "arm_upper_R",
    "arm_lower_L", "arm_lower_R",
    "hand_L", "hand_R",
};
BoneCollection.Bone[] _overrideBones;  // Resolved at Awake
```

### Step 2: Modify OnAwake — resolve bones and create sample model

After the existing `_bodyRenderer` discovery, add:
- Resolve `BoneCollection.Bone` objects from `_bodyRenderer.Model.Bones.GetBone(name)` for each name in `UpperBodyBones`
- Create `_sampleModel = new SceneModel(GameObject.Scene.SceneWorld, _bodyRenderer.Model, Transform.Zero)` and position it far offscreen (e.g., `Position = new Vector3(0, 0, -10000)`)

### Step 3: Rewrite TryPlaySequence — keep animgraph running

Instead of:
```csharp
sceneModel.UseAnimGraph = false;
sceneModel.CurrentSequence.Name = sequenceName;
```

Do:
```csharp
// Configure the hidden sample model
_sampleModel.UseAnimGraph = false;
_sampleModel.CurrentSequence.Name = sequenceName;
_sampleModel.CurrentSequence.Time = 0f;
_sequenceTime = 0f;
_currentBlendWeight = 0f;

_sequenceActive = true;
_sequenceRevertTime = Time.Now + duration;
```

The animgraph stays enabled — locomotion continues uninterrupted.

### Step 4: Add DriveAttackBoneOverrides method — the core blending

Called from `OnUpdate` when `_sequenceActive && _overrideBones is not null`.

Each frame:
1. Advance `_sequenceTime += Time.Delta`
2. Set `_sampleModel.CurrentSequence.Time = _sequenceTime`
3. Compute blend weight:
   - Fade in: `_sequenceTime / _blendInDuration` clamped to [0,1]
   - Near end: `(_sequenceRevertTime - Time.Now) / _blendOutDuration` clamped to [0,1]
   - Take the minimum of both
4. For each bone in `_overrideBones`:
   - `_bodyRenderer.TryGetBoneTransformAnimation(bone, out var animTx)` — get animgraph pose
   - `_sampleModel.GetBoneWorldTransform(bone.Index)` — get attack pose
   - `var blended = Transform.Lerp(animTx, attackTx, _currentBlendWeight)`
   - `_bodyRenderer.SetBoneTransform(bone, blended)`

### Step 5: Modify DriveSequenceRevert — clean up overrides

When the sequence ends, in addition to the existing logic:
- `_bodyRenderer.ClearPhysicsBones()` — remove all bone overrides
- `_currentBlendWeight = 0f`

Remove the `UseAnimGraph = true` line (it was only needed because we disabled it before).

### Step 6: Modify DriveAttackVisual — skip when clip is active

The existing `_usingClipVisual` path already skips the procedural lunge. No changes needed here — it already works correctly.

## Blend Weight Curve

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

## Edge Cases Handled

- **Sample model creation fails** — `TryPlaySequence` returns false, falls back to procedural lunge (existing behavior)
- **Bone not found on model** — filtered out during `OnAwake` resolution, skipped at runtime
- **`TryGetBoneTransformAnimation` returns false** — skip that bone this frame
- **Quick successive attacks** — sample model just switches sequence, blend weight resets
- **Sequence name not found** — caught by try/catch in `TryPlaySequence`, returns false

## Citizen Rig Bone Reference

The s&box Citizen model uses these bone names (from `SboxBoneClassifier.cs`):

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

## Key API References

| API | Purpose |
|-----|---------|
| `SceneModel(SceneWorld, Model, Transform)` | Create hidden sample model for sequence sampling |
| `SceneModel.UseAnimGraph = false` | Disable animgraph on sample model (required for raw sequence) |
| `SceneModel.CurrentSequence.Name/Time` | Set and scrub the attack sequence |
| `SceneModel.GetBoneWorldTransform(int boneIndex)` | Read posed bone transform from sample model |
| `SkinnedModelRenderer.TryGetBoneTransformAnimation(Bone, out Transform)` | Read animgraph-computed bone pose |
| `SkinnedModelRenderer.SetBoneTransform(Bone, Transform)` | Override a bone (applied after animgraph) |
| `SkinnedModelRenderer.ClearPhysicsBones()` | Clear all bone overrides |
| `Model.Bones.GetBone(string name)` | Resolve bone name to BoneCollection.Bone |

## Testing Approach

1. Set animation names on AttackDefinition (already done)
2. Attack while standing still — should play attack animation on upper body
3. Attack while walking — legs should continue walking, upper body plays attack
4. Attack while running — same blending behavior
5. Rapid successive attacks — no popping or visual glitches
6. Check that the procedural lunge still works as fallback when no animation name is set
