using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Marks a game object as a player-only blocker. The player's CharacterController cannot
/// pass through objects with this tag, but enemies (which use NavMeshAgent or direct position
/// setting) move through freely. Attach to any GameObject with a collider that should stop
/// the player but not enemies.
/// </summary>
public class PlayerBlocker : Component
{
	public const string Tag = "player_blocker";

	protected override void OnAwake()
	{
		GameObject.Tags.Add( Tag );
	}

	/// <summary>Returns all GameObjects tagged as player blockers in the current scene.</summary>
	public static IEnumerable<GameObject> GetAll()
	{
		return Scene.GetAll<GameObject>().Where( go => go.Tags.Has( Tag ) );
	}

	/// <summary>Checks if a position is blocked by any player blocker within the given radius.</summary>
	public static bool IsBlocked( Vector3 position, float radius = 32f )
	{
		foreach ( var go in GetAll() )
		{
			var dist = (position - go.WorldPosition).Length;
			if ( dist <= radius )
				return true;
		}
		return false;
	}
}
