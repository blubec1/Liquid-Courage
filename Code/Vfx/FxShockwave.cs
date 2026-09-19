namespace DrunkenBarFight;

/// <summary>
/// Procedural traveling shockwave front for the Opening Slam finisher - a thin row of light
/// points that sweeps forward from the player at a fixed speed for a fixed duration, dropping a
/// short-lived light burst along its front edge every few frames, then destroys itself. Purely
/// cosmetic: PlayerCombat drives the actual hit detection with the same WaveSpeed/Duration, so
/// the visible front and the damage never disagree.
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

		var center = StartPosition + Direction * (Speed * elapsed) + Vector3.Up * 40f;
		SpawnFrontRow( center );
	}

	void SpawnFrontRow( Vector3 center )
	{
		var yaw = System.MathF.Atan2( Direction.y, Direction.x );
		var halfArc = (ArcDegrees * 0.5f) * (System.MathF.PI / 180f);
		const int points = 5;

		for ( int i = 0; i < points; i++ )
		{
			var t = (float)i / (points - 1);
			var a = yaw - halfArc + t * halfArc * 2f;
			var pos = center + new Vector3( System.MathF.Cos( a ), System.MathF.Sin( a ), 0 ) * (FrontWidth * 0.5f);

			var go = new GameObject( true, "FxShockwaveLight" ) { WorldPosition = pos };

			var light = go.Components.Create<PointLight>();
			light.LightColor = Tint;
			light.Radius = 22f;

			var fx = go.Components.Create<FxPointLight>();
			fx.Duration = 0.12f;
			fx.StartRadius = 22f;
		}
	}
}