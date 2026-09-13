namespace DrunkenBarFight;

/// <summary>
/// Self-destructing solid debris chunk - a tiny tinted box that flies out under gravity and fades,
/// destroying itself after Duration. Same no-real-particle-system rationale as FxPointLight (see
/// its doc comment) but for effects that need to read as physical material - blood, glass, wood
/// chips - rather than a light glow.
/// </summary>
public class FxDebris : Component
{
	public float Duration { get; set; } = 0.4f;
	public Vector3 Velocity { get; set; }
	public float Gravity { get; set; } = 700f;
	public float Drag { get; set; } = 1.5f;

	/// <summary>When true, stops falling and flattens out the instant it reaches GroundZ instead of
	/// fading out mid-air - used by blood droplets so they read as a splat that sticks briefly
	/// rather than a glowing mote that just dissolves in the air.</summary>
	public bool SplatOnGround { get; set; }
	public float GroundZ { get; set; }

	float _start;
	ModelRenderer _renderer;
	Color _baseTint = Color.White;
	bool _hasSplatted;

	protected override void OnAwake()
	{
		_start = Time.Now;
		_renderer = Components.Get<ModelRenderer>();
		if ( _renderer is not null )
			_baseTint = _renderer.Tint;
	}

	protected override void OnUpdate()
	{
		var t = Duration > 0f ? (Time.Now - _start) / Duration : 1f;
		if ( t >= 1f )
		{
			GameObject.Destroy();
			return;
		}

		try
		{
			if ( SplatOnGround && _hasSplatted )
			{
				// Already stuck to the ground - just fade out in place, no further motion.
				if ( _renderer is not null )
					_renderer.Tint = new Color( _baseTint.r, _baseTint.g, _baseTint.b, _baseTint.a * (1f - t) );
				return;
			}

			WorldPosition += Velocity * Time.Delta;
			Velocity -= Vector3.Up * (Gravity * Time.Delta);
			Velocity = Vector3.Lerp( Velocity, Vector3.Zero, System.Math.Clamp( Time.Delta * Drag, 0f, 1f ) );

			if ( SplatOnGround && WorldPosition.z <= GroundZ )
			{
				var pos = WorldPosition;
				pos.z = GroundZ;
				WorldPosition = pos;
				Velocity = Vector3.Zero;
				_hasSplatted = true;

				// Squash flat like a splatter instead of resting as a little cube on the floor.
				try { WorldScale = new Vector3( WorldScale.x * 1.5f, WorldScale.y * 1.5f, WorldScale.z * 0.2f ); }
				catch { }
			}

			if ( _renderer is not null )
				_renderer.Tint = new Color( _baseTint.r, _baseTint.g, _baseTint.b, _baseTint.a * (1f - t) );
		}
		catch { }
	}
}
