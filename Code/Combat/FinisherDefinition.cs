using System.Collections.Generic;

namespace DrunkenBarFight;

public enum FinisherId
{
	HaymakerCombo,     // Punch, Punch, Heavy
	SweepBreaker,      // Kick, Punch, Heavy
	FlurryJab,         // Punch, Kick, Punch
	OpeningSlam,       // Heavy, Punch, Kick
}

/// <summary>
/// The mechanical shape a finisher plays out in - what "executing this finisher" actually does
/// to the enemies around you. Each behavior owns its own timing/geometry fields below.
/// </summary>
public enum FinisherBehavior
{
	/// <summary>Single immediate burst arc - the original one-strike finisher. Used as the default
	/// fallback so any future finisher that doesn't set a behavior still works.</summary>
	InstantBurst,

	/// <summary>A sequence of damage pulses in a full circle around the player, timed to the
	/// spin animation. Hits everything in Range repeatedly while the animation plays.</summary>
	AoeSpinPulses,

	/// <summary>A burst of damage that sweeps forward as a wavefront, hitting each enemy once as
	/// the front passes it. Split-second, long reach, very directional.</summary>
	LineShockwave,

	/// <summary>A rapid row of small hits in front of the player. Ticks keep the victim staggered
	/// in place so the whole row lands; the last strike can be dialed up into a heavy closer
	/// (FinalHit* fields) for the haymaker.</summary>
	RapidCombo,
}

/// <summary>
/// Data-driven description of a predetermined finisher, triggered by a specific attack string.
/// Each finisher declares a Behavior plus the tuning for that behavior - pure data, no logic.
/// The trigger sequences are what make finishers a *choice*: different inputs, different results.
/// </summary>
public class FinisherDefinition
{
	public FinisherId Id;
	public string Name;
	public AttackId[] Sequence;

	/// <summary>How this finisher plays out mechanically - see the behavior's fields below.</summary>
	public FinisherBehavior Behavior = FinisherBehavior.InstantBurst;

	// --- Shared / InstantBurst ---
	public float Damage;
	public float Range;
	public float ArcDegrees;

	/// <summary>Flat score awarded on top of the normal scoring formula.</summary>
	public float ScoreValue;

	public float ImpactStrength;
	public float Knockback;
	public float StaggerTime;

	/// <summary>Input sequence that triggers this finisher (shown in the HUD finisher reference).</summary>
	public string Description;

	/// <summary>One-line description of what the behavior actually does, so the HUD reads as a
	/// choice between different moves rather than four identical terms.</summary>
	public string Blurb;

	// --- AoeSpinPulses ---
	/// <summary>Seconds before the first pulse lands (lets the spin wind up).</summary>
	public float PulseLead = 0.25f;
	/// <summary>Seconds between pulses - the "per half second" cadence.</summary>
	public float PulseInterval = 0.5f;
	public int PulseCount = 3;

	// --- LineShockwave ---
	/// <summary>How fast the wavefront travels forward, in world units per second.</summary>
	public float WaveSpeed = 1000f;
	/// <summary>Total distance the wavefront travels before it dissipates.</summary>
	public float WaveDepth = 300f;

	// --- RapidCombo ---
	/// <summary>Seconds between strikes (including the final closer).</summary>
	public float StrikeInterval = 0.12f;
	/// <summary>Total strikes including the final closer. Keep TickStagger >= StrikeInterval * 1.5
	/// so the victim's stagger never lapses mid-row and lets them slip out a windup.</summary>
	public int StrikeCount = 1;
	/// <summary>Width of the arc each strike reaches, centered on facing.</summary>
	public float StrikeArcDegrees = 70f;
	/// <summary>Stagger applied by a non-final tick (also used as the between-pulse stagger for
	/// AoeSpinPulses). Long enough to keep the victim locked in place for the whole row.</summary>
	public float TickStagger = 0.2f;
	/// <summary>Damage multiplier on the final closer (Haymaker's heavy hit). 1 = no multiplier.</summary>
	public float FinalHitDamageMultiplier = 1f;
	/// <summary>Knockback on the final closer - the hit that actually launches the victim.</summary>
	public float FinalHitKnockback = 0f;
	/// <summary>Stagger on the final closer. 0 = falls back to StaggerTime.</summary>
	public float FinalHitStagger = 0f;

	/// <summary>Name of a Citizen-compatible animation sequence for this finisher, or null/empty for the procedural version.</summary>
	public string Animation;
	public float AnimationDuration;

