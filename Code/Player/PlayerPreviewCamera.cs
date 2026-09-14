namespace DrunkenBarFight;

/// <summary>
/// Thin wrapper around the preview stand's dedicated CameraComponent so MainMenu.razor can toggle it
/// on/off by calling a plain static-instance method (same singleton pattern as GameManager.Instance,
/// IsoCameraRig.Instance etc. already used throughout this project) instead of reaching into the scene
/// graph directly.
///
/// IMPORTANT: the CameraComponent itself must start __enabled: true in bar.scene, NOT false. This
/// component disables it in code (see OnAwake below) immediately after caching the reference - if the
/// component started disabled at the scene-JSON level instead, Components.Get&lt;CameraComponent&gt;()
/// would never find it in the first place (disabled components aren't returned by a plain Get call),
/// so _camera would stay permanently null and SetActive(true) would silently do nothing. This was
/// exactly the bug that made the Customize screen show the normal gameplay camera through the
/// semi-transparent menu instead of the dedicated front-facing preview - fixed by starting the
/// CameraComponent enabled and disabling it here via the already-cached reference instead.
/// </summary>
public class PlayerPreviewCamera : Component
{
	public static PlayerPreviewCamera Instance { get; private set; }

	CameraComponent _camera;

	protected override void OnAwake()
	{
		Instance = this;
		_camera = Components.Get<CameraComponent>();

		if ( _camera is not null )
			_camera.Enabled = false;
	}

	public void SetActive( bool active )
	{
		if ( _camera is null )
			_camera = Components.Get<CameraComponent>();

		if ( _camera is not null )
			_camera.Enabled = active;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}
}
