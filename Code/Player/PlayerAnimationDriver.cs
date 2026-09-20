using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Drives the Citizen locomotion animation graph, and handles attack/finisher/drink animations.
///
/// Attacks use bone-level override blending (see PLAN_AnimationBlending.md): the main animgraph
/// keeps running the whole time - so legs/hips keep walking normally - while a hidden secondary
/// SceneModel scrubs the attack clip in isolation, and its pose is blended onto just the
/// upper-body bones each frame (fade in, hold, fade out). This replaces the older approach of
/// disabling the whole animgraph to play a clip, which froze the legs solid mid-attack.
///
/// The drink "chug" animation doesn't need any of that: movement is paused for its entire
/// duration (see DrinkMeter/PlayerMovement/PlayerFacing), so there's no locomotion to protect,
/// and it just does a plain full-body sequence swap the simple way.
///
/// Drunk walk (see DriveDrunkWalk) uses that same full-body swap, but held for as long as the
/// player is both drunk enough and actually moving, instead of a fixed duration - it's off by
/// default (DrunkWalkAnimation empty) until a stumble-walk clip is wired up.
///
/// Every step here is best-effort and wrapped defensively: if a clip name is wrong, a bone
/// doesn't resolve, or an API doesn't behave as expected, it falls back all the way to the
/// original procedural lunge/squash-stretch rather than breaking combat.
/// </summary>
public class PlayerAnimationDriver : Component
{
	public static PlayerAnimationDriver Local { get; private set; }

	[Property] public GameObject ModelPivot { get; set; }
	[Property] public Sandbox.Citizen.CitizenAnimationHelper AnimHelper { get; set; }

	[Property, Group( "Tuning" )] public float LungeDistance { get; set; } = 14f;
	[Property, Group( "Tuning" )] public float SquashAmount { get; set; } = 0.12f;

	// Bumped from 0.08/0.12 - the shorter values snapped in/out of the attack pose almost instantly,
	// which read as stiff/mechanical rather than a fluid strike. Still fast enough not to feel laggy
	// mid-combo, especially now that the ramp itself is eased (see Smoothstep in
	// DriveAttackBoneOverrides) instead of a straight linear blend.
	[Property, Group( "Attack Blending" )] public float BlendInDuration { get; set; } = 0.12f;
	[Property, Group( "Attack Blending" )] public float BlendOutDuration { get; set; } = 0.18f;

	// Drunk walk: once the player is drunk enough AND actually moving, swap the whole body onto a
	// looping stumble-walk clip instead of the normal Citizen locomotion animgraph (same full-body
	// swap mechanism as attacks/drink - see TryPlaySequenceFull). Leave DrunkWalkAnimation blank
	// until a clip/sequence exists for it; empty string just means "feature off".
	[Property, Group( "Drunk Walk" )] public string DrunkWalkAnimation { get; set; } = "";
	[Property, Group( "Drunk Walk" )] public float DrunkWalkThreshold01 { get; set; } = 0.3f;
	[Property, Group( "Drunk Walk" )] public float DrunkWalkMoveSpeedThreshold { get; set; } = 15f;

	// Citizen rig bone names, upper body only ("spine_0" and up) - see PLAN_AnimationBlending.md.
	// Everything below this (pelvis, legs, feet) is left entirely animgraph-driven, so locomotion
	// never breaks mid-attack.
	static readonly string[] UpperBodyBones =
	{
		"spine_0", "spine_1", "spine_2", "spine_3",
		"neck_0", "head",
		"clavicle_L", "clavicle_R",
		"arm_upper_L", "arm_upper_R",
		"arm_lower_L", "arm_lower_R",
		"hand_L", "hand_R",
	};

	CharacterController _cc;
	SkinnedModelRenderer _bodyRenderer;

	/// <summary>Exposed so PlayerOutfit can apply the saved outfit/skin selection to the live player
	/// model, both at spawn and immediately when the Customize screen changes a selection.</summary>
	public SkinnedModelRenderer BodyRenderer => _bodyRenderer;

	float _swingStartTime = -999f;
	float _swingDuration = 0.2f;
	float _swingStrength = 0.5f;
	bool _usingClipVisual;

	float _drinkStartTime = -999f;
	float _drinkDuration = 0.9f;
	bool _isDrinkingVisual;
	bool _usingDrinkClipVisual;

