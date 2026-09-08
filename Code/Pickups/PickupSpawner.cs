using System;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>Periodically spawns sobering pickups somewhere reachable in the arena.</summary>
public class PickupSpawner : Component
{
	public static PickupSpawner Instance { get; private set; }

	[Property] public float SpawnInterval { get; set; } = 14f;
	[Property] public float IntervalJitter { get; set; } = 4f;
	[Property] public int MaxConcurrentPickups { get; set; } = 2;
	[Property] public float SpawnAreaRadius { get; set; } = 480f;
	[Property] public float GroundZ { get; set; } = 0f;

	float _nextSpawnTime;

	protected override void OnAwake()
	{
		Instance = this;
		_nextSpawnTime = SpawnInterval;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		var runTime = GameManager.Instance.SurvivalTime;
		if ( runTime < _nextSpawnTime )
			return;

		if ( Scene.GetAllComponents<SoberingPickup>().Count() >= MaxConcurrentPickups )
		{
			_nextSpawnTime = runTime + 2f;
			return;
		}

		SpawnPickup();
		_nextSpawnTime = runTime + SpawnInterval + (Random.Shared.NextSingle() - 0.5f) * 2f * IntervalJitter;
	}

	void SpawnPickup()
	{
		// Uniform sampling over a disk, not just a random radius, so pickups don't cluster near the center.
		var r = SpawnAreaRadius * MathF.Sqrt( Random.Shared.NextSingle() );
		var angle = Random.Shared.NextSingle() * MathF.PI * 2f;
		var pos = new Vector3( MathF.Cos( angle ) * r, MathF.Sin( angle ) * r, GroundZ );

		var go = new GameObject
		{
			Name = "SoberingPickup",
			WorldPosition = pos,
		};
		go.Tags.Add( "pickup" );

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = new Color( 0.3f, 0.9f, 0.95f, 1f );
		go.WorldScale = Vector3.One * 0.18f;

		go.AddComponent<SoberingPickup>();
	}

	public void ResetRun()
	{
		_nextSpawnTime = SpawnInterval;
	}
}
