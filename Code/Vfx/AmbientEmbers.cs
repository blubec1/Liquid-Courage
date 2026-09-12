using System;

namespace DrunkenBarFight;

/// <summary>
/// Pure atmosphere: occasionally spawns a slow-rising, fading ember somewhere within a radius of
/// this GameObject, using the same FxPointLight building block as combat VFX. Drop one of these
/// anywhere in the arena for a bit of "dusty bar" ambience - purely decorative, no gameplay effect.
/// </summary>
public class AmbientEmbers : Component
{
	[Property] public float Radius { get; set; } = 500f;
	[Property] public float GroundZ { get; set; } = 0f;
	[Property] public float SpawnInterval { get; set; } = 0.6f;
	[Property] public float EmberDuration { get; set; } = 3.5f;
	[Property] public float RiseSpeed { get; set; } = 26f;

	float _timeSinceSpawn;

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		_timeSinceSpawn += Time.Delta;
		if ( _timeSinceSpawn < SpawnInterval )
			return;

		_timeSinceSpawn = 0f;

		try
		{
			var angle = Random.Shared.NextSingle() * MathF.PI * 2f;
			var dist = Random.Shared.NextSingle() * Radius;
			// Spawned a bit above the floor with a small radius, so these read as tiny drifting
			// embers rather than glowing patches projected onto the ground.
			var pos = WorldPosition + new Vector3( MathF.Cos( angle ) * dist, MathF.Sin( angle ) * dist, GroundZ + 24f );

			var go = new GameObject( true, "FxEmber" ) { WorldPosition = pos };

			var light = go.Components.Create<PointLight>();
			light.LightColor = new Color( 1f, 0.75f, 0.4f );
			light.Radius = 8f;

			var fx = go.Components.Create<FxPointLight>();
			fx.Duration = EmberDuration;
			fx.StartRadius = 8f;
			fx.Velocity = Vector3.Up * RiseSpeed + RandomJitter();
			fx.Drag = 0.3f;
		}
		catch { }
	}

	static Vector3 RandomJitter()
	{
		return new Vector3(
			(Random.Shared.NextSingle() - 0.5f) * 8f,
			(Random.Shared.NextSingle() - 0.5f) * 8f,
			0f );
	}
}
