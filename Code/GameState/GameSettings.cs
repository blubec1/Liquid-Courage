using System;
using System.Text.Json;

namespace DrunkenBarFight;

/// <summary>
/// Persisted player preferences: master volume, resolution, fullscreen. Saved to a small JSON
/// file via FileSystem.Data (the documented sandboxed save-data API), loaded once and kept as a
/// singleton so both the main menu and pause menu read/write the same values.
///
/// Master and SFX volume actually work (applied to every Sound.Play call via PlaySound() below);
/// Music volume is pushed at the engine's global music player in the same setters.
/// Resolution/fullscreen are saved as a preference but NOT applied to the real window - s&box's
/// sandboxed game-code compiler whitelists which APIs addon code can call (SB1000 errors on
/// anything not allowed) and specifically closes off reflection/invocation-style APIs, and actual
/// display/render settings live in the engine layer outside that sandbox. There's also a known
/// Facepunch bug (sbox-public#9943) where changing the active viewport resolution while playing
/// inside the editor can softlock the editor itself, which is another reason not to guess at this.
/// See ApplyVideo() below.
/// </summary>
public class GameSettings
{
	public float MasterVolume { get; set; } = 1f;
	public float SfxVolume { get; set; } = 1f;
	public float MusicVolume { get; set; } = 1f;
	public int ResolutionIndex { get; set; } = 2;
	public bool Fullscreen { get; set; } = true;

	public static readonly (int Width, int Height)[] Resolutions =
	{
		(1280, 720),
		(1600, 900),
		(1920, 1080),
		(2560, 1440),
		(3840, 2160),
	};

	public string ResolutionLabel( int index )
	{
		if ( index < 0 || index >= Resolutions.Length )
			return "?";

		var r = Resolutions[index];
		return $"{r.Width} x {r.Height}";
	}

	const string FileName = "settings.json";

	static GameSettings _instance;
	public static GameSettings Instance => _instance ??= Load();

	static GameSettings Load()
	{
		try
		{
			if ( FileSystem.Data.FileExists( FileName ) )
			{
				var json = FileSystem.Data.ReadAllText( FileName );
				var loaded = JsonSerializer.Deserialize<GameSettings>( json );
				if ( loaded is not null )
					return loaded;
			}
		}
		catch { }

		return new GameSettings();
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

	public void SetMasterVolume( float value )
	{
		MasterVolume = Math.Clamp( value, 0f, 1f );
		ApplyMusicVolume();
		Save();
	}

	public void SetSfxVolume( float value )
	{
		SfxVolume = Math.Clamp( value, 0f, 1f );
		Save();
	}

	public void SetMusicVolume( float value )
	{
		MusicVolume = Math.Clamp( value, 0f, 1f );
		ApplyMusicVolume();
		Save();
	}

	/// <summary>
	/// Push MasterVolume * MusicVolume at anything that doesn't route through PlaySound. The global
	/// music player is engine-side and outlives the scene, so it won't pick the sliders up on its
	/// own. Guarded because music may not be playing (menu boot, headless) - setting the volume is
	/// still harmless then.
	/// </summary>
	void ApplyMusicVolume()
	{
		try
		{
			Game.Music.Volume = MasterVolume * MusicVolume;
		}
		catch { }
	}

	public void CycleResolution()
	{
		ResolutionIndex = (ResolutionIndex + 1) % Resolutions.Length;
		Save();
		ApplyVideo();
	}

	public void SetFullscreen( bool value )
	{
		Fullscreen = value;
		Save();
		ApplyVideo();
	}

	/// <summary>
	/// Deliberately does NOT touch the actual window/monitor here. s&box's sandboxed game-code
	/// compiler enforces an API whitelist (SB1000 errors on anything not allowed) specifically to
	/// close off reflection/invocation-style APIs for security reasons, and engine-level render
	/// settings live outside that sandbox entirely - so there's no safe, whitelist-friendly way to
	/// flip the real monitor resolution/fullscreen state from addon code. Resolution and Fullscreen
	/// here are saved as a preference (see Save()) so the UI remembers the player's choice, but
	/// actually applying it is s&box's own video settings (outside this game), not something this
	/// code can reach. If a real, whitelisted API for this exists in your engine version, tell me
	/// what it's called and I'll wire it in directly instead of leaving this as a preference-only
	/// stub.
	/// </summary>
	public void ApplyVideo()
	{
		// Intentionally inert - see remarks above.
	}

	/// <summary>Loudness multiplier applied to every cue on top of the master volume slider. The
	/// combat stinger WAVs are short 8-bit placeholders that peak at only ~30-65% of full scale, so
	/// a little makeup gain is needed to make hits read. SoundHandle.Volume is an unclamped float
	/// multiplier, so this can go above 1 - raise it if impacts still feel weak (clipping is the
	/// only downside).</summary>
	public const float SfxGain = 2.5f;

	/// <summary>Volume-aware sound wrapper - use this instead of raw Sound.Play everywhere so the
	/// master volume slider actually affects every sound cue in the game. <paramref name="pitch"/> is
	/// the playback rate multiplier (1 = normal); callers that fire the same cue rapidly pass a small
	/// variation so repeats don't phase into a machine-gun stutter.</summary>
	public static void PlaySound( string soundPath, float pitch = 1f )
	{
		try
		{
			var handle = Sound.Play( soundPath );
			if ( handle is null )
				return;

			// Sound.Play(string) builds a fully-3D handle parked at the world origin (0,0,0) with
			// distance attenuation/occlusion/reverb left at the SoundEvent's defaults. The bar arena
			// sits ~1400+ units from the origin, so the default falloff curve cut every cue down to a
			// small fraction of its volume - "the sounds are way too quiet". Everything routed here is
			// a player-local stinger (combat impacts, UI clicks), never a world-positioned emitter, so
			// force it fully 2D and bypass distance/occlusion entirely.
			handle.ListenLocal = true;
			handle.SpacialBlend = 0f;
			handle.DistanceAttenuation = false;
			handle.OcclusionEnabled = false;
			handle.AirAbsorption = false;

			// Multiply rather than assign so any per-SoundEvent volume baked into the .sound asset is
			// preserved instead of being overwritten.
			handle.Volume *= Instance.MasterVolume * Instance.SfxVolume * SfxGain;

			if ( pitch != 1f )
				handle.Pitch = pitch;
		}
		catch { }
	}
}
