namespace DrunkenBarFight;

/// <summary>
/// Turns survival time into periodic "wave" milestone toasts + a small score bonus, so an
/// endless run still feels like it has visible progression. Also announces the first time
/// the player enters Last Call each run.
/// </summary>
public class MilestoneSystem : Component
{
	public static MilestoneSystem Instance { get; private set; }

	[Property] public float ExtraMilestoneInterval { get; set; } = 45f;
	[Property] public float ExtraMilestoneBaseBonus { get; set; } = 300f;
	[Property] public float ExtraMilestoneBonusPerWave { get; set; } = 50f;

	static readonly (float Time, string Text, float Bonus)[] FixedMilestones =
	{
		(30f, "Bar Fight Escalating!", 100f),
		(60f, "They're Calling For Backup!", 150f),
		(90f, "The Bruisers Show Up!", 200f),
		(120f, "Getting Rowdy!", 250f),
		(150f, "The Whole Bar Is Against You!", 300f),
		(180f, "Legendary Brawler!", 400f),
	};

	public int CurrentMilestoneIndex { get; private set; }

	float _nextExtraMilestoneTime;
	bool _extraInitialized;
	bool _announcedLastCallThisRun;

	protected override void OnAwake()
	{
		Instance = this;
		GameEvents.LastCallEntered += OnLastCallEntered;
	}

	protected override void OnDestroy()
	{
		GameEvents.LastCallEntered -= OnLastCallEntered;
	}

	void OnLastCallEntered()
	{
		if ( _announcedLastCallThisRun )
			return;

		_announcedLastCallThisRun = true;
		GameEvents.RaiseMilestone( "LAST CALL - one more drink could black you out." );
	}

	protected override void OnUpdate()
	{
		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		var t = GameManager.Instance.SurvivalTime;

		while ( CurrentMilestoneIndex < FixedMilestones.Length && t >= FixedMilestones[CurrentMilestoneIndex].Time )
		{
			var m = FixedMilestones[CurrentMilestoneIndex];
			CurrentMilestoneIndex++;
			GameEvents.RaiseMilestone( m.Text );
			ScoreSystem.Local?.AddScore( m.Bonus );
		}

		if ( CurrentMilestoneIndex >= FixedMilestones.Length )
		{
			if ( !_extraInitialized )
			{
				_extraInitialized = true;
				_nextExtraMilestoneTime = FixedMilestones[^1].Time + ExtraMilestoneInterval;
			}

			if ( t >= _nextExtraMilestoneTime )
			{
				CurrentMilestoneIndex++;
				var wave = CurrentMilestoneIndex - FixedMilestones.Length;
				GameEvents.RaiseMilestone( $"Still Standing - Wave {wave}" );
				ScoreSystem.Local?.AddScore( ExtraMilestoneBaseBonus + wave * ExtraMilestoneBonusPerWave );
				_nextExtraMilestoneTime = t + ExtraMilestoneInterval;
			}
		}
	}

	public void ResetRun()
	{
		CurrentMilestoneIndex = 0;
		_extraInitialized = false;
		_nextExtraMilestoneTime = 0;
		_announcedLastCallThisRun = false;
	}
}
