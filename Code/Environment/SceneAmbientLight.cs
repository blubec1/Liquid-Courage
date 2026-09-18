using System.Collections.Generic;

namespace DrunkenBarFight;

/// <summary>
/// Thin marker dropped on every "always on" ambient scene light (the Sun, the bar's point lights,
/// the Customize preview's key/fill lights) so MainMenu.razor can switch them all off at once while
/// the Customize screen is open - letting the dedicated CustomizePreviewLight spotlight be the only
/// thing lighting the character - then switch them back on leaving Customize or starting the run.
///
/// Self-registers into a static list (same pattern as EnemyBase.All/PlayerBlocker.All) rather than
/// querying the scene for a shared "Light" type, since DirectionalLight/PointLight/SpotLight are
/// separate concrete component types with no common queryable base used elsewhere in this codebase.
/// Toggling goes through the plain Component.Enabled property (same one already used everywhere for
/// CameraComponent/SkinnedModelRenderer), not anything light-type-specific, so this works unchanged
/// no matter which concrete light component actually lives on the GameObject.
/// </summary>
public class SceneAmbientLight : Component
{
	static readonly List<SceneAmbientLight> All = new();

	Component _light;

	protected override void OnAwake()
	{
		_light = (Component)Components.Get<DirectionalLight>();
		_light ??= Components.Get<PointLight>();
		_light ??= Components.Get<SpotLight>();

		All.Add( this );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
	}

	public static void SetAllActive( bool active )
	{
		foreach ( var entry in All )
		{
			if ( entry._light is not null )
				entry._light.Enabled = active;
		}
	}
}
