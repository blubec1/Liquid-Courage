namespace DrunkenBarFight;

/// <summary>
/// Slow, tanky, hits hard, and telegraphs its attack briefly so an alert player can dodge,
/// finish it off, or use a finisher on it. Forces the player to prioritize targets.
/// </summary>
public class EnemyBruiser : EnemyBase
{
	protected override void SetDefaults()
	{
		MaxHP = 95f;
		MoveSpeed = 95f;
		Damage = 16f;
		AttackRange = 78f;
		AttackCooldown = 2.2f;
		AttackWindup = 0.55f;
		IsSoberingEnemy = false;
	}

	public override void ApplyVisual()
	{
		if ( BodyRenderer is not null )
		{
			// No Tint override anymore - this was the worst offender for the "everyone's the same
			// muddy black-brown" complaint, since a strong dark multiply on top of a real "Human
			// Skin N" texture crushes it toward black regardless of the actual tone picked. The
			// scaled-up size plus its darker jacket/gear choices already read as "imposing" on their
			// own without needing to discolor the skin underneath.
			BodyRenderer.Tint = Color.White;

			EnemyOutfitter.Apply( BodyRenderer, "Bruiser",
				new[] { "Jacket", "Coat", "Vest", "Hoodie", "Overalls", "Bomber", "Varsity" },
				new[] { "Cargo", "Trousers", "Pants", "Jeans", "Combat" },
				new[] { "Boots", "Shoes", "Sneakers" },
				new[] { "Beanie", "Cap", "Hat", "Bandana", "Balaclava", "Hair" },
				new[] { "Gloves", "Chain", "Watch", "Belt" } );
		}

		WorldScale = Vector3.One * 1.35f;
	}
}
