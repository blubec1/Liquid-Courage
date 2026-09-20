namespace DrunkenBarFight;

/// <summary>
/// Plays the background track and keeps it looping for the whole session. Uses the engine's global
/// <see cref="Game.Music"/> player rather than a SoundHandle, so the song lives outside the scene and
/// keeps playing straight through menu -> run -> death with no per-state handling. Only one Game.Music
/// track exists at a time, so this is a single component on the Systems object and nothing else starts
/// music.
///
/// The file is OGG Vorbis (converted from the original 24-bit extensible WAV, which the asset compiler
/// may not import reliably). With no embedded loop points the track restarts at the end on loop;
/// ticking Loop in the s&box sound editor makes it gapless.
/// </summary>
public class MusicController : Component
{
	/// <summary>Raw audio asset path (Game.Music.Play uses SoundFile.Load, not SoundEvent.Find - so
	/// this is the .ogg file itself, not a .sound).</summary>
	[Property] public string Track { get; set; } = "sounds/song/song.ogg";

	/// <summary>Seconds to fade the track in when it first starts.</summary>
	[Property] public float FadeIn { get; set; } = 1f;

	/// <summary>Per-track loudness, on top of Game.Music.Volume (which mirrors the Master Volume
	/// slider) - use it to tame a loud master so music doesn't bury the combat cues.</summary>
	[Property] public float Gain { get; set; } = 0.8f;

	protected override void OnStart()
	{
		try
		{
			// Pick up whatever the player already set - OnStart runs once, the GameSettings setters
			// keep it in sync afterwards.
			Game.Music.Volume = GameSettings.Instance.MasterVolume * GameSettings.Instance.MusicVolume;
			Game.Music.Play( Track, FadeIn, loop: true, volume: Gain );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[MusicController] Failed to start music '{Track}': {ex.Message}" );
		}
	}
}
