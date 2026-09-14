using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DrunkenBarFight;

/// <summary>
/// The player's chosen outfit + skin tone, persisted to disk (same FileSystem.Data + JSON pattern
/// as GameSettings) so it survives between sessions. Reuses the exact ClothingContainer/Toggle/
/// PrefersHuman/Apply pipeline already confirmed working for enemies in EnemyOutfitter - the only
/// difference is these picks are made explicitly by the player via the Customize screen instead of
/// randomly, and only one item is ever selected per slot (so "cycling" means walking through every
/// matching Clothing resource, one at a time, wrapping around).
///
/// Slot keywords are the same vocabulary already tuned against this project's actual clothing pack
/// (see EnemyOutfitter's console-log-driven history) - Title-based matching, not Clothing.Category,
/// since that's the one lookup path we've directly confirmed works on this engine version.
/// </summary>
public class PlayerOutfit
{
	public enum Slot { Skin, Top, Bottom, Shoes, Head, Accessory }

	public Dictionary<string, string> SelectedTitles { get; set; } = new();

	// Tracks *position in the options list* per slot, separately from SelectedTitles. Cycle() used to
	// re-derive "where am I" by searching for the stored title string each time, but this clothing
	// pack has multiple resources that share the exact same Title (recolor/variant pairs) - re-finding
	// by title always locks onto the FIRST matching entry, so pressing Next on a duplicate-named item
	// could get stuck bouncing between the two duplicates forever (reported: "right button not working
	// properly" on Skin/Bottom specifically, both of which hit this). Storing the index directly makes
	// Next/Prev always advance by exactly one regardless of duplicate names.
	public Dictionary<string, int> SelectedIndexBySlot { get; set; } = new();

	// Bumped on every change so MainMenu.razor's BuildHash can tell the panel needs a rebuild -
	// same "time bucket" style trick this project already uses elsewhere, just event-driven instead
	// of continuous.
	public int Version { get; set; }

	static readonly Dictionary<Slot, string[]> SlotKeywords = new()
	{
		[Slot.Skin] = new[] { "Human Skin", "Skin Tone", "Skintone" },
		[Slot.Top] = new[] { "Hawaiian", "T-Shirt", "Tee", "Shirt", "Hoodie", "Jumper", "Sweater", "Polo", "Tank", "Top", "Jacket", "Coat", "Vest", "Waistcoat", "Apron", "Blouse" },
		[Slot.Bottom] = new[] { "Jeans", "Trousers", "Pants", "Shorts", "Chinos", "Slacks", "Skirt", "Cargo" },
		[Slot.Shoes] = new[] { "Shoes", "Trainers", "Sneakers", "Boots", "Sandals", "Heels" },
		[Slot.Head] = new[] { "Cap", "Beanie", "Hat", "Hair", "Bandana", "Balaclava" },
		[Slot.Accessory] = new[] { "Glasses", "Watch", "Necklace", "Chain", "Gloves", "Bracelet" },
	};

	const string FileName = "player_outfit.json";

	static PlayerOutfit _instance;
	public static PlayerOutfit Instance => _instance ??= Load();

	static IReadOnlyList<Clothing> _allClothing;
	static IReadOnlyList<Clothing> AllClothing
	{
		get
		{
			if ( _allClothing is not null )
				return _allClothing;

			try { _allClothing = ResourceLibrary.GetAll<Clothing>()?.ToList() ?? new List<Clothing>(); }
			catch ( Exception ex )
			{
				Log.Warning( $"[PlayerOutfit] ResourceLibrary.GetAll<Clothing>() failed: {ex.Message}" );
				_allClothing = new List<Clothing>();
			}

			return _allClothing;
		}
	}

