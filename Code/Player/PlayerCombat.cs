using System;
using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Reads attack input, applies cooldowns (scaled by drunkenness), asks AttackStringSystem
/// whether this completes a finisher, and if not performs the plain attack via HitDetector.
/// This is the glue between input and all the data-driven combat systems - it holds no combat
/// tuning values itself.
///
/// Finishers come in two flavours driven from FinisherDefinition.Behavior: the original instant
/// burst (hit everything in the arc once) and the timed behaviors (AoeSpinPulses / LineShockwave /
/// RapidCombo), which are driven to completion by a small per-frame state machine here. While a
/// timed finisher is running the player is pinned for its full Duration - the finisher lock is
/// just a cooldown - so an in-flight finisher can never be overlapped by another.
/// </summary>
public class PlayerCombat : Component
{
	public static PlayerCombat Local { get; private set; }

	float _nextReadyTime;
	float _lastCooldownDuration = 0.0001f;
	AttackId? _lastPerformedAttack;
	float _movementDisableEndTime;

	// --- Timed finisher state (AoeSpinPulses / LineShockwave / RapidCombo) ---
	FinisherDefinition _activeFinisher;
	float _finisherStartTime;
	float _finisherEndTime;
	int _nextTickIndex;
	float _finisherDamageMult;
	bool _shockwaveFinished;
	bool _shockwaveVfxSpawned;
	bool _shockwaveImpactPlayed;
	Vector3 _shockwaveOrigin;
	Vector3 _shockwaveFacing;
	readonly HashSet<EnemyBase> _finisherHitSet = new();

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

	public bool IsMovementFrozen => Time.Now < _movementDisableEndTime;

	protected override void OnAwake()
	{
		Local = this;
	}

	protected override void OnUpdate()
	{
		if ( PlayerStats.Local?.IsDead == true )
		{
			// Any in-flight finisher dies with the player - stop ticking it.
			ClearActiveFinisher();
			return;
		}

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
		{
			ClearActiveFinisher();
			return;
		}

		// Player is mid-"chug" - see DrinkMeter. Input is paused for the same beat every enemy
		// freezes, so elide the finisher driver too (don't clear it): it resumes cleanly after the
		// drink instead of skipping beats or firing into the enemy freeze.
		if ( DrinkMeter.Local?.IsDrinking == true )
			return;

		DriveActiveFinisher();

		// A timed finisher pins the player until its scheduled end - no new attacks or finishers
		// can overlap the one currently playing.
		if ( _activeFinisher is not null )
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

		// Attack-string tracking (for finisher recognition) still runs on every press, regardless
		// of whether it lands - a finisher is a sequence of inputs, not a sequence of connects.
		var finisher = AttackStringSystem.Local?.RecordAndCheck( id );
		if ( finisher is not null )
		{
			PerformFinisher( finisher );
			return;
		}

		PerformBasicAttack( def, id );
	}

	void PerformBasicAttack( AttackDefinition def, AttackId id )
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

		if ( def.RadialPushRadius > 0f )
			ApplyRadialPush( origin, def, hits );

		HitFeedback.PlayAttackImpact( def.ImpactStrength, hits.Count, def.ImpactSoundOverride );
		HitFeedback.PlaySwing( def.SwingSound );
		PlayerAnimationDriver.Local?.PlayAttackSwing( def );
		Vfx.AttackArc( origin, facing, def.ArcDegrees, def.Range, new Color( 0.9f, 0.95f, 1f ) );

		if ( def.DisableMovement )
		{
			var duration = def.AnimationDuration > 0f ? def.AnimationDuration : System.MathF.Max( 0.1f, def.Recovery + 0.1f );
			_movementDisableEndTime = Time.Now + duration;
		}

