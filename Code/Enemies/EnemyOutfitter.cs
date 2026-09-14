using System;
using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Dresses an enemy's Citizen model in real clothing (shirts/jackets/aprons/etc.) instead of just a
/// flat tint, using s&box's ClothingContainer + whatever Clothing resources happen to be mounted in
/// this project. Deliberately does NOT hardcode exact asset paths (e.g. "citizen_clothes/x.clothing")
/// since we can't see this project's actual content browser from here - instead it enumerates every
/// compiled Clothing resource via ResourceLibrary.GetAll and matches by a loose, case-insensitive
/// Title keyword search, so it keeps working regardless of exactly which clothing pack/version is
/// installed.
///
/// Confirmed working via console + in-game report: ResourceLibrary.GetAll&lt;Clothing&gt;(),
/// ClothingContainer.Toggle/PrefersHuman/Apply, and Clothing.Title are all real on this engine
/// version. Two rounds of feedback since:
///   1. "Only 2 outfits" - fixed by picking randomly per-instance from the whole matching pool
///      instead of always the alphabetically-first match for every enemy of an archetype.
///   2. "Make more, and vary skin tone - currently all black" - addressed below by (a) a much wider
///      keyword net per slot, (b) an extra "wildcard" slot per enemy that grabs a random unused item
///      from the ENTIRE pool regardless of keyword, and (c) loud one-time diagnostic logging of the
///      actual clothing titles available, since "all black" most likely means our skin-tone keywords
///      aren't matching anything real in this pack (or possibly that the retargeted player model's
///      base material itself is the black you're seeing, independent of clothing entirely - the
///      first-run log below will show which one it is).
/// </summary>
public static class EnemyOutfitter
{
	static IReadOnlyList<Clothing> _all;
	static bool _lookedUp;
	static bool _loggedSummary;
	static bool _loggedSkinDiagnostic;

	// Narrowed based on real console output: this pack names its skin tones "Human Skin 1/2/3/...".
	// The earlier broad net (Brown/Dark/Tan/Light/etc.) was a mistake - it mostly matched HAIR
	// styles instead ("Long Brown", "Balding Brown", "Scruffy Short Brown" are all hairstyles, not
	// skin), so the real "Human Skin N" item usually lost the random draw to a hair item that got
	// toggled into the outfit without actually changing skin tone at all - which is exactly why
	// enemies stayed a uniform dark default color no matter what got picked. "Human Skin" alone is
	// specific enough to only match the real tone variants.
	static readonly string[] SkinToneKeywords = { "Human Skin", "Skin Tone", "Skintone" };

	// Skin-tone tinting via SkinnedModelRenderer.Tint was reverted after it caused more problems than
	// it solved (bloom/hue-shift artifacts, lighting washing out the difference, and finally a solid
	// black balaclava-type clothing item rendering as an oversized mask over the whole head, which
	// looked like broken skin rather than a hood). Enemies now just keep whatever Tint their
	// archetype's ApplyVisual() sets (currently plain white/untouched) plus whatever the "Human Skin
	// N" clothing item naturally looks like - no forced tint on top.

	static IReadOnlyList<Clothing> All
	{
		get
		{
			if ( _lookedUp )
				return _all;

			_lookedUp = true;
			try { _all = ResourceLibrary.GetAll<Clothing>()?.ToList(); }
			catch ( Exception ex )
			{
				Log.Warning( $"[Outfit] ResourceLibrary.GetAll<Clothing>() isn't available on this s&box version ({ex.Message}) - enemies will keep their flat tint instead of real clothing." );
				_all = null;
			}

			if ( !_loggedSummary )
			{
				_loggedSummary = true;
				Log.Info( $"[Outfit] Found {_all?.Count ?? 0} clothing resources available to draw from." );

				// Dump every title once so we can see the pack's real naming convention instead of
				// guessing blind - if skin tone still isn't varying after this, whatever's actually
				// meant for that in your pack will be visible in this list.
				if ( _all is not null && _all.Count > 0 )
				{
					var titles = _all.Where( c => !string.IsNullOrEmpty( c?.Title ) ).Select( c => c.Title ).OrderBy( t => t, StringComparer.OrdinalIgnoreCase );
					Log.Info( $"[Outfit] All available clothing titles: {string.Join( " | ", titles )}" );

					// Shorter, targeted list specifically for skin tone - this is the number that
					// actually determines how much tone variety is even possible. If this only ever
					// prints one item, every enemy getting the "same" tone isn't a bug, it's the only
					// option that exists in this content pack.
					var skinItems = _all
						.Where( c => !string.IsNullOrEmpty( c?.Title ) )
						.Where( c => SkinToneKeywords.Any( k => c.Title.Contains( k, StringComparison.OrdinalIgnoreCase ) ) )
						.Select( c => c.Title )
						.OrderBy( t => t, StringComparer.OrdinalIgnoreCase )
						.ToList();
					Log.Info( $"[Outfit] Skin-tone candidates found ({skinItems.Count}): {string.Join( " | ", skinItems )}" );
				}
			}

			return _all;
		}
	}

