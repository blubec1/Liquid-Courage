namespace DrunkenBarFight;

/// <summary>
/// One "dizzy star" circling above a knocked-out enemy's head - purely procedural motion
/// (no particle system needed), paired with a shrinking PointLight for the actual visual.
/// </summary>
public class FxOrbitStar : Component
{
	public Vector3 Center { get; set; }
	public float Radius { get; set; } = 20f;
	public float Speed { get; set; } = 5f;
	public float PhaseOffset { get; set; }
	public float Duration { get; set; } = 1f;
	public float RiseSpeed { get; set; } = 24f;

	float _start;
	PointLight _light;

	protected override void OnAwake()
	{
		_start = Time.Now;
		_light = Components.Get<PointLight>();
	}

	protected override void OnUpdate()
	{
		var t = Duration > 0f ? (Time.Now - _start) / Duration : 1f;
		if ( t >= 1f )
		{
			GameObject.Destroy();
			return;
		}

		var angle = PhaseOffset + Time.Now * Speed;
		var pos = Center + new Vector3(
			System.MathF.Cos( angle ) * Radius,
			System.MathF.Sin( angle ) * Radius,
			t * RiseSpeed );

		WorldPosition = pos;

		try
		{
			if ( _light is not null )
				_light.Radius = 18f * (1f - t);
		}
		catch { }
	}
}