	// --- Attack bone-blend state (see DriveAttackBoneOverrides) ---
	SceneModel _sampleModel;
	BoneCollection.Bone[] _overrideBones;
	bool _attackSequenceActive;
	float _attackSequenceRevertTime;
	float _sequenceTime;
	float _currentBlendWeight;

	// --- Drink full-body sequence state (no blending needed - movement is paused during it) ---
	bool _drinkSequenceActive;
	float _drinkSequenceRevertTime;

	// --- Drunk walk state (see DriveDrunkWalk) ---
	bool _drunkWalkActive;

	// --- Hit-flash state (see FlashHit) ---
	const float HitFlashDuration = 0.18f;
	float _hitFlashStartTime = -999f;
	Color _bodyBaseTint = Color.White;
	bool _hasCapturedBaseTint;

	protected override void OnAwake()
	{
		Local = this;
		_cc = Components.Get<CharacterController>();

		// Auto-discover the child that holds the model, rather than requiring a wired-up
		// editor reference - just name a child GameObject "ModelPivot" and this finds it.
		ModelPivot ??= GameObject.Children.FirstOrDefault( c => c.Name == "ModelPivot" );

		_bodyRenderer = ModelPivot?.Components.Get<SkinnedModelRenderer>() ?? Components.Get<SkinnedModelRenderer>();

		AnimHelper ??= Components.Get<Sandbox.Citizen.CitizenAnimationHelper>()
			?? ModelPivot?.Components.Get<Sandbox.Citizen.CitizenAnimationHelper>();

		if ( AnimHelper is not null && AnimHelper.Target is null && _bodyRenderer is not null )
			AnimHelper.Target = _bodyRenderer;

		SetupBoneBlending();
	}

	protected override void OnDestroy()
	{
		try { _sampleModel?.Delete(); } catch { }
	}

	/// <summary>
	/// Resolves the upper-body bone list and spins up the hidden sample model used to scrub
	/// attack sequences without ever touching the main animgraph. Best-effort: if the model has
	/// no matching bones, or SceneModel creation fails for any reason, _overrideBones/_sampleModel
	/// stay null and attacks just fall back to the old full-body swap (see TryPlaySequenceBlended)
	/// - still better than no animation at all, just without the leg-blend.
	/// </summary>
	void SetupBoneBlending()
	{
		try
		{
			if ( _bodyRenderer?.Model is null )
				return;

			_overrideBones = UpperBodyBones
				.Select( name => _bodyRenderer.Model.Bones.GetBone( name ) )
				.Where( b => b is not null )
				.ToArray();

			if ( _overrideBones.Length == 0 )
			{
				Log.Warning( "[AnimBlend] No bones resolved - bone-blend attacks will fall back to full-body swap. Check that the model's bone names match the Citizen rig (spine_0, clavicle_L, etc.)." );
				_overrideBones = null;
				return;
			}

			_sampleModel = new SceneModel( Scene.SceneWorld, _bodyRenderer.Model, new Transform( new Vector3( 0, 0, -10000 ) ) );
			_sampleModel.UseAnimGraph = false;

			// This hidden model must never actually render - it exists only so we can scrub a
			// sequence on it and read bone transforms back off it. Previously it was hidden purely
			// by parking it 10000 units away, but GetBoneWorldTransform() returns transforms in
			// THAT far-away space, and blending those straight against the real body's (correctly
			// positioned) bones dragged the real upper-body bones toward -10000 too - which is
			// exactly what made the torso/arms vanish mid-attack (they were being rendered 10000
			// units below the map). Fixed two ways: RenderingEnabled=false so it truly never
			// renders regardless of where it sits, and DriveAttackBoneOverrides now re-parents its
			// transform onto the real body every frame before sampling, so the bone transforms it
			// reports are already in the right space and need no further offsetting.
			try { _sampleModel.RenderingEnabled = false; } catch { }
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[AnimBlend] SetupBoneBlending failed: {ex.Message}" );
			_overrideBones = null;
			_sampleModel = null;
		}
	}

	protected override void OnUpdate()
	{
		DriveLocomotion();
		DriveDrunkWalk();
		DriveAttackBoneOverrides();
		DriveDrinkSequenceRevert();
		DriveAttackVisual();
		DriveDrinkVisual();
		DriveHitFlash();
	}

