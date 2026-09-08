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
		var punch = Input.Pressed( "Punch" );
		var kick = Input.Pressed( "Kick" );
		var heavy = Input.Pressed( "Heavy" );

		// TEMPORARY DIAGNOSTIC - remove once attacks are confirmed working. This logs every time
		// a raw input action fires, *before* any of our own gating - if this never prints when you
		// click, the problem is upstream of our code entirely (input focus/capture, binding, etc).
		// If it DOES print but nothing happens in-game, the printed state tells us which guard is
		// eating the attack.
		if ( punch || kick || heavy )
		{
			Log.Info( $"[PlayerCombat] input fired - Punch={punch} Kick={kick} Heavy={heavy} " +
				$"| IsReady={IsReady} IsDead={PlayerStats.Local?.IsDead} " +
				$"State={GameManager.Instance?.State} HasLocal={PlayerCombat.Local == this}" );
		}

		if ( PlayerStats.Local?.IsDead == true )
			return;

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		if ( !IsReady )
			return;

		if ( punch )
			TryAttack( AttackId.Punch );
		else if ( kick )
			TryAttack( AttackId.Kick );
		else if ( heavy )
			TryAttack( AttackId.Heavy );
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

		PerformBasicAttack( def, wasVaried );
	}

	void PerformBasicAttack( AttackDefinition def, bool wasVaried )
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

		var varietyMult = wasVaried ? 1f : 0.45f;
		var connectMult = hits.Count > 0 ? 1f : 0.6f;
		var comboAdd = def.ComboValue * varietyMult * connectMult;
		ComboSystem.Local?.AddCombo( comboAdd );

		// TEMPORARY DIAGNOSTIC - see note in OnUpdate. If "input fired" logs but this line never
		// does, something between the two threw - check the console for a red exception line.
		Log.Info( $"[PlayerCombat] {def.Name} completed - hits={hits.Count} comboAdd={comboAdd:0.0} " +
			$"comboNow={ComboSystem.Local?.Value:0.0} hasComboSystem={ComboSystem.Local is not null}" );
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

		ComboSystem.Local?.AddCombo( 25f );
		GameEvents.RaiseFinisherExecuted( finisher, hits.Count );
	}
}
