using System;

namespace DrunkenBarFight;

/// <summary>
/// Broken-fluorescent style flicker for a PointLight - drop it on any GameObject that has one and
/// it modulates that light's authored color each frame: a layered-sine base so the shimmer reads as
/// organic rather than a mechanical square wave, a smoothed random jitter on top for the stuttery
/// "bad tube" feel, and occasional deep dips almost to black (like the tube cutting out for a few
/// frames). Fully tunable per-light via its properties, purely decorative, and wrapped in try/catch
/// so a bad light config can never break anything else (same convention as AmbientEmbers/Vfx).
/// </summary>
public class LightFlickerComponent : Component
{
	[Property] public float MinBrightness { get; set; } = 0.3f;
	[Property] public float MaxBrightness { get; set; } = 1f;
	[Property] public float FlickerSpeed { get; set; } = 14f;
	[Property] public float JitterAmount { get; set; } = 0.4f;

	/// <summary>Offsets the base wave, so lights sharing settings don't flicker in lockstep (they all
	/// run off the global scene clock otherwise).</summary>
	[Property] public float PhaseOffset { get; set; }

	/// <summary>Probability per second of a deep dip starting - set 0 for a steady, dip-free flicker.</summary>
	[Property] public float DipChance { get; set; } = 0.3f;
	[Property] public float DipMinDuration { get; set; } = 0.06f;
	[Property] public float DipMaxDuration { get; set; } = 0.2f;

	/// <summary>Brightness held while a dip is active - small but not zero reads as a dying tube
	/// rather than the light turning fully off.</summary>
	[Property, Range( 0f, 1f )] public float DipLevel { get; set; } = 0.02f;

	PointLight _light;
	Color _baseColor;
	float _jitter;
	float _dipEndTime;

	protected override void OnAwake()
	{
		_light = Components.Get<PointLight>();
		if ( _light is not null )
			_baseColor = _light.LightColor;
	}

	protected override void OnUpdate()
	{
		if ( _light is null )
			return;

		try
		{
			var inDip = Time.Now < _dipEndTime;

			if ( !inDip && DipChance > 0f && Random.Shared.NextSingle() < DipChance * Time.Delta )
			{
				_dipEndTime = Time.Now + DipMinDuration + Random.Shared.NextSingle() * MathF.Max( 0f, DipMaxDuration - DipMinDuration );
				inDip = true;
			}

			float brightness;
			if ( inDip )
			{
				brightness = DipLevel;
			}
			else
			{
				var t = Time.Now * FlickerSpeed + PhaseOffset;
				// Three detuned sines folded into ~0-1: gives a pseudo-random but continuous wave
				// (the "shimmer") instead of a single sine's predictable bob.
				var wave = MathF.Sin( t ) + 0.7f * MathF.Sin( t * 1.7f + 1.3f ) + 0.5f * MathF.Sin( t * 3.1f + 0.7f );
				var norm = (wave + 2.2f) / 4.4f;

				// Smooth the per-frame random target so the noise is a stutter, not full white noise.
				var lerpAmount = System.Math.Clamp( Time.Delta * 18f, 0f, 1f );
				_jitter += ((Random.Shared.NextSingle() * 2f - 1f) - _jitter) * lerpAmount;

				brightness = MinBrightness + (MaxBrightness - MinBrightness) * (norm + _jitter * JitterAmount);
				brightness = System.Math.Clamp( brightness, MinBrightness, MaxBrightness );
			}

			_light.LightColor = _baseColor * brightness;
		}
		catch { }
	}
}