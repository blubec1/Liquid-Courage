using System;

namespace DrunkenBarFight;

/// <summary>
/// The hidden risk/reward core of the game. Never render Value directly in the HUD - only react
/// to IsLastCall/BlackoutFraction01 and the power-scaling multipliers below (see Hud.razor's VFX),
/// so the player feels their way through it rather than watching a number.
///
/// Drunkenness now ONLY rises when DrinkMeter fills up and calls DrinkGlass() (see DrinkMeter.cs) -
/// never directly from combo, damage, or kills. It falls slowly on its own over time, and faster
/// from sobering enemies/pickups. Crossing MaxValue on a drink ends the run immediately.
/// </summary>
public class DrunkennessSystem : Component
{
	public static DrunkennessSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float MaxValue { get; set; } = 150f;
	[Property, Group( "Tuning" )] public float GlassAmount { get; set; } = 10f;
	[Property, Group( "Tuning" )] public float LastCallThreshold { get; set; } = 130f;
	[Property, Group( "Tuning" )] public float PassiveDecayAmount { get; set; } = 1f;
	[Property, Group( "Tuning" )] public float PassiveDecayInterval { get; set; } = 15f;

	[Property, Group( "Power Scaling" )] public float MaxDamageDealtBonus { get; set; } = 1.0f;
	[Property, Group( "Power Scaling" )] public float MaxDamageTakenReduction { get; set; } = 0.5f;
	[Property, Group( "Power Scaling" )] public float MaxMoveSpeedBonus { get; set; } = 0.3f;
	[Property, Group( "Power Scaling" )] public float MaxAttackSpeedBonus { get; set; } = 0.3f;

	public float Value { get; private set; }
	public float HighestValue { get; private set; }
	public int GlassesDrunk { get; private set; }

	/// <summary>The "one drink from blackout" danger zone - this is what the screen-edge VFX reacts to.</summary>
	public bool IsLastCall => Value >= LastCallThreshold;

	float NormalizedT => Math.Clamp( Value / MaxValue, 0f, 1f );

	/// <summary>0 below LastCallThreshold, ramps to 1 as Value approaches MaxValue. Drives the blackout tunnel-vision VFX.</summary>
	public float BlackoutFraction01 => MaxValue > LastCallThreshold
		? Math.Clamp( (Value - LastCallThreshold) / (MaxValue - LastCallThreshold), 0f, 1f )
		: 0f;

	public float MoveSpeedMultiplier => 1f + NormalizedT * MaxMoveSpeedBonus;
	public float AttackCooldownMultiplier => Math.Clamp( 1f - NormalizedT * MaxAttackSpeedBonus, 0.4f, 1f );

	/// <summary>Damage the player deals is multiplied by this - climbs toward +100% at max drunkenness.</summary>
	public float DamageMultiplier => 1f + NormalizedT * MaxDamageDealtBonus;

	/// <summary>Damage the player takes is multiplied by this - drops toward -50% at max drunkenness.</summary>
	public float DamageTakenMultiplier => Math.Clamp( 1f - NormalizedT * MaxDamageTakenReduction, 0.05f, 1f );

	/// <summary>Additive bonus folded into ScoreSystem's multiplier formula.</summary>
	public float ScoreMultiplierBonus => NormalizedT * 1.2f + (IsLastCall ? 1f : 0f);

	float _decayTimer;

	protected override void OnAwake()
	{
		Local = this;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		// DEV ONLY - quick keys to test drunkenness thresholds/overdrunk without grinding kills.
		// M = drink a glass (+10, same code path a real drink uses - triggers the toast/flash too).
		// N = sober up a glass (-10). Remove both before shipping. Uses raw keyboard, not an
		// Input.config action, since these are debug-only and shouldn't be player-rebindable.
		try
		{
			if ( Sandbox.Input.Keyboard.Pressed( "M" ) )
				DrinkGlass();
			if ( Sandbox.Input.Keyboard.Pressed( "N" ) )
				Reduce( GlassAmount );
		}
		catch { }

		if ( Value <= 0f )
		{
			_decayTimer = 0f;
			return;
		}

		_decayTimer += Time.Delta;
		if ( _decayTimer >= PassiveDecayInterval )
		{
			_decayTimer -= PassiveDecayInterval;
			Reduce( PassiveDecayAmount );
		}
	}

	/// <summary>The ONLY way drunkenness goes up. Called by DrinkMeter once it fills. Deliberately not
	/// clamped to MaxValue on the way in - overshooting past MaxValue is exactly what ends the run.</summary>
	public void DrinkGlass()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		var wasLastCall = IsLastCall;
		GlassesDrunk++;
		Value += GlassAmount;
		HighestValue = MathF.Max( HighestValue, Value );

		GameEvents.RaiseDrinkTriggered( GlassesDrunk );

		if ( Value > MaxValue )
		{
			GameEvents.RaiseOverdrunk();
			GameManager.Instance?.EndRun( EndReason.Overdrunk );
			return;
		}

		CheckLastCallTransition( wasLastCall );
	}

	/// <summary>Called by sobering enemy deaths, sobering pickups, and passive decay.</summary>
	public void Reduce( float amount )
	{
		if ( amount <= 0 )
			return;

		var wasLastCall = IsLastCall;
		Value = Math.Max( 0f, Value - amount );
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
		GlassesDrunk = 0;
		_decayTimer = 0f;
	}
}
