namespace DrunkenBarFight;

/// <summary>
/// Score = base action value x combo multiplier x drunkenness multiplier x style multiplier.
/// This class owns the running score/multiplier and reacts to kill/finisher events; it doesn't
/// know how those events were produced.
/// </summary>
public class ScoreSystem : Component
{
	public static ScoreSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float KillScoreBase { get; set; } = 15f;
	[Property, Group( "Tuning" )] public float EnvironmentalKillBonusFlat { get; set; } = 10f;
	[Property, Group( "Tuning" )] public float SurvivalTickValue { get; set; } = 20f;
	[Property, Group( "Tuning" )] public float SurvivalTickInterval { get; set; } = 5f;
	// ComboSystem.Value is now a hit-connect streak count (typically 0-30ish), not the old 0-150
	// combo meter - this scale was retuned to match (streak of 20 -> +2.0x instead of needing 100).
	[Property, Group( "Tuning" )] public float ComboMultiplierScale { get; set; } = 10f;

	public long Score { get; private set; }
	public int FinishersUsed { get; private set; }
	public int EnemiesDefeated { get; private set; }

	float _timeSinceLastSurvivalTick;

	public float ComboMultiplier => 1f + (ComboSystem.Local?.Value ?? 0f) / ComboMultiplierScale;
	public float DrunkennessMultiplier => 1f + (DrunkennessSystem.Local?.ScoreMultiplierBonus ?? 0f);
	public float StyleMultiplier => 1f + (StyleSystem.Local?.CurrentStyleBonus ?? 0f);

	/// <summary>Live combined multiplier - shown directly in the HUD.</summary>
	public float Multiplier => ComboMultiplier * DrunkennessMultiplier * StyleMultiplier;

	protected override void OnAwake()
	{
		Local = this;
		GameEvents.EnemyKilled += OnEnemyKilled;
		GameEvents.FinisherExecuted += OnFinisherExecuted;
	}

	protected override void OnDestroy()
	{
		GameEvents.EnemyKilled -= OnEnemyKilled;
		GameEvents.FinisherExecuted -= OnFinisherExecuted;
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		_timeSinceLastSurvivalTick += Time.Delta;
		if ( _timeSinceLastSurvivalTick >= SurvivalTickInterval )
		{
			_timeSinceLastSurvivalTick = 0;
			AddScore( SurvivalTickValue );
		}
	}

	void OnEnemyKilled( EnemyKillInfo info )
	{
		EnemiesDefeated++;
		var baseValue = KillScoreBase + (info.WasEnvironmental ? EnvironmentalKillBonusFlat : 0f);
		AddScore( baseValue );
	}

	void OnFinisherExecuted( FinisherDefinition def, int hitCount )
	{
		FinishersUsed++;
		AddScore( def.ScoreValue );
	}

	public float AddScore( float baseValue )
	{
		if ( baseValue <= 0 || (GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing) )
			return 0;

		var gained = baseValue * Multiplier;
		Score += (long)System.MathF.Round( gained );
		return gained;
	}

	public void ResetRun()
	{
		Score = 0;
		FinishersUsed = 0;
		EnemiesDefeated = 0;
		_timeSinceLastSurvivalTick = 0;
		MultiKillTracker.Reset();
	}
}
