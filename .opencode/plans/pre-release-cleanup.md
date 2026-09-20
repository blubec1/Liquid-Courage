# Pre-Release Cleanup Plan

Scope chosen by the user: leaderboard ident, dev keys, debug log spam, drunk-walk
(leave as-is), SETUP.md sync. **Not** included: Heavy rebalance, outfit log cleanup.

## Context / findings

- `.sbproj` is `Org: lastcall`, `Ident: last_call` -> published ident is `lastcall.last_call`.
- `DriveDrunkWalk()` already early-returns on an empty `DrunkWalkAnimation`
  (`PlayerAnimationDriver.cs:378`) and the scene sets `""`, so it is an intentional,
  guarded no-op. No stumble-walk clip exists in `Assets/`. Decision: **leave as-is.**
- Build/verify command:
  `dotnet build "C:\PROJECTS\Liquid-Courage\Code\drunken_bar_fight.csproj" -v q --nologo`

## 1. `Code/Leaderboard/LeaderboardService.cs` (release blocker)

- Replace the stale class comment (lines 12-15, the `IMPORTANT:` paragraph) so it no
  longer claims the project is under the "local" org.
- Line 20: `public const string GameIdent = "local.drunken_bar_fight";`
  -> `public const string GameIdent = "lastcall.last_call";`
- Line 19 summary: change to `The published package ident - must match Org.Ident in drunken_bar_fight.sbproj.`

Proposed replacement:

```csharp
/// The game ident below must match the Org.Ident of the published package in
/// drunken_bar_fight.sbproj (currently "lastcall.last_call"). Until the game is published under
/// that ident on sbox.game there is no real leaderboard to submit to - everything below is
/// written against the real Sandbox.Services API and starts working the moment it is.
/// </summary>
public static class LeaderboardService
{
	/// <summary>The published package ident - must match Org.Ident in drunken_bar_fight.sbproj.</summary>
	public const string GameIdent = "lastcall.last_call";
```

## 2. `Code/Drunkenness/DrunkennessSystem.cs` - remove debug keys

Delete the `M`/`N` dev block (lines 111-122), keeping the real `SoberUp` handling.
Old (111-124):

```csharp
		// DEV ONLY - quick keys to test drunkenness thresholds/overdrunk without grinding kills.
		// M = drink a glass (+15, same code path a real drink uses - triggers the toast/flash too).
		// N = sober up a glass (-15). Remove both before shipping. Uses raw keyboard, not an
		// Input.config action, since these are debug-only and shouldn't be player-rebindable.
		try
		{
			if ( Sandbox.Input.Keyboard.Pressed( "M" ) )
				DrinkGlass();
			if ( Sandbox.Input.Keyboard.Pressed( "N" ) )
				Reduce( GlassAmount );
		}
		catch { }

		if ( Input.Pressed( "SoberUp" ) )
```

New:

```csharp
		if ( Input.Pressed( "SoberUp" ) )
```

## 3. `Code/Combat/AttackStringSystem.cs` - remove per-attack log

Delete the history-dump loop (lines 45-48):

```csharp
		for(int i = 0; i < _history.Count; i++ )
		{
			Log.Info( $"[{i}] {_history[i]}" );
		}

```

Keep the surrounding `_history.RemoveRange(...)` and the finisher-cooldown check.

## 4. `Code/Player/PlayerAnimationDriver.cs` - strip tracing

Remove these `Log.Info` lines (keep every `Log.Warning`):

- `:146` `[AnimBlend] Resolved ... upper-body bones ...`
- `:170` `[AnimBlend] Hidden sample model created OK.`
- `:291` `[AnimBlend] '{sequenceName}': no sample model/bones, ...`
- `:305` `[AnimBlend] Playing '{sequenceName}' (blended, ...)`
- `:335` `[AnimBlend] Playing '{sequenceName}' (full-body swap, ...)`
- `:406` `[DrunkWalk] Entered drunk-walk loop ('{DrunkWalkAnimation}').`

Remove the `isFirstFrame` diagnostic block (`:476-477`, `:524-531`) plus the now-orphaned
counters:

- `:476` `bool isFirstFrame = _sequenceTime <= 0f;`
- `:477` `int hitCount = 0, missCount = 0;`
- `:509-512` collapse the miss branch:
  ```csharp
  				if ( !_bodyRenderer.TryGetBoneTransformAnimation( bone, out var animWorldTx ) )
  					continue;
  ```
- `:521` remove `hitCount++;`
- `:524-531` remove:
  ```csharp
  			if ( isFirstFrame )
  			{
  				// Dump a few raw transforms so we can verify the values are sane at runtime.
  				_bodyRenderer.TryGetBoneTransformAnimation( _overrideBones[0], out var diagAnim );
  				var diagAttack = _sampleModel.GetBoneWorldTransform( _overrideBones[0].Index );
  				Log.Info( $"[AnimBlend] First blend frame: {hitCount} bones overridden, {missCount} missed, weight={_currentBlendWeight:0.00}, sampleSeqTime={_sampleModel.CurrentSequence.Time:0.000}." );
  				Log.Info( $"[AnimBlend]   bone[0]='{_overrideBones[0].Name}' animPos={diagAnim.Position:0.00} attackPos={diagAttack.Position:0.00}" );
  			}
  ```

## 5. `SETUP.md:154` - doc sync

`flat 10 drunkenness` -> `flat 15 drunkenness` (keep `Max drunkenness is 150.` as-is).

## Verify

Run the build command; expect `Build succeeded` / `0 Error(s)`. Then confirm in the editor
console that no `[AnimBlend]`/`[DrunkWalk]` Info lines appear during normal play.

## Explicitly deferred

- Heavy rebalance (damage/impact) - user declined.
- `EnemyOutfitter` / `PlayerOutfit` `Log.Info` noise - not requested.
- `.sbproj` empty `Metadata` (no startup scene) - noted but out of scope.
- Actual drunk-walk clip - no art asset exists.
