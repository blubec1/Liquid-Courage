using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Shared brain for every enemy archetype: a self-registering list (used by HitDetector instead
/// of physics queries), simple kinematic chase-and-attack AI, knockback, and the freeze-frame
/// stagger that doubles as our hit-stop. Archetypes only override SetDefaults() and, optionally,
/// ResolveAttack()/OnKilled() for special behaviour.
/// </summary>
public abstract class EnemyBase : Component
{
	public static readonly List<EnemyBase> All = new();

	[Property, Group( "Stats" )] public float MaxHP { get; set; } = 30f;
	[Property, Group( "Stats" )] public float MoveSpeed { get; set; } = 140f;
	[Property, Group( "Stats" )] public float Damage { get; set; } = 6f;
	[Property, Group( "Stats" )] public float AttackRange { get; set; } = 55f;
	[Property, Group( "Stats" )] public float AttackCooldown { get; set; } = 1.2f;
	[Property, Group( "Stats" )] public float AttackWindup { get; set; } = 0f;
	[Property, Group( "Stats" )] public bool IsSoberingEnemy { get; set; } = false;
	[Property, Group( "Stats" )] public float SoberingAmount { get; set; } = 18f;

	[Property, Group( "Time Scaling" )] public float HpRampPerSecond { get; set; } = 0.006f;
	[Property, Group( "Time Scaling" )] public float DamageRampPerSecond { get; set; } = 0.004f;

	[Property] public Sandbox.Citizen.CitizenAnimationHelper AnimHelper { get; set; }
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }

	public float CurrentHP { get; private set; }
	public bool IsDead { get; private set; }
	public float FreezeUntil { get; private set; }

	float _spawnZ;
	float _nextAttackReady;
	bool _isWindingUp;
	float _windupEndTime;
	Vector3 _knockbackVelocity;
	bool _pendingWasFinisher;
	bool _pendingWasEnvironmental;
	float _deathStartTime = -1f;
	const float DeathVisualDuration = 0.25f;

	protected virtual void SetDefaults() { }

	/// <summary>Called by the spawner once BodyRenderer is assigned, so archetypes can tint/scale themselves.</summary>
	public virtual void ApplyVisual() { }

	/// <summary>
	/// Called by HordeSpawner right after spawn with the current run's survival time, so the
	/// horde keeps getting tougher the longer a run goes - independent of the spawn-rate/archetype
	/// ramp HordeSpawner already does. Purely multiplicative on top of the archetype's own stats.
	/// </summary>
	public void ApplyTimeScaling( float survivalTime )
	{
		var t = System.MathF.Max( 0f, survivalTime );
		MaxHP *= 1f + t * HpRampPerSecond;
		Damage *= 1f + t * DamageRampPerSecond;
		CurrentHP = MaxHP;
	}

	/// <summary>Freezes this enemy in place for the given duration - reuses the same field the hit-stagger
	/// system already uses. Used by DrinkMeter so the whole crowd visibly pauses during a drink.</summary>
	public void SetFreeze( float duration )
	{
		FreezeUntil = System.MathF.Max( FreezeUntil, Time.Now + duration );
	}

	protected override void OnAwake()
	{
		SetDefaults();
		CurrentHP = MaxHP;
		_spawnZ = WorldPosition.z;

		// Registered here (not OnStart) so an enemy spawned at runtime is immediately visible
		// to HitDetector/ComputeSeparation the same frame it's created.
		All.Add( this );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
	}

	protected override void OnUpdate()
	{
		// Knockback slide always plays, even mid-stagger - it's part of the impact feedback.
		if ( _knockbackVelocity.Length > 1f )
		{
			var pos = WorldPosition + _knockbackVelocity * Time.Delta;
			pos = new Vector3( pos.x, pos.y, _spawnZ );
			WorldPosition = pos;
			_knockbackVelocity = Vector3.Lerp( _knockbackVelocity, Vector3.Zero, System.Math.Clamp( Time.Delta * 9f, 0f, 1f ) );
		}

		if ( IsDead )
		{
			UpdateDeathVisual();
			return;
		}

		if ( AnimHelper is not null )
		{
			AnimHelper.WithVelocity( _knockbackVelocity );
			AnimHelper.WithWishVelocity( _knockbackVelocity );
			AnimHelper.IsGrounded = true;
		}

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		if ( Time.Now < FreezeUntil )
			return;

		UpdateAi();
	}

	void UpdateAi()
	{
		var player = PlayerStats.Local;
		if ( player is null || player.IsDead )
			return;

		if ( _isWindingUp )
		{
			if ( Time.Now >= _windupEndTime )
			{
				_isWindingUp = false;
				ResolveAttack();
				_nextAttackReady = Time.Now + AttackCooldown;
			}
			return;
		}

		var toPlayer = player.WorldPosition - WorldPosition;
		toPlayer = new Vector3( toPlayer.x, toPlayer.y, 0 );
		var dist = toPlayer.Length;

		if ( dist <= AttackRange && Time.Now >= _nextAttackReady )
		{
			FacePoint( player.WorldPosition );

			if ( AttackWindup > 0f )
			{
				_isWindingUp = true;
				_windupEndTime = Time.Now + AttackWindup;
			}
			else
			{
				ResolveAttack();
				_nextAttackReady = Time.Now + AttackCooldown;
			}
			return;
		}

		if ( dist > AttackRange * 0.85f )
		{
			var dir = dist > 0.01f ? toPlayer / dist : Vector3.Forward;
			var separation = ComputeSeparation();
			var moveDir = (dir + separation).Length > 0.01f ? (dir + separation) : dir;
			moveDir = moveDir.Length > 0.01f ? moveDir / moveDir.Length : dir;

			var newPos = WorldPosition + moveDir * MoveSpeed * Time.Delta;
			WorldPosition = new Vector3( newPos.x, newPos.y, _spawnZ );
			FacePoint( player.WorldPosition );

			if ( AnimHelper is not null )
			{
				var vel = moveDir * MoveSpeed;
				AnimHelper.WithVelocity( vel );
				AnimHelper.WithWishVelocity( vel );
			}
		}
	}

	Vector3 ComputeSeparation()
	{
		var push = Vector3.Zero;
		foreach ( var other in All )
		{
			if ( other == this || other is null || other.IsDead )
				continue;

			var away = WorldPosition - other.WorldPosition;
			away = new Vector3( away.x, away.y, 0 );
			var d = away.Length;
			if ( d > 0.01f && d < 40f )
				push += (away / d) * (40f - d) * 0.05f;
		}
		return push;
	}

	void FacePoint( Vector3 point )
	{
		var dir = point - WorldPosition;
		dir = new Vector3( dir.x, dir.y, 0 );
		if ( dir.Length < 0.01f )
			return;

		var yaw = System.MathF.Atan2( dir.y, dir.x ) * (180f / System.MathF.PI);
		WorldRotation = Rotation.Slerp( WorldRotation, Rotation.FromYaw( yaw ), System.Math.Clamp( Time.Delta * 10f, 0f, 1f ) );
	}

	/// <summary>Override for archetype-specific attack resolution (default: flat damage if still in range).</summary>
	protected virtual void ResolveAttack()
	{
		var player = PlayerStats.Local;
		if ( player is null )
			return;

		var dist = new Vector3( player.WorldPosition.x - WorldPosition.x, player.WorldPosition.y - WorldPosition.y, 0 ).Length;
		if ( dist <= AttackRange * 1.3f )
			player.TakeDamage( Damage );
	}

	/// <summary>Called by combat code when this enemy is hit by an attack or finisher.</summary>
	public void ApplyHit( float damage, Vector3 knockback, float staggerTime, bool fromFinisher = false, bool fromEnvironmental = false )
	{
		if ( IsDead )
			return;

		CurrentHP -= damage;
		FreezeUntil = System.MathF.Max( FreezeUntil, Time.Now + staggerTime );
		_knockbackVelocity += knockback;
		_pendingWasFinisher |= fromFinisher;
		_pendingWasEnvironmental |= fromEnvironmental;

		if ( CurrentHP <= 0 )
			Die();
	}

	void Die()
	{
		if ( IsDead )
			return;

		IsDead = true;
		_deathStartTime = Time.Now;

		var info = new EnemyKillInfo
		{
			Enemy = this,
			WasFinisher = _pendingWasFinisher,
			WasEnvironmental = _pendingWasEnvironmental,
			WasSobering = IsSoberingEnemy,
			MultiKillIndex = MultiKillTracker.RegisterKillAndGetIndex(),
		};

		if ( IsSoberingEnemy )
			DrunkennessSystem.Local?.Reduce( SoberingAmount );

		GameEvents.RaiseEnemyKilled( info );
	}

	void UpdateDeathVisual()
	{
		var t = System.Math.Clamp( (Time.Now - _deathStartTime) / DeathVisualDuration, 0f, 1f );
		WorldScale = Vector3.One * (1f - t);

		if ( t >= 1f )
			GameObject.Destroy();
	}
}
