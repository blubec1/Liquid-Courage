namespace DrunkenBarFight;

/// <summary>A thrown bottle projectile. Flies straight, damages/knocks back the first enemy it reaches, then breaks.</summary>
public class ThrownBottle : Component
{
	public Vector3 Direction { get; set; } = Vector3.Forward;
	public float Speed { get; set; } = 900f;
	public float Damage { get; set; } = 24f;
	public float Knockback { get; set; } = 260f;
	public float MaxRange { get; set; } = 650f;

	const float HitRadius = 40f;
	const float MaxLifetime = 3f;

	Vector3 _startPos;
	float _spawnTime;

	protected override void OnStart()
	{
		_startPos = WorldPosition;
		_spawnTime = Time.Now;
	}

	protected override void OnUpdate()
	{
		WorldPosition += Direction * Speed * Time.Delta;

		var traveled = new Vector3( WorldPosition.x - _startPos.x, WorldPosition.y - _startPos.y, 0 ).Length;
		if ( traveled >= MaxRange || Time.Now - _spawnTime >= MaxLifetime )
		{
			GameObject.Destroy();
			return;
		}

		foreach ( var enemy in EnemyBase.All )
		{
			if ( enemy is null || !enemy.IsValid || enemy.IsDead )
				continue;

			var diff = enemy.WorldPosition - WorldPosition;
			if ( new Vector3( diff.x, diff.y, 0 ).Length <= HitRadius )
			{
				enemy.ApplyHit( Damage, Direction * Knockback, 0.4f, fromEnvironmental: true );
				HitFeedback.PlayAttackImpact( 0.6f, 1 );
				GameObject.Destroy();
				return;
			}
		}
	}
}
