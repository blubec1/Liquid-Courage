using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// A bottle prop placed in the bar. Walk near it and press Use to throw it in the direction
/// you're facing - a simple, supplemental environmental weapon, not a second combat system.
/// The visual child GameObject (assign in the editor) is hidden while the bottle is "in hand"
/// and respawns after a delay.
/// </summary>
public class ThrowableBottle : Component
{
	[Property] public GameObject Visual { get; set; }

	[Property, Group( "Tuning" )] public float InteractRadius { get; set; } = 75f;
	[Property, Group( "Tuning" )] public float RespawnDelay { get; set; } = 8f;
	[Property, Group( "Tuning" )] public float ThrowSpeed { get; set; } = 900f;
	[Property, Group( "Tuning" )] public float ThrowDamage { get; set; } = 24f;
	[Property, Group( "Tuning" )] public float ThrowKnockback { get; set; } = 260f;
	[Property, Group( "Tuning" )] public float ThrowRange { get; set; } = 650f;

	bool _consumed;
	float _respawnAt;

	protected override void OnAwake()
	{
		// Auto-discover a child named "Visual" if one wasn't wired up explicitly in the editor.
		Visual ??= GameObject.Children.FirstOrDefault( c => c.Name == "Visual" );
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		if ( _consumed )
		{
			if ( Time.Now >= _respawnAt )
			{
				_consumed = false;
				if ( Visual is not null )
					Visual.Enabled = true;
			}
			return;
		}

		var player = PlayerStats.Local;
		if ( player is null )
			return;

		var diff = player.WorldPosition - WorldPosition;
		var dist = new Vector3( diff.x, diff.y, 0 ).Length;

		if ( dist <= InteractRadius && Input.Pressed( "Use" ) )
		{
			Throw( player );
		}
	}

	void Throw( PlayerStats player )
	{
		_consumed = true;
		_respawnAt = Time.Now + RespawnDelay;
		if ( Visual is not null )
			Visual.Enabled = false;

		var facingComponent = player.Components.Get<PlayerFacing>();
		var direction = facingComponent?.FacingDirection ?? Vector3.Forward;

		var go = new GameObject
		{
			Name = "ThrownBottle",
			WorldPosition = player.WorldPosition + direction * 40f,
		};
		go.Tags.Add( "thrown_prop" );

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = new Color( 0.5f, 0.85f, 0.4f, 1f );
		go.WorldScale = Vector3.One * 0.15f;

		var projectile = go.AddComponent<ThrownBottle>();
		projectile.Direction = direction;
		projectile.Speed = ThrowSpeed;
		projectile.Damage = ThrowDamage;
		projectile.Knockback = ThrowKnockback;
		projectile.MaxRange = ThrowRange;
	}
}