	/// <summary>
	/// Applies one randomly-picked item per slot group (each group is a list of alternative
	/// keywords for the same conceptual slot - all matches across every keyword in the group are
	/// pooled together and one is picked at random) plus a randomized skin tone, plus a chance at
	/// one extra "wildcard" item pulled from the entire pool regardless of keyword, for variety that
	/// isn't capped by how well our guessed keywords match this pack. Call once per spawned enemy -
	/// the randomness is what creates per-instance variety.
	/// </summary>
	public static void Apply( SkinnedModelRenderer renderer, string archetypeLabel, params string[][] slotKeywordGroups )
	{
		try
		{
			if ( renderer is null )
				return;

			var pool = All;
			if ( pool is null || pool.Count == 0 )
				return;

			var container = new ClothingContainer();
			var appliedNames = new List<string>();
			var usedTitles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var rng = Random.Shared;

			// Skin tone first so it's never crowded out by a slot running out of matches.
			var skinApplied = TryApplySlot( container, pool, SkinToneKeywords, usedTitles, appliedNames, rng );

			if ( !skinApplied && !_loggedSkinDiagnostic )
			{
				_loggedSkinDiagnostic = true;
				Log.Warning( "[Outfit] No clothing title matched any skin-tone keyword - if enemies all look the same color, this pack likely names tone variants differently (or reskinning isn't done via clothing titles at all here). Check the '[Outfit] All available clothing titles' log line above and tell me what the real tone items are called." );
			}

			for ( int i = 0; i < slotKeywordGroups.Length; i++ )
			{
				var hit = TryApplySlot( container, pool, slotKeywordGroups[ i ], usedTitles, appliedNames, rng );

				// The first two slots passed in are always "top" and "bottom" by convention at every
				// call site - those are the two that make a character read as bare-naked if they
				// come up empty (a screenshot showed exactly this: a fully bare pink body with no
				// clothing at all). If the specific keywords whiff, force ANY unused item rather than
				// leaving that body region uncovered.
				if ( !hit && i < 2 )
					ApplyWildcard( container, pool, usedTitles, appliedNames, rng );
			}

			// Wildcard: on top of the named slots, ~55% chance to grab one more totally random unused
			// item from the whole pool - not filtered by keyword at all. This is what actually pushes
			// "more variety" beyond whatever our specific keyword guesses happen to catch.
			if ( rng.NextDouble() < 0.55 )
				ApplyWildcard( container, pool, usedTitles, appliedNames, rng );

			if ( appliedNames.Count == 0 )
			{
				Log.Info( $"[Outfit] {archetypeLabel}: no matching clothing found for any requested slot - keeping flat tint." );
				return;
			}

			container.PrefersHuman = true;
			container.Apply( renderer );

			Log.Info( $"[Outfit] {archetypeLabel}: applied [{string.Join( ", ", appliedNames )}]." );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Outfit] Failed to dress {archetypeLabel}, falling back to flat tint: {ex.Message}" );
		}
	}

	static void ApplyWildcard( ClothingContainer container, IReadOnlyList<Clothing> pool, HashSet<string> usedTitles, List<string> appliedNames, Random rng )
	{
		var candidates = pool.Where( c => !string.IsNullOrEmpty( c?.Title ) && !usedTitles.Contains( c.Title ) ).ToList();
		if ( candidates.Count == 0 )
			return;

		var pick = candidates[ rng.Next( candidates.Count ) ];
		try
		{
			container.Toggle( pick );
			usedTitles.Add( pick.Title );
			appliedNames.Add( pick.Title );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Outfit] wildcard container.Toggle failed for '{pick.Title}': {ex.Message}" );
		}
	}

	static bool TryApplySlot( ClothingContainer container, IReadOnlyList<Clothing> pool, string[] keywords,
		HashSet<string> usedTitles, List<string> appliedNames, Random rng )
	{
		var picked = PickRandomMatch( pool, keywords, usedTitles, rng );
		if ( picked is null )
			return false;

		try
		{
			container.Toggle( picked );
			usedTitles.Add( picked.Title );
			appliedNames.Add( picked.Title );
			return true;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Outfit] container.Toggle failed for '{picked.Title}': {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Pools every clothing item whose Title contains ANY of the given keywords (not just the first
	/// keyword that gets a hit) and picks one at random, excluding anything already used elsewhere
	/// in this same outfit so the same exact item can't fill two slots at once.
	/// </summary>
	static Clothing PickRandomMatch( IReadOnlyList<Clothing> pool, string[] keywords, HashSet<string> usedTitles, Random rng )
	{
		var candidates = pool
			.Where( c => !string.IsNullOrEmpty( c?.Title ) && !usedTitles.Contains( c.Title ) )
			.Where( c => keywords.Any( k => c.Title.Contains( k, StringComparison.OrdinalIgnoreCase ) ) )
			.ToList();

		if ( candidates.Count == 0 )
			return null;

		return candidates[ rng.Next( candidates.Count ) ];
	}
}
