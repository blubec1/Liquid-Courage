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
		MoveSpeed = 125f;
		Damage = 4f;
		AttackRange = 45f;
		AttackCooldown = 1.4f;
		AttackWindup = 0f;
		IsSoberingEnemy = true;
		SoberingAmount = 18f;
	}

	public override void ApplyVisual()
	{
		if ( BodyRenderer is not null )
			BodyRenderer.Tint = new Color( 0.95f, 0.8f, 0.25f, 1f );
	}
}
