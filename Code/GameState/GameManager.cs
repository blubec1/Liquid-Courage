using System;
using System.Linq;

namespace DrunkenBarFight;

public enum RunState
{
	Playing,
	Dead
}

public enum EndReason
{
	Killed,
	Overdrunk
}

/// <summary>
/// Top level run/game-state manager. Doesn't know anything about combat internals - it just
/// orchestrates start/end/restart of a run and holds the final stats snapshot. Everything it
/// needs from other systems goes through their static singleton accessors or GameEvents.
/// </summary>
public class GameManager : Component
{
	public static GameManager Instance { get; private set; }

	[Property, Group( "Tuning" )] public float RestartInputDelay { get; set; } = 0.5f;

	public RunState State { get; private set; } = RunState.Playing;
	public float SurvivalTime { get; private set; }
	public RunStats CurrentRunStats { get; private set; }
	public float TimeSinceRunEnded { get; private set; }

	Vector3 _playerSpawnPosition;
	Rotation _playerSpawnRotation;
	bool _hasSpawn;

	protected override void OnStart()
	{
		Instance = this;

		var player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault();
		if ( player is not null )
		{
			_playerSpawnPosition = player.WorldPosition;
			_playerSpawnRotation = player.WorldRotation;
			_hasSpawn = true;
		}

		GameEvents.RaiseRunStarted();
	}

	protected override void OnUpdate()
	{
		if ( State == RunState.Playing )
		{
			SurvivalTime += Time.Delta;
		}
		else
		{
			TimeSinceRunEnded += Time.Delta;

			// Keyboard fallback for the death screen's Play Again button.
			if ( CanRestartYet && (Input.Pressed( "Punch" ) || Input.Pressed( "Kick" ) || Input.Pressed( "Heavy" ) || Input.Pressed( "Jump" )) )
				RestartRun();
		}
	}

	/// <summary>Called by PlayerStats (HP hits 0) or DrunkennessSystem (max drunkenness) exactly once.</summary>
	public void EndRun( EndReason reason )
	{
		if ( State == RunState.Dead )
			return;

		State = RunState.Dead;
		TimeSinceRunEnded = 0;

		var stats = new RunStats
		{
			FinalScore = ScoreSystem.Local?.Score ?? 0,
			HighestCombo = ComboSystem.Local?.HighestValue ?? 0,
			HighestDrunkenness = DrunkennessSystem.Local?.HighestValue ?? 0,
			FinishersUsed = ScoreSystem.Local?.FinishersUsed ?? 0,
			EnemiesDefeated = ScoreSystem.Local?.EnemiesDefeated ?? 0,
			SurvivalTime = SurvivalTime,
			MilestoneReached = MilestoneSystem.Instance?.CurrentMilestoneIndex ?? 0,
			DiedFromOverdrunk = reason == EndReason.Overdrunk,
		};

		CurrentRunStats = stats;

		GameEvents.RaiseRunEnded( stats );

		_ = SubmitScoreAsync( stats );
	}

	async System.Threading.Tasks.Task SubmitScoreAsync( RunStats stats )
	{
		var result = await LeaderboardService.SubmitAndRankAsync( stats );
		stats.LeaderboardRank = result.rank;
		stats.LeaderboardTotalPlayers = result.total;
		stats.LeaderboardAttempted = true;
	}

	/// <summary>Wipes all run-scoped state and puts the player back at the spawn point.</summary>
	public void RestartRun()
	{
		if ( State != RunState.Dead )
			return;

		// Clear any leftover enemies / pickups / thrown props from the last run.
		foreach ( var enemy in Scene.GetAllComponents<EnemyBase>().ToList() )
			enemy.GameObject.Destroy();

		foreach ( var pickup in Scene.GetAllComponents<SoberingPickup>().ToList() )
			pickup.GameObject.Destroy();

		foreach ( var thrown in Scene.GetAllComponents<ThrownBottle>().ToList() )
			thrown.GameObject.Destroy();

		ComboSystem.Local?.ResetRun();
		DrunkennessSystem.Local?.ResetRun();
		ScoreSystem.Local?.ResetRun();
		StyleSystem.Local?.ResetRun();
		AttackStringSystem.Local?.ResetRun();
		HordeSpawner.Instance?.ResetRun();
		PickupSpawner.Instance?.ResetRun();
		MilestoneSystem.Instance?.ResetRun();

		if ( _hasSpawn && PlayerStats.Local is not null )
		{
			PlayerStats.Local.WorldPosition = _playerSpawnPosition;
			PlayerStats.Local.WorldRotation = _playerSpawnRotation;
		}

		PlayerStats.Local?.ResetForNewRun();

		SurvivalTime = 0;
		CurrentRunStats = null;
		State = RunState.Playing;

		GameEvents.RaiseRunStarted();
	}

	public bool CanRestartYet => State == RunState.Dead && TimeSinceRunEnded >= RestartInputDelay;
}
