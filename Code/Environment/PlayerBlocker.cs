using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Marks a game object as a player-only blocker. The player's CharacterController cannot
/// pass through objects with this tag, but enemies (which use NavMeshAgent or direct position
/// setting) move through freely. Attach to any GameObject with a collider that should stop
/// the player but not enemies.
///
/// Self-registers into a static list (same pattern as EnemyBase.All) rather than querying the
/// scene statically - Scene is an instance property inherited from Component, so it isn't reachable
/// from a static method without an instance, which is what caused CS0120 here originally.
/// </summary>
public class PlayerBlocker : Component
{
	public const string Tag = "player_blocker";

	static readonly List<PlayerBlocker> All = new();

	protected override void OnAwake()
	{
		GameObject.Tags.Add( Tag );
		All.Add( this );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
	}

	/// <summary>Returns all GameObjects tagged as player blockers in the current scene.</summary>
	public static IEnumerable<GameObject> GetAll()
	{
		return All.Select( b => b.GameObject );
	}

	/// <summary>Checks if a position is blocked by any player blocker within the given radius.</summary>
	public static bool IsBlocked( Vector3 position, float radius = 32f )
	{
		foreach ( var blocker in All )
		{
			var dist = (position - blocker.WorldPosition).Length;
			if ( dist <= radius )
				return true;
		}
		return false;
	}
}
