namespace DrunkenBarFight;

/// <summary>Player HP and death handling. Damage never touches drunkenness directly - only combo.</summary>
public class PlayerStats : Component
{
	public static PlayerStats Local { get; private set; }

	[Property, Group( "Tuning" )] public float MaxHP { get; set; } = 100f;

	public float CurrentHP { get; private set; }
	public bool IsDead { get; private set; }

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

		// The drunker you are, the less you feel it - part of the "powerful but reckless" trade-off.
		amount *= DrunkennessSystem.Local?.DamageTakenMultiplier ?? 1f;

		CurrentHP = System.Math.Max( 0, CurrentHP - amount );
		ComboSystem.Local?.OnPlayerDamaged();
		GameEvents.RaiseShakeRequested( 3f + amount * 0.15f, 0.12f );

		if ( CurrentHP <= 0 )
		{
			IsDead = true;
			GameEvents.RaisePlayerDied();
			GameManager.Instance?.EndRun( EndReason.Killed );
		}
	}

	public void ResetForNewRun()
	{
		CurrentHP = MaxHP;
		IsDead = false;
	}
}
