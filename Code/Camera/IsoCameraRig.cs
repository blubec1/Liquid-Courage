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

		WorldPosition = _smoothedPosition + ComputeShakeOffset();
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
}
