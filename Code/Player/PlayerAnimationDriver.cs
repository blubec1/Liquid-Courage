using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Drives the Citizen locomotion animation graph, and handles attack/finisher "impact" visuals.
///
/// If an AttackDefinition/FinisherDefinition has an Animation name set (a Citizen-compatible
/// sequence - e.g. a Mixamo clip retargeted onto the Citizen skeleton), we try to play that
/// sequence directly. If not, we fall back to a purely procedural lunge + squash/stretch on the
/// model pivot instead.
///
/// Playing a one-shot sequence on top of the Citizen anim graph is a known rough edge in s&box
/// (there's no simple first-class "PlayAnimation(name)" call at the time this was written), so
/// TryPlaySequence is deliberately defensive: if it fails for any reason, we silently fall back
/// to the procedural version rather than breaking the attack.
/// </summary>
public class PlayerAnimationDriver : Component
{
	public static PlayerAnimationDriver Local { get; private set; }

	[Property] public GameObject ModelPivot { get; set; }
	[Property] public Sandbox.Citizen.CitizenAnimationHelper AnimHelper { get; set; }

	[Property, Group( "Tuning" )] public float LungeDistance { get; set; } = 14f;
	[Property, Group( "Tuning" )] public float SquashAmount { get; set; } = 0.12f;

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

	bool _sequenceActive;
	float _sequenceRevertTime;

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
	}

	protected override void OnUpdate()
	{
		DriveLocomotion();
		DriveSequenceRevert();
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

		_usingClipVisual = TryPlaySequence( def.Animation, _swingDuration );
	}

	public void PlayFinisher( FinisherDefinition def )
	{
		_swingStartTime = Time.Now;
		_swingDuration = def.AnimationDuration > 0f ? def.AnimationDuration : System.MathF.Max( 0.2f, def.StaggerTime * 0.6f + 0.2f );
		_swingStrength = 1.2f;

		_usingClipVisual = TryPlaySequence( def.Animation, _swingDuration );
	}

	/// <summary>Called by DrinkMeter the instant a glass is triggered - plays the "chug" beat.</summary>
	public void PlayDrink( string animName, float duration )
	{
		_drinkStartTime = Time.Now;
		_drinkDuration = System.MathF.Max( 0.2f, duration );
		_isDrinkingVisual = true;

		_usingDrinkClipVisual = TryPlaySequence( animName, _drinkDuration );
	}

	/// <summary>
	/// Best-effort: temporarily disables the Citizen anim graph and plays a named sequence
	/// directly. Returns true if it looks like it took. See class remarks - this is a known
	/// rough edge, so any failure here just means we fall back to the procedural lunge.
	/// </summary>
	bool TryPlaySequence( string sequenceName, float duration )
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

			_sequenceActive = true;
			_sequenceRevertTime = Time.Now + duration;
			return true;
		}
		catch
		{
			// Sequence name not found, API shape different than expected, etc - not fatal.
			_sequenceActive = false;
			return false;
		}
	}

	void DriveSequenceRevert()
	{
		if ( !_sequenceActive )
			return;

		if ( Time.Now >= _sequenceRevertTime )
		{
			_sequenceActive = false;

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
	/// the attack lunge curve. Skipped if a real Animation clip took over via TryPlaySequence.</summary>
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
