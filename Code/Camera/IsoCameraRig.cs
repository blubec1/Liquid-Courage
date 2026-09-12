using System;

namespace DrunkenBarFight;

/// <summary>
/// Fixed-angle isometric camera that smoothly follows the player's position (never rotates)
/// and layers screenshake on top in response to GameEvents. Set the camera's rotation once in
/// the editor to whatever isometric angle looks best - this script only ever translates it.
/// </summary>
public class IsoCameraRig : Component
{
	public static IsoCameraRig Instance { get; private set; }

	[Property] public Vector3 Offset { get; set; } = new( -500, -500, 600 );
	[Property] public float FollowLerp { get; set; } = 9f;

	// Continuous ambient sway while the player is drunk - a GTA5-style light drift/wobble, not a
	// jittery impact shake. Smooth layered sine waves (rather than Random noise like the impact
	// shake below) so it reads as "swaying" instead of "vibrating". Driven by
	// DrunkennessSystem.ShakeFraction01, which is 0 until the player crosses ShakeStartValue (60 by
	// default) - stone sober and lightly buzzed both get zero sway - then ramps 0-1 from there to
	// MaxValue. Deliberately capped low even at full drunkenness (see DrunkSwayMaxAmount/
	// MaxSpeedMultiplier below) - it should read as "the room is gently swimming", never as
	// something that fights the player's ability to see/aim. Calm at every point on the curve,
	// including the peak.
	[Property, Group( "Drunk Sway" )] public float DrunkSwayMaxAmount { get; set; } = 4.5f;
	[Property, Group( "Drunk Sway" )] public float DrunkSwaySpeed { get; set; } = 0.85f;
	[Property, Group( "Drunk Sway" )] public float DrunkSwayEasePower { get; set; } = 1.3f;

	// Progression: right at ShakeStartValue the sway is both small AND slow (a gentle, almost-
	// missable drift); as ShakeFraction01 climbs toward 1 it gets a bit bigger and a bit quicker, so
	// the escalation still reads - but MaxSpeedMultiplier is kept modest on purpose so even the peak
	// stays calm rather than turning frantic.
	[Property, Group( "Drunk Sway" )] public float DrunkSwayMaxSpeedMultiplier { get; set; } = 1.6f;

	public CameraComponent Camera { get; private set; }

	public Vector3 FlatForward
	{
		get
		{
			var f = WorldRotation.Forward;
			var flat = new Vector3( f.x, f.y, 0 );
			return flat.Length > 0.001f ? flat / flat.Length : Vector3.Forward;
		}
	}

	public Vector3 FlatRight
	{
		get
		{
			var r = WorldRotation.Right;
			var flat = new Vector3( r.x, r.y, 0 );
			return flat.Length > 0.001f ? flat / flat.Length : Vector3.Right;
		}
	}

	Vector3 _smoothedPosition;
	float _shakeAmount;
	float _shakeEndTime = -999f;
	float _shakeDuration = 1f;

	protected override void OnAwake()
	{
		Instance = this;
		Camera = Components.Get<CameraComponent>();
		_smoothedPosition = WorldPosition;

		GameEvents.ShakeRequested += OnShakeRequested;
		GameEvents.HitStopRequested += OnHitStopRequested;
	}

	protected override void OnDestroy()
	{
		GameEvents.ShakeRequested -= OnShakeRequested;
		GameEvents.HitStopRequested -= OnHitStopRequested;
	}

	void OnShakeRequested( float amount, float duration )
	{
		_shakeAmount = MathF.Max( _shakeAmount * 0.4f, amount );
		_shakeEndTime = MathF.Max( _shakeEndTime, Time.Now + duration );
		_shakeDuration = MathF.Max( duration, 0.01f );
	}

	void OnHitStopRequested( float duration )
	{
		OnShakeRequested( 3f, duration * 1.5f );
	}

	protected override void OnUpdate()
	{
		var target = PlayerMovement.Local;
		if ( target is not null )
		{
			var desired = target.WorldPosition + Offset;
			_smoothedPosition = Vector3.Lerp( _smoothedPosition, desired, Math.Clamp( Time.Delta * FollowLerp, 0f, 1f ) );
		}

		WorldPosition = _smoothedPosition + ComputeShakeOffset() + ComputeDrunkSwayOffset();
	}

	Vector3 ComputeShakeOffset()
	{
		if ( Time.Now >= _shakeEndTime )
			return Vector3.Zero;

		var remaining = Math.Clamp( (_shakeEndTime - Time.Now) / _shakeDuration, 0f, 1f );
		var mag = _shakeAmount * remaining;

		return new Vector3(
			(Random.Shared.NextSingle() - 0.5f) * 2f * mag,
			(Random.Shared.NextSingle() - 0.5f) * 2f * mag,
			(Random.Shared.NextSingle() - 0.5f) * mag * 0.5f );
	}

	/// <summary>Light, continuous GTA5-style drunk sway - smooth and wavy rather than jittery, so it
	/// reads as "the world is gently swimming" rather than a combat hit. Each axis uses two summed
	/// sine waves at different, non-matching frequencies/phases so the drift never quite repeats.
	/// Zero when sober; eases in with drunkenness via DrunkSwayEasePower.</summary>
	Vector3 ComputeDrunkSwayOffset()
	{
		var fraction = DrunkennessSystem.Local?.ShakeFraction01 ?? 0f;
		if ( fraction <= 0f || DrunkSwayMaxAmount <= 0f )
			return Vector3.Zero;

		var eased = MathF.Pow( fraction, DrunkSwayEasePower );
		var mag = DrunkSwayMaxAmount * eased;

		// Speed ramps on the same eased curve as amplitude, from 1x at sober up to
		// DrunkSwayMaxSpeedMultiplier at full drunkenness - the wobble gets both bigger and faster
		// together as the run gets more dangerous, not just louder.
		var speedScale = 1f + eased * (DrunkSwayMaxSpeedMultiplier - 1f);
		var t = Time.Now * DrunkSwaySpeed * speedScale;

		var swayX = MathF.Sin( t * 0.9f ) * 0.6f + MathF.Sin( t * 2.3f + 1.7f ) * 0.4f;
		var swayY = MathF.Sin( t * 1.3f + 0.8f ) * 0.6f + MathF.Sin( t * 2.9f + 3.1f ) * 0.4f;
		var swayZ = MathF.Sin( t * 0.7f + 2.2f ) * 0.5f;

		return new Vector3( swayX, swayY, swayZ ) * mag;
	}
}
