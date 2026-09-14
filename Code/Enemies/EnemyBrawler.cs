namespace DrunkenBarFight;

/// <summary>Standard melee grunt. Moderate everything - the main source of crowd pressure.</summary>
public class EnemyBrawler : EnemyBase
{
	protected override void SetDefaults()
	{
		MaxHP = 30f;
		// Fastest of the three - lands right at the 5%-slower-than-player cap after OnAwake's
		// jitter+clamp (see EnemyBase.MaxEnemyMoveSpeed).
		MoveSpeed = 235f;
		Damage = 6f;
		AttackRange = 55f;
		AttackCooldown = 1.2f;
		AttackWindup = 0f;
		IsSoberingEnemy = false;
	}

	public override void ApplyVisual()
	{
		if ( BodyRenderer is null )
			return;

		// No Tint override anymore - a real "Human Skin N" clothing item now sets actual skin tone
		// (see EnemyOutfitter), and multiplying a color tint on top of that was muddying/discoloring
		// the real skin texture (that's what was making everyone look flat orange-brown regardless
		// of which tone got picked). Leave the renderer at its natural white tint so the real skin
		// and clothing colors show through undistorted.
		BodyRenderer.Tint = Color.White;

		EnemyOutfitter.Apply( BodyRenderer, "Brawler",
			new[] { "Hawaiian", "T-Shirt", "Tee", "Shirt", "Hoodie", "Jumper", "Sweater", "Polo", "Tank", "Top" },
			new[] { "Jeans", "Trousers", "Pants", "Shorts", "Chinos", "Slacks" },
			new[] { "Trainers", "Shoes", "Sneakers", "Boots", "Sandals" },
			new[] { "Cap", "Beanie", "Hat", "Hair", "Bandana" },
			new[] { "Glasses", "Sunglasses", "Watch", "Necklace", "Chain", "Bracelet" } );
	}
}
