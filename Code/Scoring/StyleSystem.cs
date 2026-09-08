using System;

namespace DrunkenBarFight;

/// <summary>
/// A small set of understandable "style" bonuses that feed into ScoreSystem's multiplier.
/// Drunkenness/Last Call scoring is owned by DrunkennessSystem directly - this class covers
/// everything else: attack variety, maintaining a combo, finishers, multi-kills and
/// environmental kills.
/// </summary>
public class StyleSystem : Component
{
	public static StyleSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float ComboMaintainFloor { get; set; } = 40f;
	[Property, Group( "Tuning" )] public float ComboMaintainBonus { get; set; } = 0.2f;
	[Property, Group( "Tuning" )] public float VarietyBonusPerStreak { get; set; } = 0.06f;
	[Property, Group( "Tuning" )] public float MaxVarietyBonus { get; set; } = 0.3f;
	[Property, Group( "Tuning" )] public float FinisherPulseAmount { get; set; } = 0.5f;
	[Property, Group( "Tuning" )] public float FinisherPulseDuration { get; set; } = 3f;
	[Property, Group( "Tuning" )] public float MultiKillPulsePerKill { get; set; } = 0.25f;
	[Property, Group( "Tuning" )] public float MultiKillPulseDuration { get; set; } = 2.5f;
	[Property, Group( "Tuning" )] public float EnvironmentalPulseAmount { get; set; } = 0.35f;
	[Property, Group( "Tuning" )] public float EnvironmentalPulseDuration { get; set; } = 2.5f;

	int _varietyStreak;
	AttackId? _lastAttack;

	float _finisherPulseEnd = -999f;
	float _multiKillPulseEnd = -999f;
	float _multiKillPulseAmount;
	float _environmentalPulseEnd = -999f;

	protected override void OnAwake()
	{
		Local = this;
		GameEvents.AttackPerformed += OnAttackPerformed;
		GameEvents.FinisherExecuted += OnFinisherExecuted;
		GameEvents.EnemyKilled += OnEnemyKilled;
	}

	protected override void OnDestroy()
	{
		GameEvents.AttackPerformed -= OnAttackPerformed;
		GameEvents.FinisherExecuted -= OnFinisherExecuted;
		GameEvents.EnemyKilled -= OnEnemyKilled;
	}

	void OnAttackPerformed( AttackId id, bool wasVaried )
	{
		if ( wasVaried || _lastAttack is null )
			_varietyStreak++;
		else
			_varietyStreak = 0;

		_lastAttack = id;
	}

	void OnFinisherExecuted( FinisherDefinition def, int hitCount )
	{
		_finisherPulseEnd = Time.Now + FinisherPulseDuration;
	}

	void OnEnemyKilled( EnemyKillInfo info )
	{
		if ( info.MultiKillIndex > 0 )
		{
			_multiKillPulseAmount = info.MultiKillIndex * MultiKillPulsePerKill;
			_multiKillPulseEnd = Time.Now + MultiKillPulseDuration;
		}

		if ( info.WasEnvironmental )
		{
			_environmentalPulseEnd = Time.Now + EnvironmentalPulseDuration;
		}
	}

	/// <summary>Total style multiplier bonus (additive, i.e. 0 = no bonus) for ScoreSystem to fold in.</summary>
	public float CurrentStyleBonus
	{
		get
		{
			var bonus = 0f;

			bonus += MathF.Min( _varietyStreak * VarietyBonusPerStreak, MaxVarietyBonus );

			if ( ComboSystem.Local is not null && ComboSystem.Local.Value >= ComboMaintainFloor )
				bonus += ComboMaintainBonus;

			if ( Time.Now < _finisherPulseEnd )
				bonus += FinisherPulseAmount;

			if ( Time.Now < _multiKillPulseEnd )
				bonus += _multiKillPulseAmount;

			if ( Time.Now < _environmentalPulseEnd )
				bonus += EnvironmentalPulseAmount;

			return bonus;
		}
	}

	public void ResetRun()
	{
		_varietyStreak = 0;
		_lastAttack = null;
		_finisherPulseEnd = -999f;
		_multiKillPulseEnd = -999f;
		_environmentalPulseEnd = -999f;
	}
}
