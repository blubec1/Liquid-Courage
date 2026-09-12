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

	[Property, Group( "Attack Blending" )] public float BlendInDuration { get; set; } = 0.08f;
	[Property, Group( "Attack Blending" )] public float BlendOutDuration { get; set; } = 0.12f;

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

			Log.Info( $"[AnimBlend] Resolved {_overrideBones.Length}/{UpperBodyBones.Length} upper-body bones on model '{_bodyRenderer.Model?.ResourceName}'." );

			if ( _overrideBones.Length == 0 )
			{
				Log.Warning( "[AnimBlend] No bones resolved - bone-blend attacks will fall back to full-body swap. Check that the model's bone names match the Citizen rig (spine_0, clavicle_L, etc.)." );
				_overrideBones = null;
				return;
			}

			_sampleModel = new SceneModel( Scene.SceneWorld, _bodyRenderer.Model, new Transform( new Vector3( 0, 0, -10000 ) ) );
			_sampleModel.UseAnimGraph = false;
			Log.Info( "[AnimBlend] Hidden sample model created OK." );
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
		DriveDrinkSequenceRevert();
		DriveAttackBoneOverrides();
		DriveAttackVisual();
		DriveDrinkVisual();
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

		_usingClipVisual = TryPlaySequenceBlended( def.Animation, _swingDuration );
	}

	public void PlayFinisher( FinisherDefinition def )
	{
		_swingStartTime = Time.Now;
		_swingDuration = def.AnimationDuration > 0f ? def.AnimationDuration : System.MathF.Max( 0.2f, def.StaggerTime * 0.6f + 0.2f );
		_swingStrength = 1.2f;

		_usingClipVisual = TryPlaySequenceBlended( def.Animation, _swingDuration );
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
	/// Attack path: leaves the main animgraph running (legs keep walking) and instead scrubs the
	/// clip on a hidden secondary SceneModel, which DriveAttackBoneOverrides blends onto just the
	/// upper-body bones every frame. See PLAN_AnimationBlending.md. Falls back to a full-body swap
	/// if the bone-blend setup isn't available on this model.
	/// </summary>
	bool TryPlaySequenceBlended( string sequenceName, float duration )
	{
		if ( string.IsNullOrEmpty( sequenceName ) )
			return false;

		if ( _sampleModel is null || _overrideBones is null )
		{
			Log.Info( $"[AnimBlend] '{sequenceName}': no sample model/bones, falling back to full-body swap." );
			return TryPlaySequenceFull( sequenceName, duration );
		}

		try
		{
			_sampleModel.CurrentSequence.Name = sequenceName;
			_sampleModel.CurrentSequence.Time = 0f;

			_sequenceTime = 0f;
			_currentBlendWeight = 0f;
			_attackSequenceActive = true;
			_attackSequenceRevertTime = Time.Now + duration;

			Log.Info( $"[AnimBlend] Playing '{sequenceName}' (blended, {duration:0.00}s). CurrentSequence.Name now reads back as '{_sampleModel.CurrentSequence.Name}'." );
			return true;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[AnimBlend] '{sequenceName}' blended playback threw: {ex.Message}" );
			_attackSequenceActive = false;
			return false;
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
			Log.Info( $"[AnimBlend] Playing '{sequenceName}' (full-body swap, {duration:0.00}s)." );
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
	/// The core of the blend plan: scrubs the hidden sample model forward in time, computes a
	/// fade-in/fade-out blend weight (see PLAN_AnimationBlending.md's blend curve), and overrides
	/// each upper-body bone on the real body renderer with a lerp between its current animgraph
	/// pose and the sampled attack pose. Cleans up via ClearPhysicsBones() once the attack ends.
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

			try { _bodyRenderer.ClearPhysicsBones(); } catch { }
			return;
		}

		bool isFirstFrame = _sequenceTime <= 0f;
		int hitCount = 0, missCount = 0;

		try
		{
			_sequenceTime += Time.Delta;
			_sampleModel.CurrentSequence.Time = _sequenceTime;

			var fadeIn = BlendInDuration > 0f ? System.Math.Clamp( _sequenceTime / BlendInDuration, 0f, 1f ) : 1f;
			var remaining = _attackSequenceRevertTime - Time.Now;
			var fadeOut = BlendOutDuration > 0f ? System.Math.Clamp( remaining / BlendOutDuration, 0f, 1f ) : 1f;
			_currentBlendWeight = System.MathF.Min( fadeIn, fadeOut );

			foreach ( var bone in _overrideBones )
			{
				if ( !_bodyRenderer.TryGetBoneTransformAnimation( bone, out var animTx ) )
				{
					missCount++;
					continue;
				}

				var attackTx = _sampleModel.GetBoneWorldTransform( bone.Index );
				// Fully-qualified via the global namespace alias: bare "Transform" here would
				// resolve to Component.Transform (this component's own GameTransform property),
				// not the real Transform struct type - that's what caused CS1061 "GameTransform
				// does not contain Lerp". "Sandbox.Transform" is NOT the fix, despite looking like
				// the obvious disambiguation - Transform lives in the global namespace, not nested
				// under Sandbox, so that qualification throws CS0234 ("Transform does not exist in
				// the namespace Sandbox"). global::Transform is the correct, verified-compiling form.
				var blended = global::Transform.Lerp( animTx, attackTx, _currentBlendWeight, true );
				_bodyRenderer.SetBoneTransform( bone, blended );
				hitCount++;
			}

			if ( isFirstFrame )
				Log.Info( $"[AnimBlend] First blend frame: {hitCount} bones overridden, {missCount} missed (TryGetBoneTransformAnimation returned false), weight={_currentBlendWeight:0.00}, sampleSeqTime={_sampleModel.CurrentSequence.Time:0.000}." );
		}
		catch ( System.Exception ex )
		{
			// Any API shape mismatch here - bail out of the blend for the rest of this attack
			// rather than risking a per-frame exception storm.
			Log.Warning( $"[AnimBlend] DriveAttackBoneOverrides threw, disabling blend for this attack: {ex.Message}" );
			_attackSequenceActive = false;
			_currentBlendWeight = 0f;
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

		// Quick out-and-back curve: fast lunge forward, slower recover.
		var curve = t < 0.35f
			? (t / 0.35f)
			: 1f - ((t - 0.35f) / 0.65f);

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

		// Tilt back and hold, then recover - a "head back, chugging" read.
		var curve = t < 0.4f ? (t / 0.4f) : 1f - ((t - 0.4f) / 0.6f);

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
}
