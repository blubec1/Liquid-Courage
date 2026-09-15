using System;
using System.Linq;

namespace DrunkenBarFight;

public enum RunState
{
	MainMenu,
	Playing,
	Paused,
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

	public RunState State { get; private set; } = RunState.MainMenu;
	public float SurvivalTime { get; private set; }
	public RunStats CurrentRunStats { get; private set; }
	public float TimeSinceRunEnded { get; private set; }

	public bool IsInMainMenu => State == RunState.MainMenu;
	public bool IsPaused => State == RunState.Paused;

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

		// RunStarted is now raised by StartGame() once Play is actually pressed, not here -
		// the game opens on the main menu instead of dropping straight into a run.
	}

	protected override void OnUpdate()
	{
		if ( State == RunState.Playing )
		{
			SurvivalTime += Time.Delta;
		}
		else if ( State == RunState.Dead )
		{
			TimeSinceRunEnded += Time.Delta;

			// Keyboard fallback for the death screen's Play Again button.
			if ( CanRestartYet && (Input.Pressed( "Punch" ) || Input.Pressed( "Kick" ) || Input.Pressed( "Heavy" ) || Input.Pressed( "Jump" )) )
				RestartRun();
		}

		// Escape toggles pause mid-run. Read raw/unrebindable (same pattern as the M/N drunkenness
		// cheat keys) rather than through Input.config, since this shouldn't be rebindable.
		try
		{
			if ( Sandbox.Input.Keyboard.Pressed( "Escape" ) && (State == RunState.Playing || State == RunState.Paused) )
				TogglePause();
		}
		catch { }
	}

	/// <summary>Called by MainMenu.razor's Play button. Flips MainMenu -> Playing and kicks off the run.</summary>
	public void StartGame()
	{
		if ( State != RunState.MainMenu )
			return;

		State = RunState.Playing;
		GameEvents.RaiseRunStarted();
	}

	/// <summary>Called by Escape mid-run (or the pause menu's Resume button). Flips Playing &harr; Paused.</summary>
	public void TogglePause()
	{
		if ( State == RunState.Playing )
			State = RunState.Paused;
		else if ( State == RunState.Paused )
			State = RunState.Playing;
	}

	/// <summary>Called by the pause menu's "Quit to Menu" button - same cleanup as a restart, but lands
	/// back on the main menu instead of a fresh run.</summary>
	public void QuitToMenu()
	{
		ClearRunObjects();

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
		State = RunState.MainMenu;
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

	/// <summary>Wipes all run-scoped state and puts the player back at the spawn point, then starts
	/// a fresh run immediately - used by the death screen's "Play Again".</summary>
	public void RestartRun()
	{
		if ( State != RunState.Dead )
			return;

		ClearRunObjects();

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

	/// <summary>Shared by RestartRun and QuitToMenu: clears any leftover enemies/pickups/thrown props.</summary>
	void ClearRunObjects()
	{
		foreach ( var enemy in Scene.GetAllComponents<EnemyBase>().ToList() )
			enemy.GameObject.Destroy();

		foreach ( var pickup in Scene.GetAllComponents<SoberingPickup>().ToList() )
			pickup.GameObject.Destroy();

		foreach ( var thrown in Scene.GetAllComponents<ThrownBottle>().ToList() )
			thrown.GameObject.Destroy();
	}

	public bool CanRestartYet => State == RunState.Dead && TimeSinceRunEnded >= RestartInputDelay;
}
