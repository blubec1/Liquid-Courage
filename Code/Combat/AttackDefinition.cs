using System.Collections.Generic;

namespace DrunkenBarFight;

public enum AttackId
{
	Punch,
	Kick,
	Heavy
}

/// <summary>
/// Data-driven description of one of the player's three basic attacks. Tune everything here -
/// no gameplay logic lives on this class, it's pure data read by PlayerCombat / HitDetector.
/// </summary>
public class AttackDefinition
{
	public AttackId Id;
	public string Name;

	/// <summary>Base damage before drunkenness/style multipliers.</summary>
	public float Damage;

	/// <summary>Seconds before the player can attack again after this one starts.</summary>
	public float Cooldown;

	/// <summary>How long after the cooldown the attack keeps its "recovery" window (used for combo decay pacing).</summary>
	public float Recovery;

	/// <summary>Melee reach in world units.</summary>
	public float Range;

	/// <summary>Total width of the hit arc in degrees, centered on the player's facing direction.</summary>
	public float ArcDegrees;

	/// <summary>How strong the impact feedback is (0-1). Drives freeze-frame length, screenshake and enemy knockback scaling.</summary>
	public float ImpactStrength;

	/// <summary>Knockback speed applied to whatever it hits.</summary>
	public float Knockback;

	/// <summary>How long a hit enemy is frozen/staggered for.</summary>
	public float StaggerTime;

	/// <summary>
	/// Name of a Citizen-compatible animation sequence to play for this attack (e.g. a retargeted
	/// Mixamo clip), or null/empty to use the built-in procedural lunge instead. See
	/// PlayerAnimationDriver.TryPlaySequence - this is best-effort since one-shot sequence
	/// playback on top of the Citizen anim graph is a known rough edge in s&box.
	/// </summary>
	public string Animation;

	/// <summary>How long the animation should hold before returning to normal locomotion. 0 = derive from Recovery.</summary>
	public float AnimationDuration;
}

public static class AttackLibrary
{
	public static readonly IReadOnlyDictionary<AttackId, AttackDefinition> All = new Dictionary<AttackId, AttackDefinition>
	{
		[AttackId.Punch] = new()
		{
			Id = AttackId.Punch,
			Name = "Punch",
			Damage = 7,
			Cooldown = 0.26f,
			Recovery = 0.12f,
			Range = 65,
			ArcDegrees = 100,
			ImpactStrength = 0.25f,
			Knockback = 90,
			StaggerTime = 0.18f,
		},
		[AttackId.Kick] = new()
		{
			Id = AttackId.Kick,
			Name = "Kick",
			Damage = 13,
			Cooldown = 0.44f,
			Recovery = 0.18f,
			Range = 80,
			ArcDegrees = 90,
			ImpactStrength = 0.5f,
			Knockback = 220,
			StaggerTime = 0.32f,
		},
		[AttackId.Heavy] = new()
		{
			Id = AttackId.Heavy,
			Name = "Heavy",
			Damage = 28,
			Cooldown = 0.9f,
			Recovery = 0.35f,
			Range = 95,
			ArcDegrees = 80,
			ImpactStrength = 1f,
			Knockback = 380,
			StaggerTime = 0.55f,
		},
	};

	public static AttackDefinition Get( AttackId id ) => All[id];
}
