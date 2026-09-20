namespace DrunkenBarFight;

/// <summary>
/// Central place that turns "an attack connected" into juice: screenshake + a short per-enemy
/// freeze (see EnemyBase.FreezeUntil) + sound cues.
/// No gameplay state lives here - it only requests feedback through GameEvents so the camera
/// rig (or anything else) can react without this class needing a direct reference to it.
/// </summary>
public static class HitFeedback
{
	/// <summary>soundOverride lets a specific attack force its own impact cue instead of picking one
	/// off the impactStrength thresholds below - used by Kick, which wants the light-punch sound
	/// even though its ImpactStrength (0.4, for shake feel) would otherwise land it in the
	/// "medium" bucket.</summary>
	public static void PlayAttackImpact( float impactStrength, int hitCount, string soundOverride = null )
	{
		if ( hitCount <= 0 )
			return;

		var shakeAmount = 1.5f + impactStrength * 6f + (hitCount > 1 ? 1.5f : 0f);
		var shakeDuration = 0.07f + impactStrength * 0.12f;
		GameEvents.RaiseShakeRequested( shakeAmount, shakeDuration );

		if ( !string.IsNullOrEmpty( soundOverride ) )
			GameSettings.PlaySound( soundOverride );
		else
			PlayImpactSound( impactStrength );
	}

	/// <summary>Finisher impacts now select their cue off impactStrength through the same
	/// light/medium/heavy buckets as normal attacks (PlayImpactSound), so a rapid-combo jab and the
	/// haymaker's closer no longer bang identically - the heavy bucket is the "extra heavy hit", and
	/// it lands on the end of the haymaker and the slam. soundOverride forces a specific cue for a
	/// finisher that wants one regardless of strength.</summary>
	public static void PlayFinisherImpact( float impactStrength, int hitCount, string soundOverride = null )
	{
		var shakeAmount = 5f + impactStrength * 9f + hitCount * 1.5f;
		var shakeDuration = 0.15f + impactStrength * 0.16f;
		GameEvents.RaiseShakeRequested( shakeAmount, shakeDuration );

		if ( !string.IsNullOrEmpty( soundOverride ) )
			GameSettings.PlaySound( soundOverride );
		else
			PlayImpactSound( impactStrength );
	}

	/// <summary>Plays an attack/finisher's swing ("whoosh") cue at the moment the move is thrown.
	/// Unlike PlayAttackImpact this deliberately does NOT gate on hit count - the whole point is that a
	/// whiffed swing still makes a noise. Null/empty is a no-op, so any definition without a cue
	/// assigned stays silent. pitchVariation adds a small random detune so rapid repeats (Flurry Jab,
	/// AoE pulses) don't sound machine-gunned.</summary>
	public static void PlaySwing( string sound, float pitchVariation = 0f )
	{
		if ( string.IsNullOrEmpty( sound ) )
			return;

		var pitch = 1f;
		if ( pitchVariation > 0f )
			pitch += (System.Random.Shared.NextSingle() * 2f - 1f) * pitchVariation;

		GameSettings.PlaySound( sound, pitch );
	}

	/// <summary>The player's own "ouch" feedback - shake plus a hurt sound, scaled by how big a
	/// chunk of max HP the hit took (0-1). Kept separate from PlayAttackImpact/PlayFinisherImpact
	/// since this is feedback for damage taken, not damage dealt.
	///
	/// Bumped from 4/16 (amount) and 0.1/0.16 (duration) - the old floor was so low that small chip
	/// hits (the common case when surrounded) barely registered. Still meant to read as a "slight"
	/// jolt, not a disorienting wallop - PlayFinisherImpact above is deliberately the bigger one.</summary>
	public static void PlayPlayerHurt( float severity01 )
	{
		var shakeAmount = 4f + severity01 * 12f;
		var shakeDuration = 0.1f + severity01 * 0.14f;
		GameEvents.RaiseShakeRequested( shakeAmount, shakeDuration );

		// One shared "getting hit" cue regardless of severity (previously split into light/heavy
		// variants, but only one clip exists) - separate from the sounds played when the PLAYER lands
		// a hit (PlayImpactSound below). Routed through GameSettings.PlaySound (not raw Sound.Play) so
		// it respects the master volume slider like every other cue should.
		GameSettings.PlaySound( "sounds/combat/player_hurt.sound" );
	}

	static void PlayImpactSound( float impactStrength )
	{
		if ( impactStrength >= 0.75f )
			GameSettings.PlaySound( "sounds/combat/heavy_impact.sound" );
		else if ( impactStrength >= 0.4f )
			GameSettings.PlaySound( "sounds/combat/medium_impact.sound" );
		else
			GameSettings.PlaySound( "sounds/combat/light_impact.sound" );
	}
}
