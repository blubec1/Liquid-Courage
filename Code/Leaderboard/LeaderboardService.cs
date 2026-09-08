using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DrunkenBarFight;

/// <summary>
/// The only place in the codebase that talks to s&amp;box's online Stats/Leaderboards services.
/// Combat/scoring code never touches this directly - GameManager calls SubmitAndRankAsync once,
/// after a run ends, and that's the entire integration surface.
///
/// IMPORTANT: this project is currently registered under the "local" org (see drunken_bar_fight.sbproj),
/// which is not a published game, so there is no real leaderboard to submit to yet. Everything
/// below is written against the real Sandbox.Services API and will start working the moment you
/// publish the project under a real org and set GameIdent below to match - see SETUP.md.
/// </summary>
public static class LeaderboardService
{
	/// <summary>Update this to "yourorg.drunken_bar_fight" once you've published the game on sbox.game.</summary>
	public const string GameIdent = "local.drunken_bar_fight";

	public const string ScoreStatName = "score";

	public static async Task<(int? rank, int? total)> SubmitAndRankAsync( RunStats stats )
	{
		try
		{
			var extra = new Dictionary<string, object>
			{
				{ "combo", stats.HighestCombo },
				{ "drunkenness", stats.HighestDrunkenness },
				{ "finishers", stats.FinishersUsed },
				{ "survivalTime", stats.SurvivalTime },
			};

			Sandbox.Services.Stats.SetValue( ScoreStatName, stats.FinalScore, extra );
			await Sandbox.Services.Stats.FlushAsync();

			var board = Sandbox.Services.Leaderboards.GetFromStat( GameIdent, ScoreStatName );
			board.MaxEntries = 100;
			await board.Refresh();

			var entries = board.Entries?.ToList();
			if ( entries is null || entries.Count == 0 )
				return (null, null);

			// Best-effort match on the score we just submitted (exact identity lookup varies by
			// s&box version, so this avoids depending on an unconfirmed "local player" API).
			// Entry is a struct, so we look up the rank via a nullable projection rather than `?.`.
			int? rank = entries
				.Where( e => e.Value == stats.FinalScore )
				.Select( e => (int?)e.Rank )
				.FirstOrDefault();

			return (rank, entries.Count);
		}
		catch
		{
			// Not published yet, offline, or the stat isn't configured - fail quietly.
			// The death screen just shows "Submitting..." forever in that case, which is fine for now.
			return (null, null);
		}
	}
}
