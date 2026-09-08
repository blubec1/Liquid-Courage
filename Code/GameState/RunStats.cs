namespace DrunkenBarFight;

/// <summary>Plain data snapshot of a completed run, shown on the death screen and submitted to the leaderboard.</summary>
public class RunStats
{
	public long FinalScore;
	public float HighestCombo;
	public float HighestDrunkenness;
	public int FinishersUsed;
	public int EnemiesDefeated;
	public float SurvivalTime;
	public int MilestoneReached;
	public bool DiedFromOverdrunk;

	// Filled in after leaderboard submission completes (or left null if unavailable).
	public int? LeaderboardRank;
	public int? LeaderboardTotalPlayers;
	public bool LeaderboardAttempted;
}
