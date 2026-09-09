using System;

namespace DrunkenBarFight;

/// <summary>
/// The combo is now purely a "keep hitting enemies" streak - it has nothing to do with attack
/// variety anymore. Every attack that actually connects extends the streak by however many
/// enemies it hit; the streak breaks if the player goes GraceTime seconds without landing a hit.
/// The HUD's combo bar shows that grace window draining, not the streak count itself - its whole
/// job is to make "the combo is about to die" visible and pressuring.
/// </summary>
public class ComboSystem : Component
{
	public static ComboSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float GraceTime { get; set; } = 2.2f;
	[Property, Group( "Tuning" )] public float DamagedGracePenaltyFraction { get; set; } = 0.5f;

	public int HitStreak { get; private set; }
	public int HighestStreak { get; private set; }

	/// <summary>Kept as float for compatibility with ScoreSystem/StyleSystem/RunStats, which just want a number.</summary>
	public float Value => HitStreak;
	public float HighestValue => HighestStreak;

	float _graceRemaining;

	/// <summary>1 = just landed a hit, 0 = about to break. Drives the HUD's combo bar.</summary>
	public float GraceFraction01 => GraceTime > 0f ? Math.Clamp( _graceRemaining / GraceTime, 0f, 1f ) : 0f;

	protected override void OnAwake()
	{
		Local = this;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		if ( HitStreak <= 0 )
			return;

		_graceRemaining -= Time.Delta;
		if ( _graceRemaining <= 0f )
		{
			_graceRemaining = 0f;
			HitStreak = 0;
			GameEvents.RaiseComboBroken();
		}
	}

	/// <summary>Call this whenever a player attack actually connects with at least one enemy - never on a whiff.</summary>
	public void RegisterHit( int enemiesHitThisSwing )
	{
		if ( enemiesHitThisSwing <= 0 )
			return;

		HitStreak += enemiesHitThisSwing;
		HighestStreak = Math.Max( HighestStreak, HitStreak );
		_graceRemaining = GraceTime;

		GameEvents.RaiseComboHit( HitStreak );
	}

	/// <summary>Called by PlayerStats when the player takes damage - eats into the grace window instead of instantly halving the streak.</summary>
	public void OnPlayerDamaged()
	{
		_graceRemaining = Math.Max( 0f, _graceRemaining - GraceTime * DamagedGracePenaltyFraction );
	}

	public void ResetRun()
	{
		HitStreak = 0;
		HighestStreak = 0;
		_graceRemaining = 0f;
	}
}
