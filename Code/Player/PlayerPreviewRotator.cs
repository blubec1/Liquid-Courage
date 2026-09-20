namespace DrunkenBarFight;

/// <summary>
/// Holds the player preview stand facing straight at the camera by default (Elden Ring style - static,
/// dead-on front view), and lets the Customize screen's rotate buttons nudge it by a fixed increment
/// on click - no mouse drag support since this codebase has zero precedent for mouse-move Razor panel
/// events, and click buttons are the one interaction pattern already proven to work everywhere else
/// (MainMenu/PauseMenu/Hud all use plain onclick).
///
/// AutoSpinDegreesPerSecond defaults to 0 (no idle spin) - an earlier version auto-rotated
/// continuously, but that meant the character was rarely actually facing the camera straight-on,
/// which is exactly what a front-facing cosmetics screen shouldn't do. Yaw 0 (identity) faces the
/// stand's +X toward the PreviewCamera parked on the +X side, so it's front-on by default and only
/// turns when the player explicitly clicks a rotate button.
/// </summary>
public class PlayerPreviewRotator : Component
{
	public static PlayerPreviewRotator Instance { get; private set; }

	/// <summary>Idle auto-spin speed in degrees/second. 0 = hold still, facing the camera.</summary>
	[Property] public float AutoSpinDegreesPerSecond { get; set; } = 0f;

	/// <summary>The preview stand's own SkinnedModelRenderer - lives on this same GameObject. Exposed
	/// so PlayerOutfit can apply the player's live selections here too (see PlayerOutfit.
	/// ApplyToLivePlayerIfPresent), separately from the real in-game player's renderer, so the
	/// Customize screen actually shows real-time changes instead of the bare default model.</summary>
	public SkinnedModelRenderer BodyRenderer { get; private set; }

	float _manualOffset;

	protected override void OnAwake()
	{
		Instance = this;
		BodyRenderer = Components.Get<SkinnedModelRenderer>();
	}

	protected override void OnUpdate()
	{
		var yaw = ( Time.Now * AutoSpinDegreesPerSecond ) + _manualOffset;
		WorldRotation = Rotation.FromYaw( yaw );
	}

	/// <summary>Called from the Customize screen's rotate-left/rotate-right buttons.</summary>
	public void Nudge( float degrees )
	{
		_manualOffset += degrees;
	}

	public void SetVisible( bool visible )
	{
		// Toggle the whole stand GameObject, not just its SkinnedModelRenderer. ClothingContainer.Apply
		// parents every clothing item as a child GameObject with its own renderer, so disabling only the
		// stand's own renderer left the clothes floating in the world after entering a run. Disabling the
		// root hides the body, every clothing child, and stops this component's per-frame rotation in one
		// shot - re-enabling restores all of it for the next Customize visit.
		GameObject.Enabled = visible;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}
}