		if ( hits.Count > 0 )
		{
			ComboSystem.Local?.RegisterHit( hits.Count );
			RaiseAttackPerformed( id );
		}
	}

	/// <summary>
	/// See AttackDefinition.RadialPushRadius. Finds every living enemy within that radius (a full
	/// circle around the player, not the attack's own facing arc - reuses HitDetector.FindEnemies
	/// with arcDegrees=360, which it already treats as "ignore facing entirely") and shoves anyone
	/// not already handled by the normal arc-hit loop straight away from the player. Zero damage -
	/// this is knockback-only, so it can't unfairly hit enemies the player never actually swung at.
	/// </summary>
	void ApplyRadialPush( Vector3 origin, AttackDefinition def, List<EnemyBase> alreadyHit )
	{
		var nearby = HitDetector.FindEnemies( origin, Vector3.Forward, def.RadialPushRadius, 360f );

		foreach ( var enemy in nearby )
		{
			if ( alreadyHit.Contains( enemy ) )
				continue;

			var dir = enemy.WorldPosition - origin;
			dir = new Vector3( dir.x, dir.y, 0 );
			if ( dir.Length <= 0.01f )
				continue;

			dir /= dir.Length;
			enemy.ApplyHit( 0f, dir * def.Knockback, def.StaggerTime );
		}
	}

	void PerformFinisher( FinisherDefinition finisher )
	{
		var origin = WorldPosition;
		var facing = WorldRotation.Forward;

		if ( finisher.Behavior == FinisherBehavior.InstantBurst )
		{
			PerformInstantBurst( finisher, origin, facing );
			return;
		}

		StartTimedFinisher( finisher, origin, facing );
	}

	/// <summary>The original single-hit finisher - one burst arc of damage, feedback, and score.</summary>
	void PerformInstantBurst( FinisherDefinition finisher, Vector3 origin, Vector3 facing )
	{
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
		HitFeedback.PlaySwing( finisher.SwingSound );
		PlayerAnimationDriver.Local?.PlayFinisher( finisher );
		Vfx.AttackArc( origin, facing, finisher.ArcDegrees, finisher.Range, new Color( 1f, 0.8f, 0.3f ) );

		if ( hits.Count > 0 )
			Vfx.FinisherBurst( origin + facing * (finisher.Range * 0.6f) + Vector3.Up * 55f );

		if ( finisher.DisableMovement )
			_movementDisableEndTime = Time.Now + finisher.Duration;

		if ( hits.Count > 0 )
		{
			ComboSystem.Local?.RegisterHit( hits.Count );
			RaiseAttackPerformed( finisher.Sequence[finisher.Sequence.Length - 1] );
			GameEvents.RaiseFinisherExecuted( finisher, hits.Count );
		}
	}

	// ---------------------------------------------------------------------------------------
	// Timed finisher state machine
	// ---------------------------------------------------------------------------------------

	/// <summary>Kicks off a timed finisher: plays the animation, locks input for its full duration,
	/// and lets OnUpdate drive its pulses/strikes/wave to completion.</summary>
	void StartTimedFinisher( FinisherDefinition finisher, Vector3 origin, Vector3 facing )
	{
		_activeFinisher = finisher;
		_finisherStartTime = Time.Now;
		_finisherEndTime = Time.Now + finisher.Duration;
		_nextTickIndex = 0;
		_finisherDamageMult = DrunkennessSystem.Local?.DamageMultiplier ?? 1f;
		_shockwaveFinished = false;
		_shockwaveVfxSpawned = false;
		_shockwaveImpactPlayed = false;
		_shockwaveOrigin = origin;
		_shockwaveFacing = facing;
		_finisherHitSet.Clear();

		// The finisher lock: the whole move becomes one cooldown, so IsReady blocks any further
		// input (including another finisher) until it has played out, and the HUD bar fills nicely.
		_lastCooldownDuration = MathF.Max( 0.05f, finisher.Duration );
		_nextReadyTime = _finisherEndTime;

		PlayerAnimationDriver.Local?.PlayFinisher( finisher );
		HitFeedback.PlaySwing( finisher.SwingSound );

		if ( finisher.DisableMovement )
			_movementDisableEndTime = _finisherEndTime;
	}

	/// <summary>Advances the active finisher on its absolute tick schedule. Everything uses
	/// Time.Now threshold checks (not Time.Delta accumulation) so frame spikes can't desync the beats.</summary>
	void DriveActiveFinisher()
	{
		var f = _activeFinisher;
		if ( f is null )
			return;

		var now = Time.Now;

		switch ( f.Behavior )
		{
			case FinisherBehavior.AoeSpinPulses:
				DrivePulseTicks( f, now );
				break;
			case FinisherBehavior.LineShockwave:
				DriveShockwave( f, now );
				break;
			case FinisherBehavior.RapidCombo:
				DriveStrikeTicks( f, now );
				break;
		}

		if ( now < _finisherEndTime )
			return;

		// Catch-up pass: a big frame spike (or a drink pause) must never eat a scheduled beat, so
		// once the finisher's time is up we flush every remaining tick at once, then report.
		switch ( f.Behavior )
		{
			case FinisherBehavior.AoeSpinPulses:
				while ( _nextTickIndex < f.PulseCount )
				{
					DoAoeSpinPulse( f, _nextTickIndex );
					_nextTickIndex++;
				}
				break;
			case FinisherBehavior.RapidCombo:
				while ( _nextTickIndex < f.StrikeCount )
				{
					DoRapidStrike( f, _nextTickIndex );
					_nextTickIndex++;
				}
				break;
			case FinisherBehavior.LineShockwave:
				if ( !_shockwaveFinished )
				{
					SpawnShockwaveVfx( f );
					DoShockwaveHit( f, f.WaveDepth );
					_shockwaveFinished = true;
				}
				break;
		}

		CompleteFinisher( f );
	}

	void DrivePulseTicks( FinisherDefinition f, float now )
	{
		while ( _nextTickIndex < f.PulseCount && now >= _finisherStartTime + f.PulseLead + _nextTickIndex * f.PulseInterval )
		{
			DoAoeSpinPulse( f, _nextTickIndex );
			_nextTickIndex++;
		}
	}

	void DriveStrikeTicks( FinisherDefinition f, float now )
	{
		while ( _nextTickIndex < f.StrikeCount && now >= _finisherStartTime + StrikeTime( f, _nextTickIndex ) )
		{
			DoRapidStrike( f, _nextTickIndex );
			_nextTickIndex++;
		}
	}

	/// <summary>Seconds from the finisher's start that strike <paramref name="index"/> lands. All
	/// but the closer use the uniform StrikeInterval cadence; the closer can instead be pinned a
	/// fixed lead before the end of the clip (FinalStrikeLeadFromEnd) so it syncs to the animation's
	/// final beat rather than landing mid-swing.</summary>
	static float StrikeTime( FinisherDefinition f, int index )
	{
		var scheduled = f.StrikeLead + index * f.StrikeInterval;

		if ( index >= f.StrikeCount - 1 && f.FinalStrikeLeadFromEnd > 0f )
			return MathF.Max( scheduled, f.Duration - f.FinalStrikeLeadFromEnd );

		return scheduled;
	}

	/// <summary>The wavefront expands forward; reach only grows and we only ever damage enemies we
	/// haven't hit yet (hit set), so no enemy can be skipped no matter how big the frame jump is,
	/// and each is hit exactly once with the full slam.</summary>
	void DriveShockwave( FinisherDefinition f, float now )
	{
		if ( _shockwaveFinished )
			return;

		// Hold both the damage sweep and its visuals until WaveLead - the animation's slam beat -
		// so the hit and the effect land exactly when the slam reads, not at t=0.
		var elapsed = now - _finisherStartTime - f.WaveLead;
		if ( elapsed < 0f )
			return;

		SpawnShockwaveVfx( f );

		var waveTravel = MathF.Max( 0.01f,
			MathF.Min( f.Duration - f.WaveLead, f.WaveDepth / MathF.Max( 0.01f, f.WaveSpeed ) ) );

		if ( elapsed >= waveTravel )
		{
			DoShockwaveHit( f, f.WaveDepth );
			_shockwaveFinished = true;
			return;
		}

		DoShockwaveHit( f, f.WaveSpeed * elapsed );
	}

	/// <summary>Fires the slam's impact burst and the traveling shockwave front exactly once, the
	/// frame the wave launches. Shared by the live driver and the end-of-finisher catch-up so the
	/// visuals are never skipped if a frame spike swallows the launch beat.</summary>
	void SpawnShockwaveVfx( FinisherDefinition f )
	{
		if ( _shockwaveVfxSpawned )
			return;

		_shockwaveVfxSpawned = true;

		HitFeedback.PlaySwing( f.StrikeSound, 0.04f );

		Vfx.SlamImpact( _shockwaveOrigin, _shockwaveFacing, f.Range );
		Vfx.ShockwaveFront( _shockwaveOrigin, _shockwaveFacing, f.WaveSpeed,
			MathF.Min( f.Duration, f.WaveDepth / MathF.Max( 0.01f, f.WaveSpeed ) ), f.ArcDegrees );
	}

	void DoAoeSpinPulse( FinisherDefinition f, int index )
	{
		var origin = WorldPosition;
		var facing = WorldRotation.Forward;
		var hits = HitDetector.FindEnemies( origin, facing, f.Range, f.ArcDegrees );

		foreach ( var enemy in hits )
		{
			// Small knockback + long pulse stagger keeps the crowd inside the ring so all three
			// pulses land on the same mob rather than shoving it out of range.
			ApplyFinisherHit( f, enemy, origin, facing, f.Damage, f.Knockback, f.TickStagger );
		}

		Vfx.AoePulseRing( origin, f.Range, new Color( 1f, 0.8f, 0.3f ) );
		HitFeedback.PlaySwing( f.StrikeSound, 0.06f );
		HitFeedback.PlayFinisherImpact( f.ImpactStrength * 0.5f, hits.Count );
	}

	void DoRapidStrike( FinisherDefinition f, int index )
	{
		var isFinal = index >= f.StrikeCount - 1;
		var origin = WorldPosition;
		var facing = WorldRotation.Forward;
		var hits = HitDetector.FindEnemies( origin, facing, f.Range, f.StrikeArcDegrees );

		var damage = isFinal ? f.Damage * f.FinalHitDamageMultiplier : f.Damage;
		var knockback = isFinal ? f.FinalHitKnockback : 0f;
		var stagger = isFinal ? (f.FinalHitStagger > 0f ? f.FinalHitStagger : f.StaggerTime) : f.TickStagger;

		foreach ( var enemy in hits )
			ApplyFinisherHit( f, enemy, origin, facing, damage, knockback, stagger );

		HitFeedback.PlaySwing( f.StrikeSound, 0.12f );

		if ( isFinal )
		{
			Vfx.FinisherBurst( origin + facing * (f.Range * 0.6f) + Vector3.Up * 55f );
			HitFeedback.PlayFinisherImpact( f.ImpactStrength, hits.Count, f.FinalImpactSound );
		}
		else
		{
			HitFeedback.PlayFinisherImpact( 0.3f, hits.Count );
		}
	}

	void DoShockwaveHit( FinisherDefinition f, float reach )
	{
		var hits = HitDetector.FindEnemies( _shockwaveOrigin, _shockwaveFacing, reach, f.ArcDegrees );

		var landed = 0;
		foreach ( var enemy in hits )
		{
			if ( _finisherHitSet.Contains( enemy ) )
				continue;

			ApplyFinisherHit( f, enemy, _shockwaveOrigin, _shockwaveFacing, f.Damage, f.Knockback, f.StaggerTime );
			landed++;
		}

		// The slam's "extra heavy hit" fires once, the first time the wavefront actually connects -
		// gated on a real hit so a shockwave that reaches nobody stays a whoosh. See FinalImpactSound.
		if ( landed > 0 && !_shockwaveImpactPlayed )
		{
			_shockwaveImpactPlayed = true;
			HitFeedback.PlayFinisherImpact( f.ImpactStrength, _finisherHitSet.Count, f.FinalImpactSound );
		}
	}

	/// <summary>Applies one tick of a timed finisher to one enemy - beats it as a finisher hit, so
	/// it raises the golden finisher damage number like any other finisher hit, and tracks the
	/// distinct hit count for the completion report.</summary>
	void ApplyFinisherHit( FinisherDefinition f, EnemyBase enemy, Vector3 origin, Vector3 facing, float damage, float knockback, float stagger )
	{
		if ( enemy is null || !enemy.IsValid || enemy.IsDead )
			return;

		var dir = enemy.WorldPosition - origin;
		dir = new Vector3( dir.x, dir.y, 0 );
		dir = dir.Length > 0.01f ? dir / dir.Length : facing;

		var dealt = damage * _finisherDamageMult;

		_finisherHitSet.Add( enemy );

		enemy.ApplyHit( dealt, dir * knockback, stagger, fromFinisher: true );
	}

	/// <summary>Reports the finisher once with the distinct count of enemies hit across all ticks,
	/// then tears the state down. Fires on the finisher's first connected hit only via the caller's
	/// distinct > 0 check, matching the old whiff rule (a finisher that hit nobody pays no score).
	/// Damage numbers are raised per tick by ApplyFinisherHit, not here.</summary>
	void CompleteFinisher( FinisherDefinition finisher )
	{
		var distinct = _finisherHitSet.Count;

		if ( distinct > 0 )
		{
			ComboSystem.Local?.RegisterHit( distinct );
			RaiseAttackPerformed( finisher.Sequence[finisher.Sequence.Length - 1] );
			GameEvents.RaiseFinisherExecuted( finisher, distinct );
		}

		ClearActiveFinisher();
	}

	void ClearActiveFinisher()
	{
		_activeFinisher = null;
		_finisherHitSet.Clear();
	}

	/// <summary>Only called once an attack actually connects - drives StyleSystem's variety bonus,
	/// which used to build up on whiffed swings too and inflate the score multiplier for free.</summary>
	void RaiseAttackPerformed( AttackId id )
	{
		var wasVaried = _lastPerformedAttack is null || _lastPerformedAttack.Value != id;
		GameEvents.RaiseAttackPerformed( id, wasVaried );
		_lastPerformedAttack = id;
	}
}
