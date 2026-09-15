namespace DrunkenBarFight;

/// <summary>Player HP and death handling. Damage never touches drunkenness directly - only combo.</summary>
public class PlayerStats : Component
{
	public static PlayerStats Local { get; private set; }

	[Property, Group( "Tuning" )] public float MaxHP { get; set; } = 130f;

	// Without this, every enemy in melee range resolves its own attack independently, so getting
	// surrounded by even 3-4 enemies could land all of their hits in the same instant and delete
	// a huge chunk of HP with zero counterplay - that's what made death feel instant and unfair.
	// A brief immunity window after any hit means a surround still hurts (only your damage is
	// ignored, not theirs individually queuing up), but it can only ever cost you one hit's worth
	// of damage per window instead of every attacker's hit landing at once.
	[Property, Group( "Tuning" )] public float DamageImmunityDuration { get; set; } = 0.35f;

	public float CurrentHP { get; private set; }
	public bool IsDead { get; private set; }

	float _immuneUntil = -1f;

	protected override void OnAwake()
	{
		Local = this;
		CurrentHP = MaxHP;
	}

	public void TakeDamage( float amount )
	{
		if ( IsDead || amount <= 0 )
			return;

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		if ( Time.Now < _immuneUntil )
			return;

		_immuneUntil = Time.Now + DamageImmunityDuration;

		// The drunker you are, the less you feel it - part of the "powerful but reckless" trade-off.
		amount *= DrunkennessSystem.Local?.DamageTakenMultiplier ?? 1f;

		CurrentHP = System.Math.Max( 0, CurrentHP - amount );
		ComboSystem.Local?.OnPlayerDamaged();

		var fraction = MaxHP > 0f ? System.Math.Clamp( amount / MaxHP, 0f, 1f ) : 0f;
		GameEvents.RaisePlayerDamaged( amount, fraction );
		HitFeedback.PlayPlayerHurt( fraction );
		Vfx.PlayerHit( WorldPosition, fraction );
		PlayerAnimationDriver.Local?.FlashHit();

		if ( CurrentHP <= 0 )
		{
			IsDead = true;
			GameEvents.RaisePlayerDied();
			GameManager.Instance?.EndRun( EndReason.Killed );
		}
	}

	/// <summary>Called by DrinkMeter each time a glass is downed - drinking isn't just a power/risk
	/// trade-off, it patches you up a little too.</summary>
	public void Heal( float amount )
	{
		if ( IsDead || amount <= 0 )
			return;

		CurrentHP = System.Math.Min( MaxHP, CurrentHP + amount );
	}

	public void ResetForNewRun()
	{
		CurrentHP = MaxHP;
		IsDead = false;
		_immuneUntil = -1f;
	}
}
