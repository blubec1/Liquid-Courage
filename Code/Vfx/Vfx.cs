using System;

namespace DrunkenBarFight;

/// <summary>
/// Central entry point for every visual effect in the game - attack swipes, hit sparks, finisher
/// flashes, and enemy death bursts. Everything here is a Scene object, never a UI/HUD element, and
/// everything is wrapped in try/catch: a VFX call failing must never break combat (same convention
/// as PlayerAnimationDriver). This is purely decorative - no gameplay values live here.
/// </summary>
public static class Vfx
{
	/// <summary>A handful of light points swept along the attack's arc, at the moment it swings -
	/// reads as a quick weapon trail without needing any sprite/material assets.</summary>
	public static void AttackArc( Vector3 origin, Vector3 facing, float arcDegrees, float range, Color color )
	{
		try
		{
			const int points = 5;
			var forwardYaw = MathF.Atan2( facing.y, facing.x );
			var halfArc = (arcDegrees * 0.5f) * (MathF.PI / 180f);

			for ( int i = 0; i < points; i++ )
			{
				var t = (float)i / (points - 1);
				var a = forwardYaw - halfArc + t * halfArc * 2f;
				var pos = origin
					+ new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 ) * (range * 0.85f)
					+ Vector3.Up * 50f;

				// Radius kept well under the height offset above so these never light up the floor.
				SpawnLightBurst( pos, color, 14f, 0.08f + i * 0.012f );
			}
		}
		catch { }
	}

	/// <summary>Spark burst where an attack actually lands on an enemy - a main flash plus a
	/// handful of mini sparks scattering outward (using FxPointLight's drift/drag support) for a
	/// proper "impact" read instead of a single static dot.</summary>
	public static void HitSpark( Vector3 position, Color color, float scale = 1f )
	{
		try
		{
			// Height kept comfortably above the flash radius so it reads as a bright hit-spark in
			// the air rather than a glowing patch projected onto the floor.
			var center = position + Vector3.Up * 70f;
			SpawnLightBurst( center, color, 40f * scale, 0.16f );
			SpawnLightBurst( center, Color.White, 20f * scale, 0.09f );

			var scatterCount = 4 + (int)MathF.Round( scale * 1.5f );
			for ( int i = 0; i < scatterCount; i++ )
			{
				var dir = RandomHorizontalDir() + Vector3.Up * (Random.Shared.NextSingle() * 0.6f);
				SpawnScatterSpark( center, color, 14f * scale, 0.2f + Random.Shared.NextSingle() * 0.1f, dir * (160f + Random.Shared.NextSingle() * 100f) );
			}
		}
		catch { }
	}

	/// <summary>Bigger double-flash plus a wider ring of scatter sparks for a finisher connecting.</summary>
	public static void FinisherBurst( Vector3 position )
	{
		try
		{
			var center = position + Vector3.Up * 60f;
			SpawnLightBurst( center, Color.White, 34f, 0.16f );
			SpawnLightBurst( center, new Color( 1f, 0.25f, 0.2f ), 48f, 0.32f );

			const int scatterCount = 8;
			for ( int i = 0; i < scatterCount; i++ )
			{
				var angle = (i / (float)scatterCount) * MathF.PI * 2f;
				var dir = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.25f );
				SpawnScatterSpark( center, new Color( 1f, 0.4f, 0.2f ), 14f, 0.24f, dir * 220f );
			}
		}
		catch { }
	}

	/// <summary>Warm knockout flash, a ring of dust/spark debris kicked outward along the ground,
	/// and a few "dizzy stars" circling above where the enemy fell - on-theme for a bar fight, and
	/// entirely procedural (no particle assets needed).</summary>
	public static void DeathBurst( Vector3 position )
	{
		try
		{
			var headPos = position + Vector3.Up * 75f;

			SpawnLightBurst( headPos, new Color( 1f, 0.85f, 0.35f ), 38f, 0.24f );

			const int debris = 6;
			for ( int i = 0; i < debris; i++ )
			{
				var angle = (i / (float)debris) * MathF.PI * 2f + Random.Shared.NextSingle();
				var dir = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.15f );
				SpawnScatterSpark( position + Vector3.Up * 45f, new Color( 0.6f, 0.55f, 0.5f ), 10f, 0.3f, dir * (90f + Random.Shared.NextSingle() * 60f) );
			}

			const int stars = 4;
			for ( int i = 0; i < stars; i++ )
			{
				var go = new GameObject( true, "FxStar" ) { WorldPosition = headPos };

				var light = go.Components.Create<PointLight>();
				light.LightColor = new Color( 1f, 0.9f, 0.45f );
				light.Radius = 16f;

				var orbit = go.Components.Create<FxOrbitStar>();
				orbit.Center = headPos;
				orbit.Radius = 20f;
				orbit.Speed = 5f + i * 0.6f;
				orbit.PhaseOffset = i * (MathF.PI / 2f);
				orbit.Duration = 1f;
				orbit.RiseSpeed = 20f;
			}
		}
		catch { }
	}

	static Vector3 RandomHorizontalDir()
	{
		var angle = Random.Shared.NextSingle() * MathF.PI * 2f;
		return new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 );
	}

	static void SpawnScatterSpark( Vector3 position, Color color, float radius, float duration, Vector3 velocity )
	{
		var go = new GameObject( true, "FxSpark" ) { WorldPosition = position };

		var light = go.Components.Create<PointLight>();
		light.LightColor = color;
		light.Radius = radius;

		var fx = go.Components.Create<FxPointLight>();
		fx.Duration = duration;
		fx.StartRadius = radius;
		fx.Velocity = velocity;
		fx.Drag = 6f;
	}

	/// <summary>Small burst of dust/embers where a thrown bottle or environmental hit lands.</summary>
	public static void ImpactPuff( Vector3 position, Color color )
	{
		try
		{
			SpawnLightBurst( position + Vector3.Up * 30f, color, 12f, 0.14f );
		}
		catch { }
	}

	/// <summary>Quick, subtle dust flash under a footstep - deliberately tiny and brief (unlike the
	/// combat VFX, this one's SUPPOSED to touch the ground) so it doesn't overwhelm movement.</summary>
	public static void FootstepDust( Vector3 groundPosition )
	{
		try
		{
			SpawnScatterSpark( groundPosition + Vector3.Up * 4f, new Color( 0.75f, 0.7f, 0.6f ), 9f, 0.12f, Vector3.Up * 12f );
		}
		catch { }
	}

	/// <summary>Festive multi-color firework when the player drinks a glass - a little celebration
	/// for the run's core loop moment.</summary>
	public static void DrinkCelebration( Vector3 position )
	{
		try
		{
			var center = position + Vector3.Up * 80f;
			SpawnLightBurst( center, new Color( 1f, 0.9f, 0.5f ), 46f, 0.3f );

			var colors = new[]
			{
				new Color( 1f, 0.85f, 0.2f ),
				new Color( 0.9f, 0.25f, 0.3f ),
				new Color( 0.3f, 0.75f, 1f ),
				new Color( 0.6f, 1f, 0.4f ),
			};

			const int scatterCount = 10;
			for ( int i = 0; i < scatterCount; i++ )
			{
				var angle = (i / (float)scatterCount) * MathF.PI * 2f;
				var dir = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.6f + Random.Shared.NextSingle() * 0.4f );
				var color = colors[i % colors.Length];
				SpawnScatterSpark( center, color, 14f, 0.5f + Random.Shared.NextSingle() * 0.3f, dir * (150f + Random.Shared.NextSingle() * 120f) );
			}
		}
		catch { }
	}

	/// <summary>Small green/gold sparkle when a sobering pickup is collected.</summary>
	public static void PickupSparkle( Vector3 position )
	{
		try
		{
			var center = position + Vector3.Up * 40f;
			SpawnLightBurst( center, new Color( 0.5f, 1f, 0.6f ), 30f, 0.2f );

			const int scatterCount = 6;
			for ( int i = 0; i < scatterCount; i++ )
			{
				var dir = RandomHorizontalDir() + Vector3.Up * (0.5f + Random.Shared.NextSingle() * 0.5f);
				SpawnScatterSpark( center, new Color( 0.6f, 1f, 0.7f ), 10f, 0.3f, dir * 90f );
			}
		}
		catch { }
	}

	/// <summary>Bigger celebratory burst for milestone toasts - reuses the drink firework's look
	/// with a gold/white palette so it reads as "achievement", not "you're drunker now".</summary>
	public static void MilestoneBurst( Vector3 position )
	{
		try
		{
			var center = position + Vector3.Up * 90f;
			SpawnLightBurst( center, Color.White, 40f, 0.28f );
			SpawnLightBurst( center, new Color( 1f, 0.85f, 0.3f ), 60f, 0.4f );

			const int scatterCount = 10;
			for ( int i = 0; i < scatterCount; i++ )
			{
				var angle = (i / (float)scatterCount) * MathF.PI * 2f;
				var dir = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.5f );
				SpawnScatterSpark( center, new Color( 1f, 0.9f, 0.5f ), 12f, 0.5f, dir * 170f );
			}
		}
		catch { }
	}

	/// <summary>A single faint ember - call this on a throttle (not every frame) while something
	/// slides from knockback, to leave a light trail behind it.</summary>
	public static void KnockbackEmber( Vector3 position, Color color )
	{
		try
		{
			SpawnScatterSpark( position + Vector3.Up * 20f, color, 8f, 0.25f, Vector3.Up * 10f );
		}
		catch { }
	}

	static void SpawnLightBurst( Vector3 position, Color color, float radius, float duration )
	{
		var go = new GameObject( true, "FxLight" ) { WorldPosition = position };

		var light = go.Components.Create<PointLight>();
		light.LightColor = color;
		light.Radius = radius;

		var fx = go.Components.Create<FxPointLight>();
		fx.Duration = duration;
		fx.StartRadius = radius;
	}
}
