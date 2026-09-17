namespace DrunkenBarFight;

/// <summary>
/// Defines a rectangular zone that blocks player movement. Place in the scene to create
/// barriers the player cannot cross (bar counters, VIP areas, blocked doorways, etc).
/// Enemies pass through freely since their movement bypasses physics collision.
/// The BoxCollider is automatically created if not already present on the GameObject.
/// </summary>
public class PlayerBlockerZone : Component
{
	[Property, Group( "Size" )] public Vector3 BoxSize { get; set; } = new( 64f, 64f, 64f );

	protected override void OnAwake()
	{
		GameObject.Tags.Add( PlayerBlocker.Tag );

		var collider = Components.Get<BoxCollider>();
		if ( collider is null )
			collider = Components.Create<BoxCollider>();

		collider.Scale = BoxSize;
		collider.IsTrigger = false;
	}
}
