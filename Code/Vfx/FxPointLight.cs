namespace DrunkenBarFight;

/// <summary>
/// Self-destructing light burst - the building block for every code-only VFX in this project.
/// s&box's real particle system (ParticleEffect/ParticleSphereEmitter etc.) needs prefab assets
/// and has serialized property types (ParticleFloat/ParticleGradient) that don't set reliably from
/// code, so instead of fighting that, all "vfx" here is a short-lived PointLight that shrinks to
/// nothing over Duration seconds and destroys itself. Cheap, dependency-free, and guaranteed to
/// render (see PointLight being the community-verified pattern for runtime-only visuals).
/// </summary>
public class FxPointLight : Component
{
	public float Duration { get; set; } = 0.2f;
	public float StartRadius { get; set; } = 80f;

	/// <summary>Optional drift - used for knockback sparks / rising embers. Zero = stays put.</summary>
	public Vector3 Velocity { get; set; }
	public float Drag { get; set; } = 4f;

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

		try
		{
			if ( _light is not null )
				_light.Radius = StartRadius * (1f - t);
		}
		catch { }

		if ( Velocity.Length > 0.5f )
		{
			WorldPosition += Velocity * Time.Delta;
			Velocity = Vector3.Lerp( Velocity, Vector3.Zero, System.Math.Clamp( Time.Delta * Drag, 0f, 1f ) );
		}
	}
}
