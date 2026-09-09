using System;

namespace DrunkenBarFight;

/// <summary>
/// Static event bus that decouples systems from one another (Combo, Drunkenness, Scoring,
/// Enemies, HUD, etc. all talk through here instead of holding direct references).
/// Every event is optional to listen to - nothing here is required for the game to run.
/// </summary>
public static class GameEvents
{
	/// <summary>Fired every time a landed hit extends the combo streak. Arg = the new streak count.</summary>
	public static event Action<int> ComboHit;
	public static void RaiseComboHit( int newStreak ) => ComboHit?.Invoke( newStreak );

	/// <summary>Fired when the combo streak breaks (no hit landed within the grace window).</summary>
	public static event Action ComboBroken;
	public static void RaiseComboBroken() => ComboBroken?.Invoke();

	/// <summary>Fired when the drink meter fills and the player drinks a glass. Arg = glass number this run.</summary>
	public static event Action<int> DrinkTriggered;
	public static void RaiseDrinkTriggered( int glassNumber ) => DrinkTriggered?.Invoke( glassNumber );

	/// <summary>Fired when drunkenness enters the dangerous "Last Call" band.</summary>
	public static event Action LastCallEntered;
	public static void RaiseLastCallEntered() => LastCallEntered?.Invoke();

	/// <summary>Fired when drunkenness drops back out of "Last Call" (e.g. sobered up).</summary>
	public static event Action LastCallExited;
	public static void RaiseLastCallExited() => LastCallExited?.Invoke();

	/// <summary>Fired the instant the player crosses maximum drunkenness. The run ends immediately.</summary>
	public static event Action PlayerOverdrunk;
	public static void RaiseOverdrunk() => PlayerOverdrunk?.Invoke();

	/// <summary>Fired when the player's HP reaches zero.</summary>
	public static event Action PlayerDied;
	public static void RaisePlayerDied() => PlayerDied?.Invoke();

	/// <summary>Fired whenever an enemy dies. Passes whether it was a sobering enemy, a finisher kill, and an environmental kill.</summary>
	public static event Action<EnemyKillInfo> EnemyKilled;
	public static void RaiseEnemyKilled( EnemyKillInfo info ) => EnemyKilled?.Invoke( info );

	/// <summary>Fired whenever a finisher successfully executes. Passes the finisher and how many enemies it hit.</summary>
	public static event Action<FinisherDefinition, int> FinisherExecuted;
	public static void RaiseFinisherExecuted( FinisherDefinition def, int enemiesHit ) => FinisherExecuted?.Invoke( def, enemiesHit );

	/// <summary>Fired whenever the player successfully starts a basic attack (not a finisher). Arg = whether it varied from the previous attack.</summary>
	public static event Action<AttackId, bool> AttackPerformed;
	public static void RaiseAttackPerformed( AttackId id, bool wasVaried ) => AttackPerformed?.Invoke( id, wasVaried );

	/// <summary>Fired when a milestone is reached, with a short display string for the HUD toast.</summary>
	public static event Action<string> Milestone;
	public static void RaiseMilestone( string text ) => Milestone?.Invoke( text );

	/// <summary>Fired once, with final run stats, right as the run ends (death or overdrunk).</summary>
	public static event Action<RunStats> RunEnded;
	public static void RaiseRunEnded( RunStats stats ) => RunEnded?.Invoke( stats );

	/// <summary>Fired when a new run starts (including restarts).</summary>
	public static event Action RunStarted;
	public static void RaiseRunStarted() => RunStarted?.Invoke();

	/// <summary>Ask the camera/hit-feedback system to shake. Amount is roughly in world units, duration in seconds.</summary>
	public static event Action<float, float> ShakeRequested;
	public static void RaiseShakeRequested( float amount, float duration ) => ShakeRequested?.Invoke( amount, duration );

	/// <summary>Ask for a brief hit-stop (time scale dip). Duration in seconds (real time).</summary>
	public static event Action<float> HitStopRequested;
	public static void RaiseHitStopRequested( float duration ) => HitStopRequested?.Invoke( duration );
}

/// <summary>Small payload describing an enemy death, used for scoring/style/drunkenness hooks.</summary>
public struct EnemyKillInfo
{
	public EnemyBase Enemy;
	public bool WasFinisher;
	public bool WasEnvironmental;
	public bool WasSobering;
	public int MultiKillIndex; // 0 = first kill in this window, 1 = second, etc.
}
