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

	/// <summary>Ring of light points along a full circle around the player - one per Sweep Breaker
	/// spin pulse, so each pulse reads as a shockwave coming off the spin. Centered on the player's
	/// current position so a walking spin keeps dragging the ring along.</summary>
	public static void AoePulseRing( Vector3 origin, float radius, Color color )
	{
		try
		{
			const int points = 10;
			for ( int i = 0; i < points; i++ )
			{
				var angle = (i / (float)points) * MathF.PI * 2f;
				var pos = origin + new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * radius + Vector3.Up * 40f;
				SpawnLightBurst( pos, color, 16f, 0.2f );
			}
		}
		catch { }
	}

	/// <summary>Creates a short-lived FxShockwave front that sweeps forward from the player - the
	/// Opening Slam line-shockwave's visuals. The component drives itself from the same speed and
	/// duration the combat code uses, so the visible front matches the damage.</summary>
	public static void ShockwaveFront( Vector3 origin, Vector3 facing, float speed, float duration, float arcDegrees )
	{
		try
		{
			var dir = new Vector3( facing.x, facing.y, 0 );
			if ( dir.Length < 0.001f )
				dir = Vector3.Forward;
			else
				dir /= dir.Length;

			var go = new GameObject( true, "FxShockwave" ) { WorldPosition = origin };
			var wave = go.Components.Create<FxShockwave>();
			wave.StartPosition = origin;
			wave.Direction = dir;
			wave.Speed = speed;
			wave.Duration = duration;
			wave.ArcDegrees = arcDegrees;
		}
		catch { }
	}

	/// <summary>The instant the Opening Slam's shockwave launches: a ground ring at the player's
	/// feet, a bright swipe across the slam's front arc, and a burst at the point of impact. Fired at
	/// the animation's slam beat (see FinisherDefinition.WaveLead) so the heavy read lands together
	/// with the damage - the traveling FxShockwave front then carries that energy forward.</summary>
	public static void SlamImpact( Vector3 origin, Vector3 facing, float range )
	{
		try
		{
			var dir = new Vector3( facing.x, facing.y, 0 );
			if ( dir.Length < 0.001f )
				dir = Vector3.Forward;
			else
				dir /= dir.Length;

			AoePulseRing( origin, range * 0.45f, new Color( 1f, 0.75f, 0.3f ) );
			AttackArc( origin, dir, 80f, range * 0.6f, new Color( 1f, 0.8f, 0.35f ) );
			FinisherBurst( origin + dir * (range * 0.35f) );
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
	/// for the run's core loop moment. Bumped bigger/richer on request: a brighter core flash, a
	/// wider main ring (more sparks, more spread, longer-lived), plus a second delayed-look outer
	/// ring (fired immediately but with a lower initial speed and a longer life, so it visually
	/// trails the first ring outward instead of a single flat pop) for a proper two-stage "fireworks"
	/// read instead of a single burst.</summary>
	public static void DrinkCelebration( Vector3 position )
	{
		try
		{
			var center = position + Vector3.Up * 80f;
			SpawnLightBurst( center, new Color( 1f, 0.92f, 0.55f ), 62f, 0.4f );

			var colors = new[]
			{
				new Color( 1f, 0.85f, 0.2f ),
				new Color( 0.9f, 0.25f, 0.3f ),
				new Color( 0.3f, 0.75f, 1f ),
				new Color( 0.6f, 1f, 0.4f ),
				new Color( 1f, 0.55f, 0.85f ),
			};

			// Inner ring - fast, tight, bright. Bumped from 10 to 16 sparks.
			const int innerCount = 16;
			for ( int i = 0; i < innerCount; i++ )
			{
				var angle = (i / (float)innerCount) * MathF.PI * 2f;
				var dir = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.6f + Random.Shared.NextSingle() * 0.4f );
				var color = colors[i % colors.Length];
				SpawnScatterSpark( center, color, 15f, 0.55f + Random.Shared.NextSingle() * 0.3f, dir * (160f + Random.Shared.NextSingle() * 130f) );
			}

			// Outer ring - slower start but travels further and lingers longer, so it reads as a
			// second wave trailing the inner pop outward rather than everything landing at once.
			const int outerCount = 10;
			for ( int i = 0; i < outerCount; i++ )
			{
				var angle = ((i + 0.5f) / outerCount) * MathF.PI * 2f;
				var dir = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.3f + Random.Shared.NextSingle() * 0.5f );
				var color = colors[(i + 2) % colors.Length];
				SpawnScatterSpark( center, color, 10f, 0.8f + Random.Shared.NextSingle() * 0.35f, dir * (240f + Random.Shared.NextSingle() * 100f) );
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

	/// <summary>Sharp red/dark burst when the player takes damage - deliberately a different
	/// palette (deep red, no white core) from the yellow/orange hit-sparks the player deals out,
	/// so it reads unambiguously as "you got hurt" rather than "you landed a hit". Severity01
	/// (0-1, roughly damage-taken/maxHP) scales how big and how scattered the burst is.</summary>
	public static void PlayerHit( Vector3 position, float severity01 )
	{
		try
		{
			var s = MathF.Max( 0.35f, severity01 );
			var center = position + Vector3.Up * 55f;

			SpawnLightBurst( center, new Color( 0.9f, 0.1f, 0.12f ), 30f * s, 0.18f );
			SpawnLightBurst( center, new Color( 0.5f, 0.02f, 0.04f ), 46f * s, 0.3f );

			var scatterCount = 3 + (int)MathF.Round( s * 4f );
			for ( int i = 0; i < scatterCount; i++ )
			{
				var dir = RandomHorizontalDir() + Vector3.Up * (0.3f + Random.Shared.NextSingle() * 0.5f);
				SpawnScatterSpark( center, new Color( 0.8f, 0.08f, 0.1f ), 10f * s, 0.25f + Random.Shared.NextSingle() * 0.15f, dir * (110f + Random.Shared.NextSingle() * 90f) );
			}
		}
		catch { }
	}

	// Loaded once and reused for every droplet - Model.Load re-resolving the same resource path on
	// every single hit (up to a dozen+ calls per hit, times every enemy hit in the same frame
	// during a wide swing or finisher) was the actual cause of the hitch reported whenever blood
	// spawned; a Model is an immutable, safely-shared resource reference so caching it is free.
	static Model _bloodModel;
	static Model BloodModel => _bloodModel ??= Model.Load( "models/dev/box.vmdl" );

	/// <summary>Small burst of dark-red debris chunks flying outward under gravity and splatting on
	/// the ground - unlike every other effect here this uses FxDebris (solid tinted geometry)
	/// rather than FxPointLight (a glow), since a light burst alone doesn't read as "blood", it
	/// reads as "flash". Severity01 scales both count and speed, same convention as
	/// PlayerHit/HitSpark. Pass the raw hit-ish world position (NOT already offset upward) - this
	/// owns its own chest-height offset internally.</summary>
	public static void BloodSpatter( Vector3 position, float severity01 = 1f )
	{
		try
		{
			var s = MathF.Max( 0.4f, severity01 );
			var center = position + Vector3.Up * 55f;
			// Droplets fall back down to roughly where they were struck, not the ground plane at
			// z=0 - keeps blood readable even on raised geometry (bar top, pool table, etc.).
			var groundZ = position.z;

			// Capped rather than left to grow unbounded with severity - keeps worst-case per-hit
			// GameObject/component creation cost (the other big contributor to the hitch) in check
			// even on a 1.5 severity finisher hit.
			var dropletCount = System.Math.Min( 14, 6 + (int)MathF.Round( s * 6f ) );
			for ( int i = 0; i < dropletCount; i++ )
			{
				var dir = RandomHorizontalDir() + Vector3.Up * (0.35f + Random.Shared.NextSingle() * 0.6f);
				var speed = (110f + Random.Shared.NextSingle() * 170f) * s;
				SpawnBloodDroplet( center, dir * speed, groundZ );
			}
		}
		catch { }
	}

	static void SpawnBloodDroplet( Vector3 position, Vector3 velocity, float groundZ )
	{
		var go = new GameObject( true, "FxBlood" ) { WorldPosition = position };

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = BloodModel;
		// Wider dark-to-bright red range (near-black clots to bright arterial red) reads more like
		// real blood than the previous narrow, uniformly-muddy tint band.
		var dark = Random.Shared.NextSingle();
		renderer.Tint = new Color( 0.22f + dark * 0.35f, 0.01f + dark * 0.02f, 0.02f, 1f );

		// Flattened and stretched along the velocity direction instead of a uniform cube - reads as
		// a streaking droplet rather than a tumbling little box.
		var speedT = System.Math.Clamp( velocity.Length / 260f, 0f, 1f );
		var baseScale = 0.075f + Random.Shared.NextSingle() * 0.05f;
		go.WorldScale = new Vector3( baseScale * (1f + speedT * 0.8f), baseScale * 0.7f, baseScale * 0.55f );

		if ( velocity.Length > 1f )
		{
			try { go.WorldRotation = Rotation.LookAt( velocity.Normal, Vector3.Up ); }
			catch { }
		}

		var fx = go.Components.Create<FxDebris>();
		fx.Duration = 0.7f + Random.Shared.NextSingle() * 0.35f;
		fx.Velocity = velocity;
		fx.Gravity = 750f;
		fx.Drag = 1.1f;
		fx.SplatOnGround = true;
		fx.GroundZ = groundZ;
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
