using System;

namespace DrunkenBarFight;

/// <summary>
/// The hidden risk/reward core of the game. Never render Value directly in the HUD - only
/// react to IsLastCall and the power-scaling multipliers below, so the player has to feel
/// their way through it rather than watch a number.
///
/// Drunkenness only rises when the combo meter crosses a threshold (see ComboSystem), never
/// directly from taking damage or from kills. It only falls from sobering enemies/pickups.
/// Hitting the max ends the run immediately, with no grace period.
/// </summary>
public class DrunkennessSystem : Component
{
	public static DrunkennessSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float IncreasePerThreshold { get; set; } = 12f;
	[Property, Group( "Tuning" )] public float LastCallThreshold { get; set; } = 78f;
	[Property, Group( "Tuning" )] public float MaxValue { get; set; } = 100f;

	[Property, Group( "Power Scaling" )] public float MaxMoveSpeedBonus { get; set; } = 0.5f;
	[Property, Group( "Power Scaling" )] public float MaxAttackSpeedBonus { get; set; } = 0.35f;
	[Property, Group( "Power Scaling" )] public float MaxDamageBonus { get; set; } = 0.6f;
	[Property, Group( "Power Scaling" )] public float LastCallExtraBonus { get; set; } = 0.2f;

	public float Value { get; private set; }
	public float HighestValue { get; private set; }

	public bool IsLastCall => Value >= LastCallThreshold && Value < MaxValue;

	float NormalizedT => Math.Clamp( Value / MaxValue, 0f, 1f );

	public float MoveSpeedMultiplier => 1f + NormalizedT * MaxMoveSpeedBonus + (IsLastCall ? LastCallExtraBonus * 0.5f : 0f);
	public float AttackCooldownMultiplier => Math.Clamp( 1f - NormalizedT * MaxAttackSpeedBonus - (IsLastCall ? LastCallExtraBonus * 0.3f : 0f), 0.4f, 1f );
	public float DamageMultiplier => 1f + NormalizedT * MaxDamageBonus + (IsLastCall ? LastCallExtraBonus : 0f);

	/// <summary>Additive bonus folded into ScoreSystem's multiplier formula.</summary>
	public float ScoreMultiplierBonus => NormalizedT * 1.2f + (IsLastCall ? 1f : 0f);

	protected override void OnAwake()
	{
		Local = this;
		GameEvents.ComboThresholdReached += OnComboThreshold;
	}

	protected override void OnDestroy()
	{
		GameEvents.ComboThresholdReached -= OnComboThreshold;
	}

	void OnComboThreshold( int index )
	{
		Increase( IncreasePerThreshold );
	}

	public void Increase( float amount )
	{
		if ( amount <= 0 || (GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing) )
			return;

		var wasLastCall = IsLastCall;
		Value = Math.Clamp( Value + amount, 0f, MaxValue );
		HighestValue = MathF.Max( HighestValue, Value );

		if ( Value >= MaxValue )
		{
			GameEvents.RaiseOverdrunk();
			GameManager.Instance?.EndRun( EndReason.Overdrunk );
			return;
		}

		CheckLastCallTransition( wasLastCall );
	}

	/// <summary>Called by sobering enemy deaths / sobering pickups.</summary>
	public void Reduce( float amount )
	{
		if ( amount <= 0 )
			return;

		var wasLastCall = IsLastCall;
		Value = Math.Clamp( Value - amount, 0f, MaxValue );
		CheckLastCallTransition( wasLastCall );
	}

	void CheckLastCallTransition( bool wasLastCall )
	{
		if ( IsLastCall && !wasLastCall )
			GameEvents.RaiseLastCallEntered();
		else if ( !IsLastCall && wasLastCall )
			GameEvents.RaiseLastCallExited();
	}

	public void ResetRun()
	{
		Value = 0;
		HighestValue = 0;
	}
}
