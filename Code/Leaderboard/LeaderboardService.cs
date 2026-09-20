using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DrunkenBarFight;

/// <summary>
/// The only place in the codebase that talks to s&amp;box's online Stats/Leaderboards services.
/// Combat/scoring code never touches this directly - GameManager calls SubmitAndRankAsync once,
/// after a run ends, and that's the entire integration surface.
///
/// The game ident below must match the Org.Ident of the published package in
/// drunken_bar_fight.sbproj (currently "lastcall.last_call"). Until the game is published under
/// that ident on sbox.game there is no real leaderboard to submit to - everything below is
/// written against the real Sandbox.Services API and starts working the moment it is.
/// </summary>
public static class LeaderboardService
{
	/// <summary>The published package ident - must match Org.Ident in drunken_bar_fight.sbproj.</summary>
	public const string GameIdent = "lastcall.last_call";

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

			// Each run submits one "score", but the board's default aggregation is the SUM of every
			// submission - so a player's first run would rank against their combined lifetime total.
			// Max aggregation makes each player appear once with their single best run, which is what
			// a high-score board should mean.
			board.SetAggregationMax();
			board.SetSortDescending();
			board.MaxEntries = 100;

			// Center on the local player so their row is guaranteed to be inside the returned window
			// (Entries is a slice around TargetSteamId, not the whole top-100 list).
			board.CenterOnMe();
			await board.Refresh();

			var entries = board.Entries?.ToList();
			if ( entries is null || entries.Count == 0 )
				return (null, null);

			// Identify our own row by SteamId rather than matching the score value - value matching
			// breaks on ties and on aggregated values, and Board2.Entry has no Me flag. Convert.ToInt64
			// keeps this working whether the entry's SteamId is signed or unsigned. Entry is a struct,
			// so the rank is looked up via a nullable projection rather than `?.`.
			var mySteamId = System.Convert.ToInt64( Sandbox.Game.SteamId );
			int? rank = entries
				.Where( e => System.Convert.ToInt64( e.SteamId ) == mySteamId )
				.Select( e => (int?)e.Rank )
				.FirstOrDefault();

			// TotalEntries is the board-wide count; Entries.Count is only the returned window (<=100).
			return (rank, (int)board.TotalEntries);
		}
		catch
		{
			// Not published yet, offline, or the stat isn't configured - fail quietly.
			// The death screen just shows "Submitting..." forever in that case, which is fine for now.
			return (null, null);
		}
	}
}
