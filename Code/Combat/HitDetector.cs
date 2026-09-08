using System;
using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Pure math melee hit detection: given an origin, a facing direction, a range and an arc width,
/// finds every registered enemy that falls inside that cone (or full circle, if arcDegrees >= 360).
/// Deliberately avoids physics queries - this is a flat, compact arena, and a simple distance+angle
/// check is fast, predictable and very easy to tune for readability.
/// </summary>
public static class HitDetector
{
	public static List<EnemyBase> FindEnemies( Vector3 origin, Vector3 facing, float range, float arcDegrees )
	{
		var results = new List<(EnemyBase enemy, float dist)>();

		var flatFacing = new Vector3( facing.x, facing.y, 0 );
		var facingLen = flatFacing.Length;
		if ( facingLen < 0.001f )
			flatFacing = Vector3.Forward;
		else
			flatFacing /= facingLen;

		var halfArcRad = (arcDegrees * 0.5f) * (MathF.PI / 180f);
		var useFullCircle = arcDegrees >= 359.9f;

		foreach ( var enemy in EnemyBase.All )
		{
			if ( enemy is null || !enemy.IsValid || enemy.IsDead )
				continue;

			var toEnemy = enemy.WorldPosition - origin;
			var flat = new Vector3( toEnemy.x, toEnemy.y, 0 );
			var dist = flat.Length;

			if ( dist > range )
				continue;

			if ( !useFullCircle )
			{
				var dir = dist < 0.001f ? flatFacing : flat / dist;
				var dot = Math.Clamp( flatFacing.x * dir.x + flatFacing.y * dir.y, -1f, 1f );
				var angle = MathF.Acos( dot );
				if ( angle > halfArcRad )
					continue;
			}

			results.Add( (enemy, dist) );
		}

		return results.OrderBy( r => r.dist ).Select( r => r.enemy ).ToList();
	}
}
