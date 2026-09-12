using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Records the player's recent attack inputs and recognizes when they match a finisher's
/// required sequence. Lives on the player alongside PlayerCombat.
/// </summary>
public class AttackStringSystem : Component
{
	public static AttackStringSystem Local { get; private set; }

	[Property, Group( "Tuning" )] public float MaxGapBetweenAttacks { get; set; } = 1.5f;
	[Property, Group( "Tuning" )] public float FinisherGlobalCooldown { get; set; } = 1.1f;

	readonly List<AttackId> _history = new();
	float _lastInputTime;
	float _lastFinisherTime = -999f;

	protected override void OnAwake()
	{
		Local = this;
	}

	/// <summary>
	/// Call this every time the player successfully starts an attack (i.e. it wasn't blocked by cooldown).
	/// Returns the finisher that should play instead, or null if this should just be a normal attack.
	/// </summary>
	public FinisherDefinition RecordAndCheck( AttackId id )
	{
		var now = Time.Now;

		if ( now - _lastInputTime > MaxGapBetweenAttacks )
			_history.Clear();

		_lastInputTime = now;
		_history.Add( id );

		// Only need to keep as many entries as the longest finisher sequence.
		var maxLen = FinisherLibrary.All.Count == 0 ? 1 : FinisherLibrary.All.Max( f => f.Sequence.Length );
		if ( _history.Count > maxLen )
			_history.RemoveRange( 0, _history.Count - maxLen );

		for(int i = 0; i < _history.Count; i++ )
		{
			Log.Info( $"[{i}] {_history[i]}" );
		}

		if ( now - _lastFinisherTime < FinisherGlobalCooldown )
			return null;

		foreach ( var finisher in FinisherLibrary.All )
		{
			var seq = finisher.Sequence;
			if ( seq.Length > _history.Count )
				continue;

			bool match = true;
			for ( int i = 0; i < seq.Length; i++ )
			{
				if ( _history[_history.Count - seq.Length + i] != seq[i] )
				{
					match = false;
					break;
				}
			}

			if ( match )
			{
				_history.Clear();
				_lastFinisherTime = now;
				return finisher;
			}
		}

		return null;
	}

	/// <summary>Recent attacks, oldest first. Exposed for HUD reference highlighting.</summary>
	public IReadOnlyList<AttackId> History => _history;

	public void ResetRun()
	{
		_history.Clear();
		_lastInputTime = 0;
		_lastFinisherTime = -999f;
	}
}
