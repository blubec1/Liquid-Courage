namespace DrunkenBarFight;

/// <summary>
/// Fills as the player defeats enemies - never decreases on its own. When it hits max, the player
/// "drinks a glass": every live enemy freezes for a beat, the player plays a drinking animation,
/// and DrunkennessSystem.DrinkGlass() adds a fixed chunk of drunkenness. This is the ONLY thing
/// that increases drunkenness now - see DrunkennessSystem for the passive decay/power-scaling side.
/// Deliberately reuses EnemyBase's existing per-enemy freeze field for the "everyone pauses" beat
/// instead of touching Scene.TimeScale, and the same best-effort TryPlaySequence/procedural-fallback
/// pattern PlayerAnimationDriver already uses for attacks, for the drink animation itself.
/// </summary>
public class DrinkMeter : Component
{
	public static DrinkMeter Local { get; private set; }

	[Property, Group( "Tuning" )] public float MaxValue { get; set; } = 100f;
	[Property, Group( "Tuning" )] public float ProgressPerKill { get; set; } = 22f;
	[Property, Group( "Tuning" )] public float ProgressPerToughKillBonus { get; set; } = 15f;
	[Property, Group( "Tuning" )] public float FreezeDuration { get; set; } = 0.9f;
	[Property, Group( "Tuning" )] public float HealPerDrink { get; set; } = 30f;

	[Property, Group( "Animation" )] public string DrinkAnimation { get; set; } = "";
	[Property, Group( "Animation" )] public float DrinkAnimationDuration { get; set; } = 0.9f;

	public float Value { get; private set; }
	public float Fraction01 => MaxValue > 0f ? System.Math.Clamp( Value / MaxValue, 0f, 1f ) : 0f;
	public bool IsDrinking { get; private set; }

	float _drinkEndTime;

	protected override void OnAwake()
	{
		Local = this;
		GameEvents.EnemyKilled += OnEnemyKilled;
	}

	protected override void OnDestroy()
	{
		GameEvents.EnemyKilled -= OnEnemyKilled;
	}

	protected override void OnUpdate()
	{
		if ( IsDrinking && Time.Now >= _drinkEndTime )
			IsDrinking = false;
	}

	void OnEnemyKilled( EnemyKillInfo info )
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		// Killing the sobering bartender is the "good for you" play - it shouldn't also push you
		// toward your next drink, or the two systems fight each other.
		if ( info.WasSobering )
			return;

		var amount = ProgressPerKill + (info.Enemy is EnemyBruiser ? ProgressPerToughKillBonus : 0f);
		Value += amount;

		if ( Value >= MaxValue )
		{
			Value = 0f;
			TriggerDrink();
		}
	}

	void TriggerDrink()
	{
		IsDrinking = true;
		_drinkEndTime = Time.Now + FreezeDuration;

		foreach ( var enemy in EnemyBase.All )
		{
			if ( enemy is not null && enemy.IsValid && !enemy.IsDead )
				enemy.SetFreeze( FreezeDuration );
		}

		var duration = DrinkAnimationDuration > 0f ? DrinkAnimationDuration : FreezeDuration;
		PlayerAnimationDriver.Local?.PlayDrink( DrinkAnimation, duration );

		DrunkennessSystem.Local?.DrinkGlass();
		PlayerStats.Local?.Heal( HealPerDrink );
		Vfx.DrinkCelebration( WorldPosition );
	}

	public void ResetRun()
	{
		Value = 0f;
		IsDrinking = false;
		_drinkEndTime = 0f;
	}
}