	/// <summary>Called by PlayerStats the instant damage actually lands - a quick red/white tint
	/// pulse on the body so getting hit reads clearly even mid-combat chaos. Purely cosmetic,
	/// wrapped defensively like every other visual here.</summary>
	public void FlashHit()
	{
		_hitFlashStartTime = Time.Now;
	}

	void DriveHitFlash()
	{
		if ( _bodyRenderer is null )
			return;

		try
		{
			if ( !_hasCapturedBaseTint )
			{
				_bodyBaseTint = _bodyRenderer.Tint;
				_hasCapturedBaseTint = true;
			}

			var t = System.Math.Clamp( (Time.Now - _hitFlashStartTime) / HitFlashDuration, 0f, 1f );
			if ( t >= 1f )
			{
				_bodyRenderer.Tint = _bodyBaseTint;
				return;
			}

			// Fast in, fast out - a sharp pulse rather than a slow fade, so it reads as "impact"
			// instead of a status effect.
			var pulse = 1f - t;
			_bodyRenderer.Tint = Color.Lerp( _bodyBaseTint, new Color( 1f, 0.25f, 0.25f ), pulse );
		}
		catch { }
	}

	void DriveLocomotion()
	{
		if ( AnimHelper is null )
			return;

		var velocity = _cc?.Velocity ?? Vector3.Zero;
		AnimHelper.WithVelocity( velocity );
		AnimHelper.WithWishVelocity( velocity );
		AnimHelper.IsGrounded = _cc?.IsOnGround ?? true;
	}

	public void PlayAttackSwing( AttackDefinition def )
	{
		_swingStartTime = Time.Now;
		_swingDuration = def.AnimationDuration > 0f ? def.AnimationDuration : System.MathF.Max( 0.1f, def.Recovery + 0.1f );
		_swingStrength = def.ImpactStrength;

		_usingClipVisual = def.DisableBlending
			? TryPlaySequenceFull( def.Animation, _swingDuration )
			: TryPlaySequenceBlended( def.Animation, _swingDuration );
	}

	public void PlayFinisher( FinisherDefinition def )
	{
		// Duration is the single source of truth (FinisherDefinition.Duration) - the animation,
		// the damage tick schedule, and the input lock all agree, so the clip and the beats stay
		// in sync instead of the old per-file fallback drifting apart.
		_swingStartTime = Time.Now;
		_swingDuration = def.Duration;
		_swingStrength = 1.2f;

		_usingClipVisual = def.DisableBlending
			? TryPlaySequenceFull( def.Animation, _swingDuration )
			: TryPlaySequenceBlended( def.Animation, _swingDuration );
	}

	/// <summary>Called by DrinkMeter the instant a glass is triggered - plays the "chug" beat.
	/// Movement is paused for the whole drink, so this uses the simple full-body swap rather than
	/// the attack blend path - there's no locomotion to protect.</summary>
	public void PlayDrink( string animName, float duration )
	{
		_drinkStartTime = Time.Now;
		_drinkDuration = System.MathF.Max( 0.2f, duration );
		_isDrinkingVisual = true;

		_usingDrinkClipVisual = TryPlaySequenceFull( animName, _drinkDuration );
	}

	/// <summary>
	/// Attack path: leaves the main animgraph running (legs/hips keep walking) and instead scrubs
	/// the clip on the hidden secondary SceneModel, which DriveAttackBoneOverrides blends onto just
	/// the upper-body bones every frame (see that method's doc comment for the coordinate-space
	/// pipeline - the earlier invisible/wrecked-body bugs both traced back to space mismatches in
	/// that pipeline, now fixed: fresh animation-only sampling, a flushed sample pose via
	/// Update(0f), and converting the blended world pose to model-local before SetBoneTransform).
	/// Falls back to the full-body swap if the bone-blend setup isn't available on this model.
	/// </summary>
	bool TryPlaySequenceBlended( string sequenceName, float duration )
	{
		if ( string.IsNullOrEmpty( sequenceName ) )
			return false;

		if ( _sampleModel is null || _overrideBones is null )
			return TryPlaySequenceFull( sequenceName, duration );

		try
		{
			_sampleModel.CurrentSequence.Name = sequenceName;
			_sampleModel.CurrentSequence.Time = 0f;

			_sequenceTime = 0f;
			_currentBlendWeight = 0f;
			_attackSequenceActive = true;
			_attackSequenceRevertTime = Time.Now + duration;
			return true;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[AnimBlend] '{sequenceName}' blended playback threw, falling back to full-body swap: {ex.Message}" );
			_attackSequenceActive = false;
			return TryPlaySequenceFull( sequenceName, duration );
		}
	}

