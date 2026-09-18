namespace DrunkenBarFight;

/// <summary>
/// Dedicated spotlight that's the ONLY light active while the Customize screen is open - every other
/// ambient light in the scene (see SceneAmbientLight) gets switched off at the same time via
/// MainMenu.razor, so the preview character is lit purely by this one dramatic angled light instead of
/// the normal bar lighting.
///
/// Aimed at the preview stand's character in code (Rotation.LookAt), not via a hand-authored rotation
/// quaternion in bar.scene - much easier to get right, and to retune later, than guessing rotation
/// values by hand in JSON (move the GameObject in the editor and it re-aims itself next launch).
///
/// Same enable-by-code-not-by-scene pattern as PlayerPreviewCamera: the SpotLight component must start
/// __enabled: true in bar.scene, then this component disables it here via the already-cached reference -
/// a component that starts disabled at the scene-JSON level is never returned by Components.Get&lt;T&gt;()
/// in the first place, which would leave SetActive(true) silently doing nothing later.
/// </summary>
public class CustomizePreviewLight : Component
{
	public static CustomizePreviewLight Instance { get; private set; }

	SpotLight _light;

	protected override void OnAwake()
	{
		Instance = this;
		_light = Components.Get<SpotLight>();

		// Best-effort aim at the preview stand - if PlayerPreviewRotator hasn't awoken yet (scene
		// GameObject order), or the API differs from expected, this just falls back to whatever
		// rotation is already set on the GameObject in the editor.
		try
		{
			var stand = PlayerPreviewRotator.Instance;
			if ( stand is not null )
			{
				var direction = stand.WorldPosition - WorldPosition;
				if ( direction.Length > 0.01f )
					WorldRotation = Rotation.LookAt( direction );
			}
		}
		catch { }

		if ( _light is not null )
			_light.Enabled = false;
	}

	public void SetActive( bool active )
	{
		if ( _light is null )
			_light = Components.Get<SpotLight>();

		if ( _light is not null )
			_light.Enabled = active;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}
}