	/// <summary>Every clothing resource whose Title matches this slot's keywords, alphabetical.</summary>
	public static IReadOnlyList<Clothing> GetOptions( Slot slot )
	{
		var keywords = SlotKeywords[slot];
		return AllClothing
			.Where( c => !string.IsNullOrEmpty( c?.Title ) )
			.Where( c => keywords.Any( k => c.Title.Contains( k, StringComparison.OrdinalIgnoreCase ) ) )
			.OrderBy( c => c.Title, StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	public string GetSelectedTitle( Slot slot ) => SelectedTitles.TryGetValue( slot.ToString(), out var t ) ? t : null;

	public void CycleNext( Slot slot ) => Cycle( slot, 1 );
	public void CyclePrev( Slot slot ) => Cycle( slot, -1 );

	void Cycle( Slot slot, int direction )
	{
		try
		{
			var options = GetOptions( slot );
			if ( options.Count == 0 )
				return;

			// Every slot except Skin gets a "None" choice (represented as a null/empty title) so
			// clothing is fully optional - Skin always has something selected once cycled at least
			// once, since an empty skin slot has no meaningful "off" state.
			var titles = new List<string>();
			if ( slot != Slot.Skin )
				titles.Add( null );
			titles.AddRange( options.Select( o => o.Title ) );

			var key = slot.ToString();
			var index = SelectedIndexBySlot.TryGetValue( key, out var storedIndex ) ? storedIndex : ( slot == Slot.Skin ? -1 : 0 );

			index = ((index + direction) % titles.Count + titles.Count) % titles.Count;

			SelectedIndexBySlot[ key ] = index;
			SelectedTitles[ key ] = titles[ index ];

			Version++;
			Save();
			ApplyToLivePlayerIfPresent();
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[PlayerOutfit] Cycle failed for {slot}: {ex.Message}" );
		}
	}

	void ApplyToLivePlayerIfPresent()
	{
		var renderer = PlayerAnimationDriver.Local?.BodyRenderer;
		if ( renderer is not null )
			ApplyTo( renderer );

		// Also push to the Customize screen's dedicated preview-stand model (a separate renderer
		// from the real in-game player) so cycling a slot shows up immediately on the live 3D
		// preview instead of only affecting the player you'll see once a run actually starts.
		var previewRenderer = PlayerPreviewRotator.Instance?.BodyRenderer;
		if ( previewRenderer is not null )
			ApplyTo( previewRenderer );
	}

	static bool _loggedApplyDiagnostic;

	/// <summary>Builds a ClothingContainer from every selected title and applies it to the given
	/// renderer - call this once at game start (GameManager.StartGame already does) as well as live
	/// from the Customize screen so changes take effect immediately.</summary>
	public void ApplyTo( SkinnedModelRenderer renderer )
	{
		try
		{
			if ( renderer is null )
				return;

			var container = new ClothingContainer();
			var appliedAny = false;
			var appliedNames = new List<string>();

			foreach ( var kv in SelectedTitles )
			{
				if ( string.IsNullOrEmpty( kv.Value ) )
					continue;

				var item = AllClothing.FirstOrDefault( c => string.Equals( c.Title, kv.Value, StringComparison.OrdinalIgnoreCase ) );
				if ( item is null )
				{
					// If this fires for the Skin slot specifically, the stored title no longer matches
					// anything in AllClothing (pack changed, or a duplicate-title mixup) - the skin
					// selection would silently do nothing, which would look exactly like "skin tones
					// aren't working" even though clicking Next/Prev is changing SelectedTitles fine.
					if ( string.Equals( kv.Key, Slot.Skin.ToString(), StringComparison.OrdinalIgnoreCase ) )
						Log.Warning( $"[PlayerOutfit] Skin title '{kv.Value}' didn't resolve to any Clothing resource - skin tone will stay whatever it already was." );

					continue;
				}

				try
				{
					container.Toggle( item );
					appliedAny = true;
					appliedNames.Add( $"{kv.Key}={item.Title}" );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"[PlayerOutfit] container.Toggle failed for '{item.Title}': {ex.Message}" );
				}
			}

			// One-time (per game session) dump of exactly what got toggled and onto which renderer -
			// if skin tone visually never changes despite this log showing a different "Skin=Human
			// Skin N" title each time, the issue is that this pack's skin clothing items themselves
			// don't alter the citizen body material/texture (the same limitation that made skin-tone
			// variety unworkable for enemies earlier in this project) rather than anything in this
			// selection/apply code.
			if ( !_loggedApplyDiagnostic )
			{
				_loggedApplyDiagnostic = true;
				Log.Info( $"[PlayerOutfit] ApplyTo({renderer.GameObject?.Name}): toggled [{string.Join( ", ", appliedNames )}]." );
			}

			if ( !appliedAny )
				return;

			container.PrefersHuman = true;
			container.Apply( renderer );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[PlayerOutfit] ApplyTo failed: {ex.Message}" );
		}
	}

	static PlayerOutfit Load()
	{
		try
		{
			if ( FileSystem.Data.FileExists( FileName ) )
			{
				var json = FileSystem.Data.ReadAllText( FileName );
				var loaded = JsonSerializer.Deserialize<PlayerOutfit>( json );
				if ( loaded is not null )
					return loaded;
			}
		}
		catch { }

		return new PlayerOutfit();
	}

	public void Save()
	{
		try
		{
			var json = JsonSerializer.Serialize( this );
			FileSystem.Data.WriteAllText( FileName, json );
		}
		catch { }
	}
}
