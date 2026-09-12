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
/// Data-driven description of a predetermined finisher, triggered by a specific attack string.
/// Finishers hit everything in a generous radius+arc rather than a narrow line, so they can
/// plausibly hit multiple enemies, and they pay out extra score directly.
/// </summary>
public class FinisherDefinition
{
	public FinisherId Id;
	public string Name;
	public AttackId[] Sequence;

	public float Damage;
	public float Range;
	public float ArcDegrees;

	/// <summary>Flat score awarded on top of the normal scoring formula.</summary>
	public float ScoreValue;

	public float ImpactStrength;
	public float Knockback;
	public float StaggerTime;

	public string Description;

	/// <summary>Name of a Citizen-compatible animation sequence for this finisher, or null/empty for the procedural version.</summary>
	public string Animation;
	public float AnimationDuration;

	/// <summary>When true, this finisher uses full-body sequence swap instead of bone-level blending.</summary>
	public bool DisableBlending;

	/// <summary>When true, player movement and facing are frozen for the duration of this finisher.</summary>
	public bool DisableMovement;
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
			Damage = 45,
			Range = 110,
			ArcDegrees = 140,
			ScoreValue = 250,
			ImpactStrength = 1f,
			Knockback = 420,
			StaggerTime = 0.7f,
			Description = "Punch, Punch, Heavy",
		},
		new()
		{
			Id = FinisherId.SweepBreaker,
			Animation = "Hurricane_Kick_2",
			DisableBlending = true,
			Name = "Sweep Breaker",
			Sequence = new[] { AttackId.Kick, AttackId.Punch, AttackId.Heavy },
			Damage = 50,
			Range = 120,
			ArcDegrees = 220,
			ScoreValue = 300,
			ImpactStrength = 1f,
			Knockback = 460,
			StaggerTime = 0.75f,
			Description = "Kick, Punch, Heavy",
		},
		new()
		{
			Id = FinisherId.FlurryJab,
			Animation = "Punching_Bag_2",
			Name = "Flurry Jab",
			Sequence = new[] { AttackId.Punch, AttackId.Kick, AttackId.Punch },
			Damage = 32,
			Range = 100,
			ArcDegrees = 120,
			ScoreValue = 180,
			ImpactStrength = 0.8f,
			Knockback = 300,
			StaggerTime = 0.5f,
			Description = "Punch, Kick, Punch",
		},
		new()
		{
			Id = FinisherId.OpeningSlam,
			Name = "Opening Slam",
			Sequence = new[] { AttackId.Heavy, AttackId.Punch, AttackId.Kick },
			Damage = 40,
			Range = 130,
			ArcDegrees = 360,
			ScoreValue = 280,
			ImpactStrength = 1f,
			Knockback = 400,
			StaggerTime = 0.7f,
			Description = "Heavy, Punch, Kick",
		},
	};
}
