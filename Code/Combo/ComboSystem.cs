using System;

namespace DrunkenBarFight;

/// <summary>
/// Owns the combo meter. It doesn't know anything about attacks or variety scaling - callers
/// (PlayerCombat) decide how much a given hit is worth and call AddCombo with the final number.
/// This system just builds, decays and fires threshold events as the meter climbs.
/// </summary>
public class ComboSystem : Component
{
	public static ComboSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float ThresholdStep { get; set; } = 20f;
	[Property, Group( "Tuning" )] public float DecayPerSecond { get; set; } = 5f;
	[Property, Group( "Tuning" )] public float IdleDrainMultiplier { get; set; } = 4.5f;
	[Property, Group( "Tuning" )] public float IdleGraceTime { get; set; } = 1.3f;
	[Property, Group( "Tuning" )] public float DamageLossFraction { get; set; } = 0.5f;

	public float Value { get; private set; }
	public float HighestValue { get; private set; }

	float _lastAttackTime = -999f;
	int _thresholdIndexThisStreak;

	protected override void OnAwake()
	{
		Local = this;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		var idle = (Time.Now - _lastAttackTime) > IdleGraceTime;
		var rate = DecayPerSecond * (idle ? IdleDrainMultiplier : 1f);
		if ( rate > 0 && Value > 0 )
		{
			Value = MathF.Max( 0, Value - rate * Time.Delta );
			if ( Value <= 0.01f )
				_thresholdIndexThisStreak = 0;
		}
	}

	/// <summary>Add (already variety-scaled) combo value from a successful hit.</summary>
	public void AddCombo( float amount )
	{
		if ( amount <= 0 )
			return;

		Value += amount;
		HighestValue = MathF.Max( HighestValue, Value );
		_lastAttackTime = Time.Now;

		CheckThresholds();
	}

	/// <summary>Called by PlayerStats whenever the player takes damage - combo takes a hit too.</summary>
	public void OnPlayerDamaged()
	{
		Value *= (1f - DamageLossFraction);
	}

	void CheckThresholds()
	{
		var targetIndex = (int)MathF.Floor( Value / ThresholdStep );
		while ( _thresholdIndexThisStreak < targetIndex )
		{
			_thresholdIndexThisStreak++;
			GameEvents.RaiseComboThreshold( _thresholdIndexThisStreak );
		}
	}

	public void ResetRun()
	{
		Value = 0;
		HighestValue = 0;
		_thresholdIndexThisStreak = 0;
		_lastAttackTime = -999f;
	}
}