	/// <summary>Old/simple approach: fully disables the animgraph on the main body and plays the
	/// sequence directly. Used for the drink chug (no locomotion to preserve) and as the fallback
	/// if the attack bone-blend setup isn't available on this model.</summary>
	bool TryPlaySequenceFull( string sequenceName, float duration )
	{
		if ( string.IsNullOrEmpty( sequenceName ) || _bodyRenderer is null )
			return false;

		try
		{
			var sceneModel = _bodyRenderer.SceneModel;
			if ( sceneModel is null )
				return false;

			sceneModel.UseAnimGraph = false;
			sceneModel.CurrentSequence.Name = sequenceName;

			_drinkSequenceActive = true;
			_drinkSequenceRevertTime = Time.Now + duration;
			return true;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[AnimBlend] '{sequenceName}' full-body playback threw: {ex.Message}" );
			_drinkSequenceActive = false;
			return false;
		}
	}

	void DriveDrinkSequenceRevert()
	{
		if ( !_drinkSequenceActive )
			return;

		if ( Time.Now >= _drinkSequenceRevertTime )
		{
			_drinkSequenceActive = false;

			try
			{
				if ( _bodyRenderer?.SceneModel is not null )
					_bodyRenderer.SceneModel.UseAnimGraph = true;
			}
			catch
			{
				// If this fails there's nothing more we can safely do from here.
			}
		}
	}

