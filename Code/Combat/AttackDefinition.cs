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

	/// <summary>How strong the impact feedback is (0-1). Drives screenshake and enemy knockback scaling.</summary>
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

	/// <summary>When true, this attack uses full-body sequence swap instead of bone-level blending.</summary>
	public bool DisableBlending;

	/// <summary>When true, player movement and facing are frozen for the duration of this attack.</summary>
	public bool DisableMovement;

	/// <summary>
	/// 0 = off. When set, this attack also pushes every living enemy within this radius of the
	/// player - regardless of facing/arc, and regardless of whether they were also hit by the normal
	/// Range/ArcDegrees check above - directly away from the player (not the attacker's facing
	/// direction). Built for Kick: a plain arc-based knockback only ever pushed the front row, and
	/// the untouched enemies behind them acted as a wall the front row's knockback slid straight
	/// into, so nothing visibly moved. Pushing the whole crowd within this radius at once means there
	/// is no stationary wall left to block anyone.
	/// </summary>
	public float RadialPushRadius;

	/// <summary>Empty/null = pick a sound off ImpactStrength's thresholds as normal (see
	/// HitFeedback.PlayImpactSound). Set this to force a specific impact cue instead - used by Kick,
	/// which wants the light-punch sound even though its ImpactStrength (tuned for shake feel, not
	/// sound selection) would otherwise land it in the "medium" bucket.</summary>
	public string ImpactSoundOverride;

	/// <summary>Whoosh cue played the instant this attack is thrown - even on a whiff - as opposed to
	/// ImpactSoundOverride which only fires when something is actually hit. See HitFeedback.PlaySwing.</summary>
	public string SwingSound;
}

public static class AttackLibrary
{
	public static readonly Dictionary<AttackId, AttackDefinition> All = new Dictionary<AttackId, AttackDefinition>
	{
		[AttackId.Punch] = new()
		{
			Id = AttackId.Punch,
			Animation = "Punching_1",
			AnimationDuration = 0.3f,
			Name = "Punch",
			Damage = 7,
			Cooldown = 0.26f,
			Recovery = 0.12f,
			Range = 65,
			ArcDegrees = 100,
			ImpactStrength = 0.25f,
			Knockback = 60,
			StaggerTime = 0.18f,
			SwingSound = "sounds/combat/swing_light.sound",
		},
		// A "get off me" panic button with real damage behind it - the wide arc and huge knockback
		// clear space, but the kick also pays out, so it's never a pure zero-damage push. The
		// guarantee that kicked enemies can't immediately hit back is still StaggerTime, not
		// distance: EnemyBase freezes an enemy's whole AI (including attack resolution) for
		// StaggerTime regardless of where the knockback leaves it. Damage also feeds the ragdoll
		// kill toss (see EnemyBase.RagdollDamageImpulseScale), so a killing kick visibly launches
		// the body instead of just dropping it in place.
		[AttackId.Kick] = new()
		{
			Id = AttackId.Kick,
			DisableBlending = true,
			DisableMovement = true,
			Animation = "Roundhouse_Kick_2",
			AnimationDuration = 0.6f,
			Name = "Kick",
			Damage = 15,
			Cooldown = 0.44f,
			Recovery = 0.18f,
			Range = 80,
			ArcDegrees = 150,
			ImpactStrength = 0.4f,
			Knockback = 600,
			StaggerTime = 0.65f,
			// Radial push now matches the hit Range exactly (both 80) - the knockback reaches no
			// further than the arc that damages, so the shove is honest to what you actually hit.
			RadialPushRadius = 80,
			// User-requested: kick uses the same impact sound as the light punch.
			ImpactSoundOverride = "sounds/combat/light_impact.sound",
			SwingSound = "sounds/combat/swing_kick.sound",
		},
		[AttackId.Heavy] = new()
		{
			Id = AttackId.Heavy,
			Animation = "Hook_Punch_2",
			AnimationDuration = 0.6f,
			Name = "Heavy",
			Damage = 27,
			Cooldown = 0.9f,
			Recovery = 0.35f,
			Range = 95,
			ArcDegrees = 80,
			ImpactStrength = 1f,
			Knockback = 380,
			StaggerTime = 0.55f,
			SwingSound = "sounds/combat/swing_heavy.sound",
		},
	};

	public static AttackDefinition Get( AttackId id ) => All[id];
}
