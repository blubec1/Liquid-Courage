namespace DrunkenBarFight;

/// <summary>
/// Responsive top-down/isometric movement on top of CharacterController. Movement direction is
/// relative to the fixed isometric camera (not the player's facing, which is aim-driven - see
/// PlayerFacing), so WASD always maps to consistent screen directions.
/// </summary>
public class PlayerMovement : Component
{
	public static PlayerMovement Local { get; private set; }

	[Property, Group( "Tuning" )] public float BaseMoveSpeed { get; set; } = 260f;
	[Property, Group( "Tuning" )] public float Acceleration { get; set; } = 14f;
	[Property, Group( "Tuning" )] public float Gravity { get; set; } = 800f;

	CharacterController _cc;

	public Vector3 Velocity => _cc?.Velocity ?? Vector3.Zero;
	public bool IsMoving => new Vector3( Velocity.x, Velocity.y, 0 ).Length > 10f;

	protected override void OnAwake()
	{
		Local = this;
		_cc = Components.Get<CharacterController>();
	}

	protected override void OnFixedUpdate()
	{
		if ( _cc is null )
			return;

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
		{
			// Let the character settle to a stop when the run is over.
			ApplyVelocity( Vector3.Zero );
			return;
		}

		var forward = (Input.Down( "Forward" ) ? 1f : 0f) - (Input.Down( "Backward" ) ? 1f : 0f);
		var side = (Input.Down( "Right" ) ? 1f : 0f) - (Input.Down( "Left" ) ? 1f : 0f);

		var camForward = IsoCameraRig.Instance?.FlatForward ?? Vector3.Forward;
		var camRight = IsoCameraRig.Instance?.FlatRight ?? Vector3.Right;

		var wishDir = camForward * forward + camRight * side;
		var wishLen = wishDir.Length;
		if ( wishLen > 1f )
			wishDir /= wishLen;

		var speedMult = DrunkennessSystem.Local?.MoveSpeedMultiplier ?? 1f;
		var targetVelocity = wishDir * (BaseMoveSpeed * speedMult);

		ApplyVelocity( targetVelocity );
	}

	void ApplyVelocity( Vector3 targetFlatVelocity )
	{
		var current = _cc.Velocity;
		var currentFlat = new Vector3( current.x, current.y, 0 );
		var newFlat = Vector3.Lerp( currentFlat, targetFlatVelocity, System.Math.Clamp( Time.Delta * Acceleration, 0f, 1f ) );

		var z = _cc.IsOnGround ? -8f : current.z - Gravity * Time.Delta;

		_cc.Velocity = new Vector3( newFlat.x, newFlat.y, z );
		_cc.Move();
	}
}
