using System;

namespace DrunkenBarFight;

/// <summary>
/// Spawns enemies around the arena perimeter on a ramping timer, mixes in tougher archetypes as
/// survival time increases, and guarantees a sobering enemy on a fixed cadence so the player is
/// never mathematically locked out of managing their drunkenness. No pathfinding, no director -
/// just a tunable ring spawn with a weighted archetype roll.
/// </summary>
public class HordeSpawner : Component
{
	public static HordeSpawner Instance { get; private set; }

	[Property, Group( "Arena" )] public float SpawnRingRadius { get; set; } = 700f;
	[Property, Group( "Arena" )] public float GroundZ { get; set; } = 0f;

	[Property, Group( "Ramp" )] public float StartSpawnInterval { get; set; } = 3.2f;
	[Property, Group( "Ramp" )] public float MinSpawnInterval { get; set; } = 0.9f;
	[Property, Group( "Ramp" )] public float SpawnIntervalRampSeconds { get; set; } = 180f;
	[Property, Group( "Ramp" )] public int MaxAliveEnemies { get; set; } = 18;

	[Property, Group( "Mix" )] public float BruiserUnlockTime { get; set; } = 45f;
	[Property, Group( "Mix" )] public float SoberingChance { get; set; } = 0.15f;
	[Property, Group( "Mix" )] public float BruiserChance { get; set; } = 0.2f;
	[Property, Group( "Mix" )] public float GuaranteedSoberingInterval { get; set; } = 20f;

	float _timeSinceSpawn;
	float _timeSinceGuaranteedSobering;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		_timeSinceSpawn += Time.Delta;
		_timeSinceGuaranteedSobering += Time.Delta;

		var survivalTime = GameManager.Instance.SurvivalTime;
		var rampT = Math.Clamp( survivalTime / SpawnIntervalRampSeconds, 0f, 1f );
		var currentInterval = StartSpawnInterval + (MinSpawnInterval - StartSpawnInterval) * rampT;

		if ( _timeSinceGuaranteedSobering >= GuaranteedSoberingInterval )
		{
			_timeSinceGuaranteedSobering = 0f;
			if ( EnemyBase.All.Count < MaxAliveEnemies )
				SpawnSpecific<EnemySoberingBartender>();
		}
		else if ( _timeSinceSpawn >= currentInterval )
		{
			_timeSinceSpawn = 0f;
			if ( EnemyBase.All.Count < MaxAliveEnemies )
				SpawnWeighted( survivalTime );
		}
	}

	void SpawnWeighted( float survivalTime )
	{
		var bruiserUnlocked = survivalTime >= BruiserUnlockTime;
		var roll = Random.Shared.NextSingle();

		if ( roll < SoberingChance )
			SpawnSpecific<EnemySoberingBartender>();
		else if ( bruiserUnlocked && roll < SoberingChance + BruiserChance )
			SpawnSpecific<EnemyBruiser>();
		else
			SpawnSpecific<EnemyBrawler>();
	}

	void SpawnSpecific<T>() where T : EnemyBase, new()
	{
		var angle = Random.Shared.NextSingle() * MathF.PI * 2f;
		var pos = new Vector3( MathF.Cos( angle ) * SpawnRingRadius, MathF.Sin( angle ) * SpawnRingRadius, GroundZ );

		var go = new GameObject
		{
			Name = typeof( T ).Name,
			WorldPosition = pos,
		};
		go.Tags.Add( "enemy" );

		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );

		var animHelper = go.AddComponent<Sandbox.Citizen.CitizenAnimationHelper>();
		animHelper.Target = renderer;

		var enemy = go.AddComponent<T>();
		enemy.BodyRenderer = renderer;
		enemy.AnimHelper = animHelper;
		enemy.ApplyVisual();
	}

	public void ResetRun()
	{
		_timeSinceSpawn = 0f;
		_timeSinceGuaranteedSobering = 0f;
	}
}
