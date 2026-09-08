namespace DrunkenBarFight;

/// <summary>
/// A simple pickup that reduces drunkenness when the player walks over it. Uses a plain
/// distance check each frame rather than physics triggers - cheap, simple, and easy to reason
/// about for the small number of pickups alive at once.
/// </summary>
public class SoberingPickup : Component
{
	[Property] public float PickupRadius { get; set; } = 45f;
	[Property] public float DrunkennessReduction { get; set; } = 25f;

	protected override void OnUpdate()
	{
		var player = PlayerStats.Local;
		if ( player is null || player.IsDead )
			return;

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		var diff = player.WorldPosition - WorldPosition;
		var dist = new Vector3( diff.x, diff.y, 0 ).Length;

		if ( dist <= PickupRadius )
		{
			DrunkennessSystem.Local?.Reduce( DrunkennessReduction );
			GameEvents.RaiseShakeRequested( 1.5f, 0.06f );
			GameObject.Destroy();
		}
	}
}
