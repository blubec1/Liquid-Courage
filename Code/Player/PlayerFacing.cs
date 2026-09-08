namespace DrunkenBarFight;

/// <summary>
/// Rotates the player to face wherever the mouse is pointing on the arena floor. This is what
/// "aims" the melee arc/crosshair independently of movement direction.
/// </summary>
public class PlayerFacing : Component
{
	[Property, Group( "Tuning" )] public float TurnSpeed { get; set; } = 18f;

	public Vector3 AimWorldPoint { get; private set; }
	public Vector3 FacingDirection { get; private set; } = Vector3.Forward;

	protected override void OnUpdate()
	{
		var camera = IsoCameraRig.Instance?.Camera;
		if ( camera is null )
			return;

		var ray = camera.ScreenPixelToRay( Mouse.Position );

		var planeZ = WorldPosition.z;
		if ( System.MathF.Abs( ray.Forward.z ) > 0.0001f )
		{
			var t = (planeZ - ray.Position.z) / ray.Forward.z;
			if ( t > 0 )
			{
				AimWorldPoint = ray.Position + ray.Forward * t;

				var dir = AimWorldPoint - WorldPosition;
				dir = new Vector3( dir.x, dir.y, 0 );
				var len = dir.Length;
				if ( len > 4f )
				{
					dir /= len;
					FacingDirection = dir;
				}
			}
		}

		var targetYaw = System.MathF.Atan2( FacingDirection.y, FacingDirection.x ) * (180f / System.MathF.PI);
		var targetRotation = Rotation.FromYaw( targetYaw );
		WorldRotation = Rotation.Slerp( WorldRotation, targetRotation, System.Math.Clamp( Time.Delta * TurnSpeed, 0f, 1f ) );
	}
}
