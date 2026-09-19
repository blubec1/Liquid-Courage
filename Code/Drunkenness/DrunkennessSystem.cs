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
	[Property, Group( "Tuning" )] public float GlassAmount { get; set; } = 20f;
	[Property, Group( "Tuning" )] public float LastCallThreshold { get; set; } = 130f;
	[Property, Group( "Tuning" )] public float PassiveDecayAmount { get; set; } = 1f;
	[Property, Group( "Tuning" )] public float PassiveDecayInterval { get; set; } = 15f;

	/// <summary>Below this raw Value, the player isn't drunk enough for ambient effects like camera
	/// sway to show at all (see ShakeFraction01) - a light buzz shouldn't visibly affect anything.</summary>
	[Property, Group( "Tuning" )] public float ShakeStartValue { get; set; } = 60f;

	[Property, Group( "Power Scaling" )] public float MaxDamageDealtBonus { get; set; } = 1.0f;
	[Property, Group( "Power Scaling" )] public float MaxDamageTakenReduction { get; set; } = 0.5f;
	[Property, Group( "Power Scaling" )] public float MaxMoveSpeedBonus { get; set; } = 0.3f;
	[Property, Group( "Power Scaling" )] public float MaxAttackSpeedBonus { get; set; } = 0.3f;

	// --- Sober Up (player press) ---
	/// <summary>Seconds between presses - short on purpose, since the cost (not the cooldown) is what
	/// keeps it from being a free "never face Last Call" button.</summary>
	[Property, Group( "Sober Up" )] public float SoberUpCooldown { get; set; } = 3f;

	/// <summary>Each press strips this fraction of current drunkenness (via DrunkennessSystem.Reduce).</summary>
	[Property, Group( "Sober Up" )] public float SoberRemoveFraction { get; set; } = 0.4f;

	/// <summary>Base hangover window at low drunkenness; grows toward SoberMaxDebuffDuration at the brink.</summary>
	[Property, Group( "Sober Up" )] public float SoberBaseDebuffDuration { get; set; } = 2f;
	[Property, Group( "Sober Up" )] public float SoberMaxDebuffDuration { get; set; } = 3f;

	/// <summary>Peak move-speed penalty at max drunkenness. Concave scaling (squared) keeps step-by-step
	/// management cheap and only makes brake-at-the-brink presses costly.</summary>
	[Property, Group( "Sober Up" )] public float SoberMaxMovePenalty { get; set; } = 0.35f;
	[Property, Group( "Sober Up" )] public float SoberMaxDamagePenalty { get; set; } = 0.45f;

	public float Value { get; private set; }
	public float HighestValue { get; private set; }
	public int GlassesDrunk { get; private set; }

	/// <summary>The "one drink from blackout" danger zone - this is what the screen-edge VFX reacts to.</summary>
	public bool IsLastCall => Value >= LastCallThreshold;

	float NormalizedT => Math.Clamp( Value / MaxValue, 0f, 1f );

	/// <summary>Public 0-1 read of how drunk the player currently is (linear, unlike BlackoutFraction01
	/// which only ramps in the last-call danger zone). Safe to use for continuous ambient effects like
	/// camera sway - just don't put the raw number on screen (see class doc comment).</summary>
	public float Fraction01 => NormalizedT;

	/// <summary>0 below ShakeStartValue, then ramps 0-1 from there up to MaxValue. Drives the camera's
	/// drunk sway (see IsoCameraRig) so it only kicks in once the player is properly drunk, not from
	/// the very first sip.</summary>
	public float ShakeFraction01 => MaxValue > ShakeStartValue
		? Math.Clamp( (Value - ShakeStartValue) / (MaxValue - ShakeStartValue), 0f, 1f )
		: 0f;

	/// <summary>0 below LastCallThreshold, ramps to 1 as Value approaches MaxValue. Drives the blackout tunnel-vision VFX.</summary>
	public float BlackoutFraction01 => MaxValue > LastCallThreshold
		? Math.Clamp( (Value - LastCallThreshold) / (MaxValue - LastCallThreshold), 0f, 1f )
		: 0f;

	public float MoveSpeedMultiplier => (1f + NormalizedT * MaxMoveSpeedBonus) * (IsSoberingDebuffed ? (1f - SoberMaxMovePenalty * _soberHangover01) : 1f);
	public float AttackCooldownMultiplier => Math.Clamp( 1f - NormalizedT * MaxAttackSpeedBonus, 0.4f, 1f );

	/// <summary>Damage the player deals is multiplied by this - climbs toward +100% at max drunkenness.</summary>
	public float DamageMultiplier => (1f + NormalizedT * MaxDamageDealtBonus) * (IsSoberingDebuffed ? (1f - SoberMaxDamagePenalty * _soberHangover01) : 1f);

	/// <summary>Damage the player takes is multiplied by this - drops toward -50% at max drunkenness.</summary>
	public float DamageTakenMultiplier => Math.Clamp( 1f - NormalizedT * MaxDamageTakenReduction, 0.05f, 1f );

	/// <summary>Additive bonus folded into ScoreSystem's multiplier formula.</summary>
	public float ScoreMultiplierBonus => NormalizedT * 1.2f + (IsLastCall ? 1f : 0f);

	float _decayTimer;
	float _soberUpReadyTime;
	float _soberDebuffEndTime;
	float _soberHangover01;

	/// <summary>True while a sober-up hangover is active - player is slowed and hits softer.</summary>
	public bool IsSoberingDebuffed => Time.Now < _soberDebuffEndTime;

	/// <summary>0-1 severity of the active hangover (concave: squared drunk fraction at press time).</summary>
	public float SoberDebuffFraction01 => IsSoberingDebuffed ? _soberHangover01 : 0f;

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

		if ( Input.Pressed( "SoberUp" ) )
			TrySoberUp();

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

	/// <summary>Player-initiated sober-up (F). Strips SoberRemoveFraction of current drunkenness, then
	/// hits the player with a hangover whose severity scales with how drunk they were - concave and
	/// capped (see SoberMax*Penalty) so step-by-step management stays cheap and only brake-at-the-brink
	/// presses hurt. Cooldown is short but the hangover is what stops spam. No-op when basically sober.</summary>
	public void TrySoberUp()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		if ( Time.Now < _soberUpReadyTime || Value < 1f )
			return;

		// Concave severity: hangover01 = (drunk fraction)². At mid-drunk management this is near-free;
		// it only climbs steeply as the player rides the brink.
		var hangover = Math.Clamp( Value / MaxValue, 0f, 1f );
		var severity = hangover * hangover;

		Reduce( Value * SoberRemoveFraction );

		_soberUpReadyTime = Time.Now + SoberUpCooldown;
		_soberHangover01 = severity;
		_soberDebuffEndTime = Time.Now + SoberBaseDebuffDuration + severity * (SoberMaxDebuffDuration - SoberBaseDebuffDuration);

		GameEvents.RaiseSoberUpPerformed();
		GameEvents.RaiseShakeRequested( 1.5f, 0.06f );

		var pos = PlayerStats.Local is not null ? PlayerStats.Local.WorldPosition : WorldPosition;
		Vfx.PickupSparkle( pos );
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
		_soberUpReadyTime = 0f;
		_soberDebuffEndTime = 0f;
		_soberHangover01 = 0f;
	}
}