	/// <summary>When true, this finisher uses full-body sequence swap instead of bone-level blending.</summary>
	public bool DisableBlending;

	/// <summary>When true, player movement and facing are frozen for the duration of this finisher.</summary>
	public bool DisableMovement;

	/// <summary>
	/// How long the finisher lasts - the single source of truth used for the animation swing
	/// duration, the tick schedule, the input lock, and the movement freeze. Because both
	/// PlayerCombat and PlayerAnimationDriver read this same value, the damage beats can never
	/// drift apart from the clip. Mirrors the old per-file fallback so InstantBurst finishers
	/// behave exactly as before.
	/// </summary>
	public float Duration
	{
		get
		{
			if ( AnimationDuration > 0f )
				return AnimationDuration;
			return System.MathF.Max( 0.2f, StaggerTime * 0.6f + 0.2f );
		}
	}
}

public static class FinisherLibrary
{
	public static readonly List<FinisherDefinition> All = new List<FinisherDefinition>
	{
		new()
		{
			Id = FinisherId.HaymakerCombo,
			Animation = "Haymaker",
			AnimationDuration = 1.3f,
			Name = "Haymaker Combo",
			Sequence = new[] { AttackId.Punch, AttackId.Punch, AttackId.Heavy },
			Behavior = FinisherBehavior.RapidCombo,
			Damage = 7,
			Range = 95,
			ArcDegrees = 60,
			StrikeInterval = 0.12f,
			StrikeCount = 6,
			StrikeArcDegrees = 60,
			TickStagger = 0.2f,
			FinalHitDamageMultiplier = 3.5f,
			FinalHitKnockback = 650,
			FinalHitStagger = 1f,
			ScoreValue = 250,
			ImpactStrength = 1f,
			Knockback = 380,
			StaggerTime = 0.7f,
			Description = "Punch, Punch, Heavy",
			Blurb = "jab row + heavy haymaker",
		},
		new()
		{
			Id = FinisherId.SweepBreaker,
			Animation = "Hurricane_Kick_2",
			DisableBlending = true,
			AnimationDuration = 1.4f,
			Name = "Sweep Breaker",
			Sequence = new[] { AttackId.Kick, AttackId.Punch, AttackId.Heavy },
			Behavior = FinisherBehavior.AoeSpinPulses,
			Damage = 16,
			Range = 150,
			ArcDegrees = 360,
			PulseLead = 0.25f,
			PulseInterval = 0.5f,
			PulseCount = 3,
			TickStagger = 0.45f,
			ScoreValue = 300,
			ImpactStrength = 1f,
			Knockback = 30,
			StaggerTime = 0.75f,
			Description = "Kick, Punch, Heavy",
			Blurb = "360 degree spin aoe",
		},
		new()
		{
			Id = FinisherId.FlurryJab,
			Animation = "Punching_Bag_2",
			AnimationDuration = 0.9f,
			Name = "Flurry Jab",
			Sequence = new[] { AttackId.Punch, AttackId.Kick, AttackId.Punch },
			Behavior = FinisherBehavior.RapidCombo,
			Damage = 6,
			Range = 90,
			ArcDegrees = 60,
			StrikeInterval = 0.12f,
			StrikeCount = 6,
			StrikeArcDegrees = 60,
			TickStagger = 0.2f,
			FinalHitDamageMultiplier = 1f,
			FinalHitKnockback = 220,
			FinalHitStagger = 0.55f,
			ScoreValue = 180,
			ImpactStrength = 0.8f,
			Knockback = 300,
			StaggerTime = 0.5f,
			Description = "Punch, Kick, Punch",
			Blurb = "rapid jab row",
		},
		new()
		{
			Id = FinisherId.OpeningSlam,
			Animation = "Grab_And_Slam_2",
			AnimationDuration = 1.5f,
			DisableMovement = true,
			DisableBlending = true,
			Name = "Opening Slam",
			Sequence = new[] { AttackId.Heavy, AttackId.Punch, AttackId.Kick },
			Behavior = FinisherBehavior.LineShockwave,
			Damage = 85,
			Range = 320,
			ArcDegrees = 26,
			WaveSpeed = 1000f,
			WaveDepth = 320f,
			ScoreValue = 280,
			ImpactStrength = 1.2f,
			Knockback = 700,
			StaggerTime = 1f,
			Description = "Heavy, Punch, Kick",
			Blurb = "line shockwave",
		},
	};
}