namespace DrunkenBarFight;

/// <summary>
/// Procedural traveling shockwave front for the Opening Slam finisher - a thin row of light
/// points that sweeps forward from the player at a fixed speed for a fixed duration, dropping a
/// short-lived light burst along its front edge every few frames, then destroys itself. Each light
/// traces down to the surface beneath it as the front advances, so the wave hugs uneven ground and
/// rides over steps/raised geometry instead of clipping through it. Purely cosmetic: PlayerCombat
/// drives the actual hit detection with the same WaveSpeed/Duration, so the visible front and the
/// damage never disagree.
/// </summary>
public class FxShockwave : Component
{
	public Vector3 StartPosition;
	public Vector3 Direction;
	public float Speed { get; set; } = 1000f;
	public float Duration { get; set; } = 0.32f;
	public float FrontWidth { get; set; } = 140f;
	public float ArcDegrees { get; set; } = 26f;
	public Color Tint { get; set; } = new Color( 1f, 0.8f, 0.3f );

	/// <summary>Height above the start position each light sits at when FollowGround is off.</summary>
	public float Height { get; set; } = 22f;
	/// <summary>When true (default), each light traces down to whatever surface is under it as the
	/// front travels, so the wave hugs uneven ground, steps and raised geometry instead of clipping
	/// through them (a light embedded in/under a surface stops reading as a visible glow).</summary>
	public bool FollowGround { get; set; } = true;
	/// <summary>How far above the traced surface each light floats while following the ground.</summary>
	public float GroundOffset { get; set; } = 8f;

	float _start;
	float _nextSpawnTime;

	protected override void OnStart()
	{
		_start = Time.Now;
		_nextSpawnTime = 0f;
	}

	protected override void OnUpdate()
	{
		var elapsed = Time.Now - _start;
		if ( elapsed > Duration )
		{
			GameObject.Destroy();
			return;
		}

		// Throttled to every few frames so the 0.3s wave only leaves a handful of front rows
		// behind instead of a per-frame GameObject storm.
		if ( elapsed < _nextSpawnTime )
			return;

		_nextSpawnTime = elapsed + 0.05f;

		// Only the forward travel is accumulated here - each light's height is resolved per point in
		// SpawnFrontRow (ground-following) so the wave can ride over uneven terrain.
		var center = StartPosition + Direction * (Speed * elapsed);
		SpawnFrontRow( center );
	}

	void SpawnFrontRow( Vector3 center )
	{
		var yaw = System.MathF.Atan2( Direction.y, Direction.x );
		var halfArc = (ArcDegrees * 0.5f) * (System.MathF.PI / 180f);
		const int points = 9;

		for ( int i = 0; i < points; i++ )
		{
			var t = (float)i / (points - 1);
			var a = yaw - halfArc + t * halfArc * 2f;
			var pos = center + new Vector3( System.MathF.Cos( a ), System.MathF.Sin( a ), 0 ) * (FrontWidth * 0.5f);

			pos.z = FollowGround
				? SampleGroundZ( pos, StartPosition.z + Height )
				: StartPosition.z + Height;

			var go = new GameObject( true, "FxShockwaveLight" ) { WorldPosition = pos };

			var light = go.Components.Create<PointLight>();
			light.LightColor = Tint;
			light.Radius = 32f;

			var fx = go.Components.Create<FxPointLight>();
			fx.Duration = 0.16f;
			fx.StartRadius = 32f;
		}
	}

	/// <summary>World Z of the first solid surface under this front point, so the wave sticks to the
	/// ground as it travels rather than punching through raised geometry. Enemies and this object's
	/// own lights are ignored so the row never climbs onto a body. Falls back to fallbackZ when
	/// nothing is hit (e.g. the front is over a gap).</summary>
	float SampleGroundZ( Vector3 at, float fallbackZ )
	{
		try
		{
			var from = new Vector3( at.x, at.y, StartPosition.z + 500f );
			var to = new Vector3( at.x, at.y, StartPosition.z - 500f );

			var trace = Scene.Trace.Ray( from, to ).IgnoreGameObject( GameObject );

			foreach ( var enemy in EnemyBase.All )
			{
				if ( enemy is not null && enemy.IsValid )
					trace = trace.IgnoreGameObject( enemy.GameObject );
			}

			var hit = trace.Run();
			if ( hit.Hit )
				return hit.HitPosition.z + GroundOffset;
		}
		catch { }

		return fallbackZ;
	}
}