using System.Collections.Generic;

namespace DrunkenBarFight;

/// <summary>Tiny rolling window that turns "N kills close together in time" into a multi-kill index.</summary>
public static class MultiKillTracker
{
	const float WindowSeconds = 1.4f;
	static readonly List<float> _recentKillTimes = new();

	/// <summary>Call once per kill. Returns 0 for a lone kill, 1+ for each additional kill inside the window.</summary>
	public static int RegisterKillAndGetIndex()
	{
		var now = Time.Now;
		_recentKillTimes.RemoveAll( t => now - t > WindowSeconds );
		_recentKillTimes.Add( now );
		return _recentKillTimes.Count - 1;
	}

	public static void Reset() => _recentKillTimes.Clear();
}