	/// <summary>
	/// When drunk enough and actually moving, swaps the body onto a looping stumble-walk clip
	/// instead of the normal Citizen locomotion animgraph - same full-body-swap mechanism as
	/// TryPlaySequenceFull, just held for as long as the condition holds rather than a fixed
	/// duration. Defers entirely to an active attack/drink swap (_drinkSequenceActive) so it never
	/// fights those; DriveDrinkSequenceRevert already hands UseAnimGraph back afterward, and
	/// clearing _drunkWalkActive here lets this cleanly re-enter next frame instead of thinking
	/// it's still mid-loop. No-ops entirely until DrunkWalkAnimation is set to a real sequence name.
	/// </summary>
	void DriveDrunkWalk()
	{
		if ( _bodyRenderer is null || string.IsNullOrEmpty( DrunkWalkAnimation ) )
			return;

		if ( _drinkSequenceActive )
		{
			_drunkWalkActive = false;
			return;
		}

		var sceneModel = _bodyRenderer.SceneModel;
		if ( sceneModel is null )
			return;

		var fraction = DrunkennessSystem.Local?.Fraction01 ?? 0f;
		var vel = _cc?.Velocity ?? Vector3.Zero;
		var groundSpeed = new Vector3( vel.x, vel.y, 0f ).Length;
		var shouldWalk = fraction >= DrunkWalkThreshold01 && groundSpeed >= DrunkWalkMoveSpeedThreshold;

		if ( shouldWalk )
		{
			if ( _drunkWalkActive )
				return;

			try
			{
				sceneModel.UseAnimGraph = false;
				sceneModel.CurrentSequence.Name = DrunkWalkAnimation;
				_drunkWalkActive = true;
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"[DrunkWalk] Failed to play '{DrunkWalkAnimation}': {ex.Message}" );
				_drunkWalkActive = false;
			}
		}
		else if ( _drunkWalkActive )
		{
			try { sceneModel.UseAnimGraph = true; }
			catch { }
			_drunkWalkActive = false;
		}
	}

	/// <summary>
	/// The core of the blend plan: scrubs the hidden sample model forward in time, computes a
	/// fade-in/fade-out blend weight (see PLAN_AnimationBlending.md's blend curve), and overrides
	/// each upper-body bone on the real body renderer with a lerp between its current animgraph
	/// pose and the sampled attack pose. Cleans up via ClearPhysicsBones() once the attack ends.
	///
	/// Coordinate-space pipeline (critical — several bugs were caused by getting this wrong):
	///
	///   1. TryGetBoneTransformAnimation → world-space, animation-only (before physics/procedural
	///      and before SetBoneOverride). Must NOT use TryGetBoneTransformLocal here — that returns
	///      the FINAL bone state including previous overrides, creating a feedback loop where the
	///      blend compounds each frame.
	///
	///   2. GetBoneWorldTransform on the sample model → world-space. The sample model is synced to
	///      the body renderer's world position each frame so these are directly comparable.
	///      The sample model also needs Update(0f) after setting CurrentSequence.Time or
	///      GetBoneWorldTransform returns stale bind-pose data.
	///
	///   3. Lerp in world-space (both values are now comparable).
	///
	///   4. SetBoneOverride (via SetBoneTransform) expects MODEL-LOCAL coordinates — i.e. the
	///      bone's position relative to the SceneModel's own world transform. We convert from
	///      world-space via modelWorldTx.ToLocal(blendedWorld). Do NOT use SetBoneWorldTransform
	///      here — it's a one-shot override that the animgraph immediately overwrites next frame.
	///      SetBoneOverride persists until ClearBoneOverrides/ClearPhysicsBones.
	///
	///   5. The sample model is hidden via RenderingEnabled = false (not by position) while its
	///      position is synced for the blend. RenderingEnabled = false does NOT suppress bone
	///      updates — it only controls mesh visibility. The model is moved back to (0,0,-10000)
	///      when the blend ends.
	/// </summary>
	void DriveAttackBoneOverrides()
	{
		if ( !_attackSequenceActive )
			return;

		if ( _sampleModel is null || _overrideBones is null || _bodyRenderer is null )
		{
			_attackSequenceActive = false;
			return;
		}

		if ( Time.Now >= _attackSequenceRevertTime )
		{
			_attackSequenceActive = false;
			_currentBlendWeight = 0f;

			// Move the sample model back offscreen now that blending is done.
			_sampleModel.Transform = new Transform( new Vector3( 0, 0, -10000 ) );

			try { _bodyRenderer.ClearPhysicsBones(); } catch { }
			return;
		}

		try
		{
			_sequenceTime += Time.Delta;
			_sampleModel.CurrentSequence.Time = _sequenceTime;

			// Flush the new sequence time into the sample model's bone state so
			// GetBoneWorldTransform returns the current pose, not stale bind data.
			_sampleModel.Update( 0f );

			// Smoothstep instead of a raw linear ramp - a straight-line blend weight snaps to
			// noticeable speed changes right at the start/end of the fade, which is what made the
			// bone-blend attacks feel stiff/mechanical rather than fluid. Easing both ends of the
			// curve (slow-fast-slow) reads as a much more natural strike.
			var fadeIn = BlendInDuration > 0f ? Smoothstep( System.Math.Clamp( _sequenceTime / BlendInDuration, 0f, 1f ) ) : 1f;
			var remaining = _attackSequenceRevertTime - Time.Now;
			var fadeOut = BlendOutDuration > 0f ? Smoothstep( System.Math.Clamp( remaining / BlendOutDuration, 0f, 1f ) ) : 1f;
			_currentBlendWeight = System.MathF.Min( fadeIn, fadeOut );

			// Hide the sample model so it doesn't render — it's only here for bone data.
			_sampleModel.RenderingEnabled = false;
			_sampleModel.Transform = _bodyRenderer.SceneModel.Transform;

			var modelWorldTx = _bodyRenderer.SceneModel.Transform;

			foreach ( var bone in _overrideBones )
			{
				// TryGetBoneTransformAnimation returns the pure animation output (before physics/procedural
				// and before any SetBoneOverride effects), avoiding a feedback loop where previously-set
				// overrides contaminate the next frame's read.
				if ( !_bodyRenderer.TryGetBoneTransformAnimation( bone, out var animWorldTx ) )
					continue;
				// Both are world-space at the same world position (sample model synced above).
				var attackWorldTx = _sampleModel.GetBoneWorldTransform( bone.Index );
				var blendedWorld = animWorldTx.LerpTo( attackWorldTx, _currentBlendWeight );

				// SetBoneOverride (called by SetBoneTransform) expects model-local coordinates.
				// Convert the world-space blend result via the SceneModel's world transform.
				var blendedLocal = modelWorldTx.ToLocal( blendedWorld );
				_bodyRenderer.SetBoneTransform( bone, blendedLocal );
			}
		}
		catch ( System.Exception ex )
		{
			// Any API shape mismatch here - bail out of the blend for the rest of this attack
			// rather than risking a per-frame exception storm.
			Log.Warning( $"[AnimBlend] DriveAttackBoneOverrides threw, disabling blend for this attack: {ex.Message}" );
			_attackSequenceActive = false;
			_currentBlendWeight = 0f;
			_sampleModel.Transform = new Transform( new Vector3( 0, 0, -10000 ) );
			try { _bodyRenderer.ClearPhysicsBones(); } catch { }
		}
	}

	void DriveAttackVisual()
	{
		if ( ModelPivot is null )
			return;

		var t = (Time.Now - _swingStartTime) / _swingDuration;

		// A real animation clip is playing - don't fight it with the procedural offset, just
		// make sure the pivot itself is settled at rest.
		if ( _usingClipVisual )
		{
			ModelPivot.LocalPosition = Vector3.Lerp( ModelPivot.LocalPosition, Vector3.Zero, System.Math.Clamp( Time.Delta * 12f, 0f, 1f ) );
			ModelPivot.LocalScale = Vector3.Lerp( ModelPivot.LocalScale, Vector3.One, System.Math.Clamp( Time.Delta * 12f, 0f, 1f ) );

			if ( t > 1f )
				_usingClipVisual = false;

			return;
		}

		if ( t < 0f || t > 1f )
		{
			ModelPivot.LocalPosition = Vector3.Lerp( ModelPivot.LocalPosition, Vector3.Zero, System.Math.Clamp( Time.Delta * 12f, 0f, 1f ) );
			ModelPivot.LocalScale = Vector3.Lerp( ModelPivot.LocalScale, Vector3.One, System.Math.Clamp( Time.Delta * 12f, 0f, 1f ) );
			return;
		}

		// Quick out-and-back curve: fast lunge forward, slower recover. Smoothstepped on both halves
		// instead of a straight linear ramp so the lunge eases into and out of its peak rather than
		// changing direction/speed abruptly at t=0.35 - that sharp corner was a big part of what made
		// the procedural fallback swing feel stiff compared to a real animation clip.
		var curve = t < 0.35f
			? Smoothstep( t / 0.35f )
			: Smoothstep( 1f - ((t - 0.35f) / 0.65f) );

		var lunge = Vector3.Forward * (LungeDistance * _swingStrength * curve);
		ModelPivot.LocalPosition = lunge;

		var squash = 1f + SquashAmount * _swingStrength * curve;
		ModelPivot.LocalScale = new Vector3( squash, 1f / squash * 0.5f + 0.5f, 1f / squash * 0.5f + 0.5f );
	}

	/// <summary>Procedural "chug" fallback: tilts the model back and dips it slightly, distinct from
	/// the attack lunge curve. Skipped if a real Animation clip took over via TryPlaySequenceFull.</summary>
	void DriveDrinkVisual()
	{
		if ( !_isDrinkingVisual || ModelPivot is null )
			return;

		var t = (Time.Now - _drinkStartTime) / _drinkDuration;

		if ( t > 1f )
		{
			_isDrinkingVisual = false;
			try { ModelPivot.LocalRotation = Rotation.Identity; } catch { }
			return;
		}

		if ( _usingDrinkClipVisual )
			return;

		// Tilt back and hold, then recover - a "head back, chugging" read. Smoothstepped for the same
		// reason as the attack lunge curve above - avoids a mechanical linear snap at the peak.
		var curve = t < 0.4f ? Smoothstep( t / 0.4f ) : Smoothstep( 1f - ((t - 0.4f) / 0.6f) );

		try
		{
			ModelPivot.LocalRotation = Rotation.FromPitch( -25f * curve );
			ModelPivot.LocalPosition = Vector3.Down * (4f * curve);
		}
		catch
		{
			// Rotation.FromPitch shape mismatch or similar - not fatal, just skip the tilt this frame.
		}
	}

	/// <summary>Classic ease-in/ease-out cubic (3t²-2t³), clamped to [0,1] input. Used everywhere a
	/// linear 0-1 blend factor would otherwise change speed abruptly at either end - attack bone-blend
	/// weight, procedural lunge, and drink tilt all route through this for a more fluid feel.</summary>
	static float Smoothstep( float t )
	{
		t = System.Math.Clamp( t, 0f, 1f );
		return t * t * (3f - 2f * t);
	}
}
