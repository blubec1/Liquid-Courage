namespace DrunkenBarFight;

/// <summary>
/// Not very dangerous alone, but killing one is the single best thing the player can do for
/// their sobriety - creates real target-priority decisions in a crowd.
/// </summary>
public class EnemySoberingBartender : EnemyBase
{
	protected override void SetDefaults()
	{
		MaxHP = 20f;
		// Middle of the pack - brisk enough to force a real decision about prioritizing it.
		MoveSpeed = 205f;
		Damage = 4f;
		AttackRange = 45f;
		AttackCooldown = 1.4f;
		AttackWindup = 0f;
		IsSoberingEnemy = true;
		SoberingAmount = 18f;
	}

	public override void ApplyVisual()
	{
		if ( BodyRenderer is null )
			return;

		// No Tint override anymore - the apron itself (applied below) is what should make this
		// archetype instantly readable, not a color multiply that was also muddying real skin tone.
		BodyRenderer.Tint = Color.White;

		EnemyOutfitter.Apply( BodyRenderer, "SoberingBartender",
			new[] { "Apron" },
			new[] { "Dress Shirt", "Shirt", "Waistcoat", "Vest", "Blouse" },
			new[] { "Trousers", "Pants", "Slacks", "Skirt" },
			new[] { "Shoes", "Boots", "Heels" },
			new[] { "Bow Tie", "Tie", "Bandana", "Hair", "Cap" } );
	}
}
