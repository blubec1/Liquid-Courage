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
			BodyRenderer.Tint = new Color( 0.4f, 0.18f, 0.18f, 1f );

		WorldScale = Vector3.One * 1.35f;
	}
}
