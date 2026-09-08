namespace DrunkenBarFight;

/// <summary>Standard melee grunt. Moderate everything - the main source of crowd pressure.</summary>
public class EnemyBrawler : EnemyBase
{
	protected override void SetDefaults()
	{
		MaxHP = 30f;
		MoveSpeed = 150f;
		Damage = 6f;
		AttackRange = 55f;
		AttackCooldown = 1.2f;
		AttackWindup = 0f;
		IsSoberingEnemy = false;
	}

	public override void ApplyVisual()
	{
		if ( BodyRenderer is not null )
			BodyRenderer.Tint = new Color( 0.75f, 0.35f, 0.35f, 1f );
	}
}
