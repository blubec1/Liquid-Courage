using System;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Reads attack input, applies cooldowns (scaled by drunkenness), asks AttackStringSystem
/// whether this completes a finisher, and if not performs the plain attack via HitDetector.
/// This is the glue between input and all the data-driven combat systems - it holds no combat
/// tuning values itself.
/// </summary>
public class PlayerCombat : Component
{
	public static PlayerCombat Local { get; private set; }

	float _nextReadyTime;
	float _lastCooldownDuration = 0.0001f;
	AttackId? _lastPerformedAttack;

	/// <summary>0 = just used an attack, 1 = ready to attack again. Drives the HUD cooldown bar under the crosshair.</summary>
	public float CooldownFraction01
	{
		get
		{
			if ( _lastCooldownDuration <= 0 )
				return 1f;

			return Math.Clamp( 1f - (_nextReadyTime - Time.Now) / _lastCooldownDuration, 0f, 1f );
		}
	}

	public bool IsReady => Time.Now >= _nextReadyTime;

	protected override void OnAwake()
	{
		Local = this;
	}

	protected override void OnUpdate()
	{
		if ( PlayerStats.Local?.IsDead == true )
			return;

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		// Player is mid-"chug" - see DrinkMeter. Input is paused for the same beat every enemy freezes.
		if ( DrinkMeter.Local?.IsDrinking == true )
			return;

		if ( !IsReady )
			return;

		// LMB = light attack, RMB = heavy attack, E = leg sweep (90 degree arc kick).
		if ( Input.Pressed( "Punch" ) )
			TryAttack( AttackId.Punch );
		else if ( Input.Pressed( "Heavy" ) )
			TryAttack( AttackId.Heavy );
		else if ( Input.Pressed( "Kick" ) )
			TryAttack( AttackId.Kick );
	}

	void TryAttack( AttackId id )
	{
		var def = AttackLibrary.Get( id );
		var cooldownMult = DrunkennessSystem.Local?.AttackCooldownMultiplier ?? 1f;

		_lastCooldownDuration = MathF.Max( 0.05f, def.Cooldown * cooldownMult );
		_nextReadyTime = Time.Now + _lastCooldownDuration;

		var wasVaried = _lastPerformedAttack is null || _lastPerformedAttack.Value != id;
		GameEvents.RaiseAttackPerformed( id, wasVaried );
		_lastPerformedAttack = id;

		var finisher = AttackStringSystem.Local?.RecordAndCheck( id );
		if ( finisher is not null )
		{
			PerformFinisher( finisher );
			return;
		}

		PerformBasicAttack( def );
	}

	void PerformBasicAttack( AttackDefinition def )
	{
		var origin = WorldPosition;
		var facing = WorldRotation.Forward;

		var hits = HitDetector.FindEnemies( origin, facing, def.Range, def.ArcDegrees );
		var damageMult = DrunkennessSystem.Local?.DamageMultiplier ?? 1f;

		foreach ( var enemy in hits )
		{
			var dir = enemy.WorldPosition - origin;
			dir = new Vector3( dir.x, dir.y, 0 );
			dir = dir.Length > 0.01f ? dir / dir.Length : facing;

			enemy.ApplyHit( def.Damage * damageMult, dir * def.Knockback, def.StaggerTime );
		}

		HitFeedback.PlayAttackImpact( def.ImpactStrength, hits.Count );
		PlayerAnimationDriver.Local?.PlayAttackSwing( def );

		// Combo now lives or dies purely on whether you're actually connecting - a whiff never
		// extends it, no matter how varied your attacks are (see ComboSystem).
		if ( hits.Count > 0 )
			ComboSystem.Local?.RegisterHit( hits.Count );
	}

	void PerformFinisher( FinisherDefinition finisher )
	{
		var origin = WorldPosition;
		var facing = WorldRotation.Forward;

		var hits = HitDetector.FindEnemies( origin, facing, finisher.Range, finisher.ArcDegrees );
		var damageMult = DrunkennessSystem.Local?.DamageMultiplier ?? 1f;

		foreach ( var enemy in hits )
		{
			var dir = enemy.WorldPosition - origin;
			dir = new Vector3( dir.x, dir.y, 0 );
			dir = dir.Length > 0.01f ? dir / dir.Length : facing;

			enemy.ApplyHit( finisher.Damage * damageMult, dir * finisher.Knockback, finisher.StaggerTime, fromFinisher: true );
		}

		HitFeedback.PlayFinisherImpact( finisher.ImpactStrength, hits.Count );
		PlayerAnimationDriver.Local?.PlayFinisher( finisher );

		if ( hits.Count > 0 )
			ComboSystem.Local?.RegisterHit( hits.Count );

		GameEvents.RaiseFinisherExecuted( finisher, hits.Count );
	}
}
