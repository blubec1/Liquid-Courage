using System;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Spawns enemies around the arena perimeter (or at hand-placed EnemySpawnPoint markers, if any exist
/// in the scene) on a ramping timer and mixes in tougher archetypes as survival time increases.
///
/// Each enemy gets a real Rigidbody with gravity ON at spawn, so it physically drops from wherever its
/// spawn point/ring position sits down to the actual floor instead of freezing at the spawn marker's
/// height (see EnemyBase.DriveFallToGround) - once it settles, physics is switched off and normal
/// kinematic AI movement takes over from that landed height.
///
/// Each enemy also gets a NavMeshAgent so it routes around obstacles/buildings (assumes the scene's
/// NavMesh has already been baked by hand in the editor) instead of phasing straight through them.
/// An earlier version of this pathed the agent straight to the player's exact position, which had no
/// notion of attack range and caused enemies to slide into/through the player - EnemyBase.UpdateAi now
/// targets a point MinDistanceFromPlayer short of the player instead, and hard-clamps the final
/// position every frame as a safety net. Falls back to plain direct-line-plus-separation movement if
/// the agent is missing or throws.
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
	[Property, Group( "Mix" )] public float BruiserChance { get; set; } = 0.2f;

	float _timeSinceSpawn;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		_timeSinceSpawn += Time.Delta;

		var survivalTime = GameManager.Instance.SurvivalTime;
		var rampT = Math.Clamp( survivalTime / SpawnIntervalRampSeconds, 0f, 1f );
		var currentInterval = StartSpawnInterval + (MinSpawnInterval - StartSpawnInterval) * rampT;

		if ( _timeSinceSpawn >= currentInterval )
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

		if ( bruiserUnlocked && roll < BruiserChance )
			SpawnSpecific<EnemyBruiser>();
		else
			SpawnSpecific<EnemyBrawler>();
	}

	/// <summary>Picks a random EnemySpawnPoint's WorldPosition if any exist in the scene (placed by
	/// hand in the editor), otherwise falls back to the original random-point-on-a-ring placement so
	/// nothing breaks for scenes that never add any spawn points.</summary>
	Vector3 GetSpawnPosition()
	{
		var points = Scene.GetAllComponents<EnemySpawnPoint>().ToList();
		if ( points.Count > 0 )
			return points[ Random.Shared.Next( points.Count ) ].WorldPosition;

		var angle = Random.Shared.NextSingle() * MathF.PI * 2f;
		return new Vector3( MathF.Cos( angle ) * SpawnRingRadius, MathF.Sin( angle ) * SpawnRingRadius, GroundZ );
	}

	void SpawnSpecific<T>() where T : EnemyBase, new()
	{
		var pos = GetSpawnPosition();

		var go = new GameObject
		{
			Name = typeof( T ).Name,
			WorldPosition = pos,
		};
		go.Tags.Add( "enemy" );

		var renderer = go.AddComponent<SkinnedModelRenderer>();
		// Must be the same custom retargeted model the player uses, NOT the plain stock citizen -
		// "Punching_1"/"Hook_Punch_2" (played by EnemyBase's attack bone-blend) only exist as
		// sequences on animations/retargeted/player_animations.vmdl. Loading plain citizen.vmdl here
		// meant the hidden sample model couldn't find those sequences and just sat in bind pose
		// (T-pose), which then got blended onto the real body every time an enemy attacked. Same
		// skeleton either way (this model is built on top of citizen's), so locomotion/tint/scale
		// all still work identically.
		renderer.Model = Model.Load( "animations/retargeted/player_animations.vmdl" );

		// Without a collider here the player's CharacterController has nothing to push against
		// and just walks straight through enemies. Using the citizen model's own collision hull
		// (same model that ragdoll/ModelPhysics relies on) rather than guessing at capsule/sphere
		// dimensions by hand.
		try
		{
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = renderer.Model;
		}
		catch { }

		// Gravity ON at spawn so the enemy physically falls from its spawn point down to the real
		// floor (see EnemyBase.DriveFallToGround, which switches MotionEnabled off once it settles
		// and hands control back to the normal kinematic AI movement). Without this, enemies just
		// floated exactly at whatever height the spawn point/ring sits at.
		try
		{
			var rigidbody = go.AddComponent<Rigidbody>();
			rigidbody.MotionEnabled = true;
			rigidbody.Gravity = true;
		}
		catch { }

		// Best-effort NavMesh pathfinding to the player around obstacles - only used once
		// DriveFallToGround marks the enemy as landed (see EnemyBase.UpdateAi), so it never fights
		// the physics-driven fall above. Falls back to direct-line movement if this is missing/throws.
		try
		{
			var agent = go.AddComponent<NavMeshAgent>();
			agent.Radius = 16f;
			agent.Height = 64f;
			agent.MaxSpeed = EnemyBase.MaxEnemyMoveSpeed;
			agent.Acceleration = 900f;
		}
		catch { }

		var animHelper = go.AddComponent<Sandbox.Citizen.CitizenAnimationHelper>();
		animHelper.Target = renderer;

		var enemy = go.AddComponent<T>();
		enemy.BodyRenderer = renderer;
		enemy.AnimHelper = animHelper;
		enemy.ApplyTimeScaling( GameManager.Instance?.SurvivalTime ?? 0f );
		enemy.ApplyVisual();
	}

	public void ResetRun()
	{
		_timeSinceSpawn = 0f;
	}
}
