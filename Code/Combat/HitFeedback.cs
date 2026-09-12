namespace DrunkenBarFight;

/// <summary>
/// Central place that turns "an attack connected" into juice: screenshake + a short per-enemy
/// freeze (our stand-in for hit-stop/impact frames, see EnemyBase.FreezeUntil) + sound cues.
/// No gameplay state lives here - it only requests feedback through GameEvents so the camera
/// rig (or anything else) can react without this class needing a direct reference to it.
/// </summary>
public static class HitFeedback
{
	public static void PlayAttackImpact( float impactStrength, int hitCount )
	{
		if ( hitCount <= 0 )
			return;

		var shakeAmount = 2.5f + impactStrength * 9f + (hitCount > 1 ? 2f : 0f);
		var shakeDuration = 0.08f + impactStrength * 0.14f;
		GameEvents.RaiseShakeRequested( shakeAmount, shakeDuration );
		GameEvents.RaiseHitStopRequested( impactStrength * 0.1f );

		PlayImpactSound( impactStrength );
	}

	public static void PlayFinisherImpact( float impactStrength, int hitCount )
	{
		var shakeAmount = 8f + impactStrength * 14f + hitCount * 2f;
		var shakeDuration = 0.18f + impactStrength * 0.2f;
		GameEvents.RaiseShakeRequested( shakeAmount, shakeDuration );
		GameEvents.RaiseHitStopRequested( 0.18f );

		Sound.Play( "sounds/combat/finisher_impact.sound" );
	}

	/// <summary>The player's own "ouch" feedback - shake plus a hurt sound, scaled by how big a
	/// chunk of max HP the hit took (0-1). Kept separate from PlayAttackImpact/PlayFinisherImpact
	/// since this is feedback for damage taken, not damage dealt.</summary>
	public static void PlayPlayerHurt( float severity01 )
	{
		var shakeAmount = 4f + severity01 * 16f;
		var shakeDuration = 0.1f + severity01 * 0.16f;
		GameEvents.RaiseShakeRequested( shakeAmount, shakeDuration );

		if ( severity01 >= 0.12f )
			Sound.Play( "sounds/combat/heavy_impact.sound" );
		else
			Sound.Play( "sounds/combat/light_impact.sound" );
	}

	static void PlayImpactSound( float impactStrength )
	{
		if ( impactStrength >= 0.75f )
			Sound.Play( "sounds/combat/heavy_impact.sound" );
		else if ( impactStrength >= 0.4f )
			Sound.Play( "sounds/combat/medium_impact.sound" );
		else
			Sound.Play( "sounds/combat/light_impact.sound" );
	}
}
