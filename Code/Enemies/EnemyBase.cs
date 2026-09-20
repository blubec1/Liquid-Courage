using System.Collections.Generic;
using System.Linq;

namespace DrunkenBarFight;

/// <summary>
/// Shared brain for every enemy archetype: a self-registering list (used by HitDetector instead
/// of physics queries), simple kinematic chase-and-attack AI, knockback, and the freeze-frame
/// stagger. Archetypes only override SetDefaults() and, optionally,
/// ResolveAttack()/OnKilled() for special behaviour.
/// </summary>
public abstract class EnemyBase : Component
{
	public static readonly List<EnemyBase> All = new();

	[Property, Group( "Stats" )] public float MaxHP { get; set; } = 30f;
	[Property, Group( "Stats" )] public float MoveSpeed { get; set; } = 110f;
	[Property, Group( "Stats" )] public float Damage { get; set; } = 6f;
	[Property, Group( "Stats" )] public float AttackRange { get; set; } = 55f;
	[Property, Group( "Stats" )] public float AttackCooldown { get; set; } = 1.2f;
	[Property, Group( "Stats" )] public float AttackWindup { get; set; } = 0f;

	// Hard floor on how close an enemy is ever allowed to get to the player, enforced every frame in
	// UpdateAi regardless of which movement path ran - this is what stops enemies from sliding into
	// or overlapping the player, independent of pathing quirks.
	// Bumped from 32: the player's own CharacterController.Radius is 16, and the citizen model's
	// collision hull is considerably wider than a point, so 32 left barely any real clearance -
	// enemies could still visually overlap/snag the player's capsule while "respecting" the old
	// minimum. 50 gives enough room that the two collision volumes don't actually touch.
	[Property, Group( "Stats" )] public float MinDistanceFromPlayer { get; set; } = 50f;

	// Capped by this archetype's own AttackRange - otherwise a
	// short-ranged archetype could get permanently held outside its own attack range by the standoff
	// clamp above and never be able to land a hit at all. Correctness (being able to attack) wins over
	// the spacing preference for any archetype whose range is tighter than the base standoff.
	float EffectiveMinDistance => System.MathF.Min( MinDistanceFromPlayer, AttackRange * 0.8f );

	[Property, Group( "Time Scaling" )] public float HpRampPerSecond { get; set; } = 0.006f;
	// Lowered from 0.004: at the old rate, a 5-minute run made every enemy hit ~2.2x harder on top
	// of getting surrounded more often, which turned late runs into an unavoidable death spiral.
	[Property, Group( "Time Scaling" )] public float DamageRampPerSecond { get; set; } = 0.0025f;

	[Property] public Sandbox.Citizen.CitizenAnimationHelper AnimHelper { get; set; }
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }

	public float CurrentHP { get; private set; }
	public bool IsDead { get; private set; }
	public float FreezeUntil { get; private set; }

	// Fastest any enemy is ever allowed to move - lowered well below the player's own BaseMoveSpeed
	// (260, see PlayerMovement) so the whole horde reads as noticeably slower than the player, not
	// just barely-slower. Applied as a clamp in OnAwake after each archetype's base speed +
	// per-instance jitter.
	public const float MaxEnemyMoveSpeed = 210f;

	// --- Fall-to-ground (see DriveFallToGround) and NavMesh pathfinding (see UpdateAi) - both
	// resolved defensively; either being missing/misbehaving just skips straight to the plain
	// kinematic behavior this project already had.
	//
	// NavMeshAgent-driven movement was tried once before and reverted because it pathed straight to
	// the player's exact WorldPosition with no notion of AttackRange, sliding enemies into/through
	// the player. This time the agent is given a standoff target (MinDistanceFromPlayer short of the
	// player, along the enemy's current approach direction) instead of the player's exact position,
	// and EnforceMinDistanceFromPlayer() hard-clamps the final position every frame as a safety net
	// regardless of which movement path ran - so obstacle-avoiding pathing is restored without the
	// clipping/overlap bug. ---
	Rigidbody _rigidbody;
	NavMeshAgent _navAgent;
	bool _hasLanded;
	float _spawnStartTime;

	float _spawnZ;
	float _nextAttackReady;
	bool _isWindingUp;
	float _windupEndTime;

	// Grace window after this enemy first becomes close enough to attack, before it's actually
	// allowed to swing - without this, an enemy that closed the gap right as its cooldown happened
	// to be ready could hit the player the instant it entered range, with zero warning. Similar idea
	// to AttackWindup, but keyed off "time since entering range" rather than "time since deciding to
	// swing", so it also covers windup-less archetypes (Brawler, SoberingBartender). Kept short on
	// purpose - this is meant to give a fair beat to react, not add a second real windup on top of
	// the existing one.
	const float MinTimeInRangeBeforeAttack = 0.1f;
	bool _wasInAttackRange;
	float _inAttackRangeSince = -999f;
	Vector3 _knockbackVelocity;
	// Smoothed heading for the non-NavMesh movement fallback (see UpdateAi) - without this, moveDir
	// is recomputed fresh every frame straight from raw direction-to-player + separation, so it can
	// flip a few degrees frame-to-frame as separation forces shift, reading as a twitchy/robotic
	// wobble instead of a real character turning with some inertia. NavMeshAgent already smooths its
	// own velocity internally so this is only needed on the fallback path.
	Vector3 _smoothedMoveDir;
	bool _pendingWasFinisher;

	// --- Hit-flash state (mirrors PlayerAnimationDriver.FlashHit) - a quick tint pulse on the body
	// so getting hit reads clearly on the ENEMY itself, independent of the knockback force pushing
	// it around. Without this, a hit only ever showed up as movement (the knockback slide) plus a
	// small VFX burst at the hit point - nothing about the enemy's own body said "you got hit". ---
	const float HitFlashDuration = 0.18f;
	float _hitFlashStartTime = -999f;
	Color _bodyBaseTint = Color.White;
	bool _hasCapturedBaseTint;
	bool _pendingWasEnvironmental;
	float _deathStartTime = -1f;
	float _lastHitDamage;

	// --- Attack bone-blend state (mirrors PlayerAnimationDriver's DriveAttackBoneOverrides - see
	// that file's doc comment for the full coordinate-space explanation. Kept local to EnemyBase
	// rather than shared so every archetype gets it for free with no extra wiring.) ---
	static readonly string[] UpperBodyBones =
	{
		"spine_0", "spine_1", "spine_2", "spine_3",
		"neck_0", "head",
		"clavicle_L", "clavicle_R",
		"arm_upper_L", "arm_upper_R",
		"arm_lower_L", "arm_lower_R",
		"hand_L", "hand_R",
	};

	// Bumped from 0.08/0.12 to match PlayerAnimationDriver's own increase - the shorter values
	// snapped in/out of the attack pose almost instantly, reading as stiff rather than fluid.
	const float AttackBlendIn = 0.12f;
	const float AttackBlendOut = 0.18f;

	SceneModel _sampleModel;
	BoneCollection.Bone[] _overrideBones;
	bool _attackSequenceActive;
	float _attackSequenceRevertTime;
	float _sequenceTime;
	float _currentBlendWeight;
	bool _boneBlendSetupTried;

	// Real physics ragdoll on death via Sandbox.ModelPhysics (added to this GameObject, which
	// already carries the SkinnedModelRenderer). The animgraph is switched off first so it stops
	// fighting the physics-driven pose. Total lifetime from death to despawn is 3 seconds: a short
	// ragdoll hold so the fall actually reads, then an opacity fade over the remainder.
	const float RagdollHoldDuration = 1.2f;
	const float FadeDuration = 1.8f;

	// The ragdoll is placed relative to the floor probed beneath the death spot (RagdollSpawnLift
	// above it), not the corpse's raw z - a corpse that died flush with ground (or clipped into it)
	// would otherwise materialize its ragdoll embedded in the floor. If that spawn site is inside
	// geometry or another entity, FindRagdollSpawnPosition falls back to a plain vertical raise.
	// These are the probe/fallback knobs; see FindRagdollSpawnPosition. (A sideways shimmy used to
	// run up to eight extra sphere sweeps per death - removed, it spiked trace cost hard in
	// horde-clear moments with many simultaneous kills.)
	const float RagdollSpawnLift = 45f;
	const float RagdollSpawnProbeHeight = 400f;
	const float RagdollSpawnBodyRadius = 20f;
	const float RagdollSpawnFallbackLift = 100f;

	// Knockback is tuned to look right as a kinematic slide (WorldPosition += velocity * Time.Delta
	// with drag decay). On death it never reaches the ragdoll directly - the ragdoll toss is driven
	// purely by the killing hit's damage, proportionally, so a heavy haymaker visibly launches the
	// body while a weak tap just tips it over. See QueueRagdollKick.
	//
	// These scales look huge, but they are deliberate: s&box ragdoll bodies are very heavy, so a
	// shove that reads as a satisfying launch here would barely nudge them at "normal" values. The
	// impulse lands on the torso bone only (see TryApplyRagdollToss), which works because Die() now
	// disables the leftover spawn Rigidbody and NavMeshAgent first - with the root body pinned, the
	// torso would tear away from the hips instead of carrying the whole body.
	const float RagdollDamageImpulseScale = 1000f;
	const float RagdollDamageLiftScale = 340f;
	const float RagdollDamageLiftCap = 60f;

	// Applied to every ragdoll body once they exist (see TagRagdollHierarchy), along with
	// EnhancedCcd. Damping bleeds the toss impulse and free-fall momentum quickly so the corpse
	// settles in place instead of sliding across the map after it lands.
	const float RagdollLinearDamping = 4f;
	const float RagdollAngularDamping = 4f;

	// The kill toss is queued at death and applied the first frame ModelPhysics actually has its
	// per-bone bodies. Those bodies don't exist synchronously inside Components.Create - they're
	// spawned on a later engine tick - so an immediate lookup (the old ApplyRagdollKick behavior)
	// found zero bodies and every corpse just dropped in place. See TryApplyRagdollToss.
	Vector3 _ragdollTossImpulse;
	bool _ragdollTossPending;

	// ModelPhysics builds the ragdoll as a hierarchy of per-bone physics GameObjects, and those
	// bodies do NOT inherit the root's "enemy" tag - so without this they fell back to the default
	// solid collision rule and physically collided with the playerblocker walls (playerblocker+solid
	// = Collide in Collision.config). Tagged lazily on the same tick the bodies appear, exactly like
	// the kill toss above, and only once.
	bool _ragdollTagged;

	// Citizen ragdoll torso candidates, best to worst. The skeleton uses spine_0..3 names (see
	// UpperBodyBones) and ModelPhysics builds one physics body per bone, matched by bone index -
	// so spine_1 is the mid-torso body the kill toss lands on, with graceful fallbacks if a
	// variant rig lacks it.
	static readonly string[] RagdollTorsoCandidates = { "spine_1", "spine_2", "spine_0" };

	protected virtual void SetDefaults() { }

	/// <summary>Called by the spawner once BodyRenderer is assigned, so archetypes can tint/scale themselves.</summary>
	public virtual void ApplyVisual() { }

	/// <summary>
	/// Called by HordeSpawner right after spawn with the current run's survival time, so the
	/// horde keeps getting tougher the longer a run goes - independent of the spawn-rate/archetype
	/// ramp HordeSpawner already does. Purely multiplicative on top of the archetype's own stats.
	/// </summary>
	public void ApplyTimeScaling( float survivalTime )
	{
		var t = System.MathF.Max( 0f, survivalTime );
		MaxHP *= 1f + t * HpRampPerSecond;
		Damage *= 1f + t * DamageRampPerSecond;
		CurrentHP = MaxHP;
	}

	/// <summary>Freezes this enemy in place for the given duration - reuses the same field the hit-stagger
	/// system already uses. Used by DrinkMeter so the whole crowd visibly pauses during a drink.</summary>
	public void SetFreeze( float duration )
	{
		FreezeUntil = System.MathF.Max( FreezeUntil, Time.Now + duration );
	}

	protected override void OnAwake()
	{
		SetDefaults();

		// Per-instance speed variation so a crowd of the same archetype doesn't all move in perfect
		// lockstep - widened again from +/-25% (0.75-1.25) to +/-40% (0.6-1.4), so a pack has real
		// stragglers and real front-runners instead of just a mild wobble. Still hard-clamped to
		// MaxEnemyMoveSpeed so no amount of jitter can ever let an enemy out-run the player.
		MoveSpeed *= 0.6f + System.Random.Shared.NextSingle() * 0.8f;
		MoveSpeed = System.MathF.Min( MoveSpeed, MaxEnemyMoveSpeed );

		// Per-instance attack-timing jitter (+/-15% cooldown, plus a randomized first-attack timer)
		// so a pack of the same archetype doesn't all wind up and swing in perfect unison - that
		// synced-loop feel is what read as "not continuous" even though each individual attack
		// looked fine.
		AttackCooldown *= 0.85f + System.Random.Shared.NextSingle() * 0.3f;
		_nextAttackReady = Time.Now + System.Random.Shared.NextSingle() * AttackCooldown;

		CurrentHP = MaxHP;
		_spawnZ = WorldPosition.z;
		_spawnStartTime = Time.Now;

		try { _rigidbody = Components.Get<Rigidbody>(); }
		catch { _rigidbody = null; }

		try { _navAgent = Components.Get<NavMeshAgent>(); }
		catch { _navAgent = null; }

		// No Rigidbody means nothing to fall with - treat as already landed at whatever height it
		// spawned at (the original behavior) rather than getting stuck waiting forever.
		if ( _rigidbody is null )
			_hasLanded = true;

		// Registered here (not OnStart) so an enemy spawned at runtime is immediately visible
		// to HitDetector/ComputeSeparation the same frame it's created.
		All.Add( this );
	}

	protected override void OnStart()
	{
		// BodyRenderer/AnimHelper are assigned by HordeSpawner right after AddComponent<T>(),
		// which runs after OnAwake but before the first OnStart - so this is the earliest safe
		// point to spin up the hidden sample model used for attack bone-blending.
		EnsureBoneBlendSetup();
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		try { _sampleModel?.Delete(); } catch { }
	}

	/// <summary>
	/// Resolves the upper-body bone list and spins up the hidden sample model used to scrub
	/// attack sequences without touching the main animgraph - same technique as
	/// PlayerAnimationDriver.SetupBoneBlending. Best-effort and idempotent: if it fails (or
	/// BodyRenderer isn't assigned yet), attacks just play with no animation instead of breaking
	/// anything.
	/// </summary>
	void EnsureBoneBlendSetup()
	{
		if ( _boneBlendSetupTried )
			return;

		_boneBlendSetupTried = true;

		try
		{
			if ( BodyRenderer?.Model is null )
				return;

			_overrideBones = UpperBodyBones
				.Select( name => BodyRenderer.Model.Bones.GetBone( name ) )
				.Where( b => b is not null )
				.ToArray();

			if ( _overrideBones.Length == 0 )
			{
				_overrideBones = null;
				return;
			}

			_sampleModel = new SceneModel( Scene.SceneWorld, BodyRenderer.Model, new Transform( new Vector3( 0, 0, -10000 ) ) );
			_sampleModel.UseAnimGraph = false;
			try { _sampleModel.RenderingEnabled = false; } catch { }
		}
		catch
		{
			_overrideBones = null;
			_sampleModel = null;
		}
	}

	/// <summary>Picks punch or heavy (mostly punch, occasional heavy for variety) and starts the
	/// bone-blend so the swing plays on the upper body while legs/hips keep walking/chasing
	/// normally - the same fix that stopped the player's legs freezing mid-attack.</summary>
	void PlayAttackAnimation()
	{
		EnsureBoneBlendSetup();

		if ( _overrideBones is null || _sampleModel is null || BodyRenderer is null )
			return;

		var def = AttackLibrary.Get( System.Random.Shared.NextSingle() < 0.3f ? AttackId.Heavy : AttackId.Punch );
		var duration = def.AnimationDuration > 0f ? def.AnimationDuration : 0.35f;

		try
		{
			_sampleModel.CurrentSequence.Name = def.Animation;
			_sampleModel.CurrentSequence.Time = 0f;

			_sequenceTime = 0f;
			_currentBlendWeight = 0f;
			_attackSequenceActive = true;
			_attackSequenceRevertTime = Time.Now + duration;
		}
		catch
		{
			_attackSequenceActive = false;
		}
	}

	/// <summary>Per-frame blend of the scrubbed attack pose onto just the upper-body bones - see
	/// PlayerAnimationDriver.DriveAttackBoneOverrides for the full coordinate-space explanation
	/// this mirrors exactly.</summary>
	void DriveAttackBoneOverrides()
	{
		if ( !_attackSequenceActive )
			return;

		if ( _sampleModel is null || _overrideBones is null || BodyRenderer is null )
		{
			_attackSequenceActive = false;
			return;
		}

		if ( Time.Now >= _attackSequenceRevertTime )
		{
			_attackSequenceActive = false;
			_currentBlendWeight = 0f;
			_sampleModel.Transform = new Transform( new Vector3( 0, 0, -10000 ) );
			try { BodyRenderer.ClearPhysicsBones(); } catch { }
			return;
		}

		try
		{
			_sequenceTime += Time.Delta;
			_sampleModel.CurrentSequence.Time = _sequenceTime;
			_sampleModel.Update( 0f );

			// Smoothstep instead of a raw linear ramp - see PlayerAnimationDriver.Smoothstep for the
			// same reasoning (a straight-line blend snaps to a noticeable speed change right at the
			// start/end of the fade, which read as mechanical rather than fluid).
			var fadeIn = AttackBlendIn > 0f ? Smoothstep( System.Math.Clamp( _sequenceTime / AttackBlendIn, 0f, 1f ) ) : 1f;
			var remaining = _attackSequenceRevertTime - Time.Now;
			var fadeOut = AttackBlendOut > 0f ? Smoothstep( System.Math.Clamp( remaining / AttackBlendOut, 0f, 1f ) ) : 1f;
			_currentBlendWeight = System.MathF.Min( fadeIn, fadeOut );

			_sampleModel.RenderingEnabled = false;
			_sampleModel.Transform = BodyRenderer.SceneModel.Transform;

			var modelWorldTx = BodyRenderer.SceneModel.Transform;

			foreach ( var bone in _overrideBones )
			{
				if ( !BodyRenderer.TryGetBoneTransformAnimation( bone, out var animWorldTx ) )
					continue;

				var attackWorldTx = _sampleModel.GetBoneWorldTransform( bone.Index );
				var blendedWorld = animWorldTx.LerpTo( attackWorldTx, _currentBlendWeight );
				var blendedLocal = modelWorldTx.ToLocal( blendedWorld );
				BodyRenderer.SetBoneTransform( bone, blendedLocal );
			}
		}
		catch
		{
			_attackSequenceActive = false;
			_currentBlendWeight = 0f;
			try { _sampleModel.Transform = new Transform( new Vector3( 0, 0, -10000 ) ); } catch { }
			try { BodyRenderer.ClearPhysicsBones(); } catch { }
		}
	}

	/// <summary>Classic ease-in/ease-out cubic (3t²-2t³) - see PlayerAnimationDriver.Smoothstep, kept
	/// as a separate copy here since EnemyBase and PlayerAnimationDriver aren't related types.</summary>
	static float Smoothstep( float t )
	{
		t = System.Math.Clamp( t, 0f, 1f );
		return t * t * (3f - 2f * t);
	}

	/// <summary>Quick red/white tint pulse on the body the instant a hit lands - see the field
	/// comment above. Mirrors PlayerAnimationDriver.FlashHit/DriveHitFlash exactly, just kept as a
	/// separate copy since EnemyBase and PlayerAnimationDriver aren't related types.</summary>
	void DriveHitFlash()
	{
		if ( BodyRenderer is null )
			return;

		try
		{
			if ( !_hasCapturedBaseTint )
			{
				_bodyBaseTint = BodyRenderer.Tint;
				_hasCapturedBaseTint = true;
			}

			var t = System.Math.Clamp( (Time.Now - _hitFlashStartTime) / HitFlashDuration, 0f, 1f );
			if ( t >= 1f )
			{
				BodyRenderer.Tint = _bodyBaseTint;
				return;
			}

			// Fast in, fast out - a sharp pulse rather than a slow fade, so it reads as "impact"
			// instead of a status effect.
			var pulse = 1f - t;
			BodyRenderer.Tint = Color.Lerp( _bodyBaseTint, new Color( 1f, 0.25f, 0.25f ), pulse );
		}
		catch { }
	}

	protected override void OnUpdate()
	{
		if ( IsDead )
		{
			// Ragdoll kill toss: applies the queued impulse the first frame physics bodies exist
			// (ModelPhysics spawns them a tick after Components.Create, see QueueRagdollKick).
			TryApplyRagdollToss();

			// Stamp the per-bone ragdoll bodies with the enemy/ragdoll tags once they exist, so the
			// corpse obeys the same collision rules as the live enemy instead of the default solid rule.
			TagRagdollHierarchy();

			// Physics (or nothing, if ragdoll setup failed) owns position/rotation from here on -
			// don't fight it with the manual knockback slide below.
			UpdateDeathVisual();
			return;
		}

		// Knockback slide always plays, even mid-stagger - it's part of the impact feedback.
		if ( _knockbackVelocity.Length > 1f )
		{
			var pos = WorldPosition + _knockbackVelocity * Time.Delta;
			pos = new Vector3( pos.x, pos.y, _spawnZ );
			WorldPosition = pos;
			_knockbackVelocity = Vector3.Lerp( _knockbackVelocity, Vector3.Zero, System.Math.Clamp( Time.Delta * 9f, 0f, 1f ) );
		}

		// Same reasoning as the knockback slide above - always plays, even mid-stagger/pause, so the
		// flash reliably reads on every hit regardless of what else is going on.
		DriveHitFlash();

		if ( AnimHelper is not null )
		{
			AnimHelper.WithVelocity( _knockbackVelocity );
			AnimHelper.WithWishVelocity( _knockbackVelocity );
			AnimHelper.IsGrounded = true;
		}

		// NavMeshAgent drives the GameObject toward its last MoveTo target every engine tick entirely
		// on its own, independent of whether this script calls MoveTo again (first found with the
		// pause bug - it kept walking through a paused game). The same problem was silently undoing
		// the knockback slide above: UpdateAi stops re-issuing MoveTo while staggered, but the agent
		// just kept steering back toward the LAST target it was given (right next to the player)
		// instead of standing still, fighting the manual knockback repositioning almost as fast as it
		// applied - which is why a kicked enemy never visibly went anywhere. Disabling the component
		// (a per-GameObject NavMeshAgent instance, not the scene-wide NavMesh system this project has
		// otherwise been deliberately careful never to touch from code) any time something else is
		// meant to own this enemy's position - paused, staggered, or actively sliding from knockback -
		// and handing it back only once none of those apply, fixes both symptoms with one gate.
		var wantsNavAgentActive = (GameManager.Instance is null || GameManager.Instance.State == RunState.Playing)
			&& Time.Now >= FreezeUntil
			&& _knockbackVelocity.Length <= 1f;

		if ( _navAgent is not null && _navAgent.Enabled != wantsNavAgentActive )
		{
			try { _navAgent.Enabled = wantsNavAgentActive; } catch { }
		}

		if ( GameManager.Instance is not null && GameManager.Instance.State != RunState.Playing )
			return;

		// Physics owns position while falling from an elevated spawn point - don't run attacks/AI
		// until it's actually settled on the ground.
		if ( !_hasLanded )
		{
			DriveFallToGround();
			return;
		}

		DriveAttackBoneOverrides();

		if ( Time.Now < FreezeUntil )
			return;

		UpdateAi();
	}

	/// <summary>Lets the spawn-time Rigidbody (gravity on) carry the enemy down from its spawn
	/// point/ring height to the real floor, then switches physics off and hands control back to the
	/// normal kinematic AI movement from wherever it landed. Enemies with no Rigidbody (or one that
	/// throws) are just treated as already-landed at spawn height - the original behavior.</summary>
	void DriveFallToGround()
	{
		if ( _rigidbody is null )
		{
			_hasLanded = true;
			return;
		}

		try
		{
			var elapsed = Time.Now - _spawnStartTime;
			var settled = _rigidbody.Velocity.Length < 8f && elapsed > 0.15f;

			// Safety net - if it's still not settled after 2s (stuck on geometry, whatever), just
			// accept wherever it currently is rather than leaving the enemy inert forever.
			if ( settled || elapsed > 2f )
			{
				_hasLanded = true;
				_rigidbody.MotionEnabled = false;
				_rigidbody.Gravity = false;
				_spawnZ = WorldPosition.z;
			}
		}
		catch
		{
			_hasLanded = true;
		}
	}

	void UpdateAi()
	{
		var player = PlayerStats.Local;
		if ( player is null || player.IsDead )
			return;

		// Corrects any overlap left over from last frame (movement overshoot, knockback, navmesh
		// quirks) before this frame's distance-based decisions run.
		EnforceMinDistanceFromPlayer( player );

		if ( _isWindingUp )
		{
			if ( Time.Now >= _windupEndTime )
			{
				_isWindingUp = false;
				ResolveAttack();
				_nextAttackReady = Time.Now + AttackCooldown;
			}
			return;
		}

		var toPlayer = player.WorldPosition - WorldPosition;
		toPlayer = new Vector3( toPlayer.x, toPlayer.y, 0 );
		var dist = toPlayer.Length;

		// Tracks how long this enemy has been continuously close enough to attack, reset the instant
		// it steps back out of range - see MinTimeInRangeBeforeAttack's comment for why.
		var inRangeNow = dist <= AttackRange;
		if ( inRangeNow && !_wasInAttackRange )
			_inAttackRangeSince = Time.Now;
		_wasInAttackRange = inRangeNow;

		if ( dist <= AttackRange && Time.Now >= _nextAttackReady && Time.Now >= _inAttackRangeSince + MinTimeInRangeBeforeAttack )
		{
			FacePoint( player.WorldPosition );

			if ( AttackWindup > 0f )
			{
				_isWindingUp = true;
				_windupEndTime = Time.Now + AttackWindup;
			}
			else
			{
				ResolveAttack();
				_nextAttackReady = Time.Now + AttackCooldown;
			}
			return;
		}

		if ( dist > AttackRange * 0.85f )
		{
			var dir = dist > 0.01f ? toPlayer / dist : Vector3.Forward;
			var separation = ComputeSeparation();
			var moveDir = (dir + separation).Length > 0.01f ? (dir + separation) : dir;
			moveDir = moveDir.Length > 0.01f ? moveDir / moveDir.Length : dir;

			// Prefer NavMesh pathfinding so enemies actually route around obstacles/buildings instead
			// of phasing through them. Unlike the earlier attempt, the agent is told to stop
			// MinDistanceFromPlayer short of the player (along the current approach direction) rather
			// than the player's exact position - that's what was causing enemies to slide into/through
			// the player. Falls back to plain direct-line-plus-separation movement if there's no agent
			// on this enemy or anything about it throws.
			var usedNavAgent = false;
			if ( _navAgent is not null )
			{
				try
				{
					var standoffTarget = player.WorldPosition - dir * EffectiveMinDistance;
					_navAgent.MoveTo( standoffTarget );
					var agentVel = _navAgent.Velocity;
					var agentVel2D = new Vector3( agentVel.x, agentVel.y, 0 );

					if ( agentVel2D.Length > 0.01f )
					{
						if ( AnimHelper is not null )
						{
							AnimHelper.WithVelocity( agentVel );
							AnimHelper.WithWishVelocity( agentVel );
						}

						FacePoint( player.WorldPosition );
					}

					usedNavAgent = true;
				}
				catch
				{
					usedNavAgent = false;
				}
			}

			if ( !usedNavAgent )
			{
				// Smooth the heading rather than snapping straight to this frame's raw direction - see
				// _smoothedMoveDir's field comment. Speed itself is untouched (MoveSpeed is applied
				// after), only the direction eases toward the target.
				_smoothedMoveDir = _smoothedMoveDir.Length > 0.01f
					? Vector3.Lerp( _smoothedMoveDir, moveDir, System.Math.Clamp( Time.Delta * 8f, 0f, 1f ) )
					: moveDir;

				var newPos = WorldPosition + _smoothedMoveDir * MoveSpeed * Time.Delta;
				WorldPosition = new Vector3( newPos.x, newPos.y, _spawnZ );

				if ( AnimHelper is not null )
				{
					var vel = _smoothedMoveDir * MoveSpeed;
					AnimHelper.WithVelocity( vel );
					AnimHelper.WithWishVelocity( vel );
				}

				FacePoint( player.WorldPosition );
			}

			// Safety net regardless of which path just ran - never end the frame closer than
			// MinDistanceFromPlayer, so pathing/physics overshoot can't cause visible clipping.
			EnforceMinDistanceFromPlayer( player );
		}
	}

	/// <summary>Hard floor on enemy-to-player distance - pushes the enemy back out along the
	/// away-from-player direction if it's ended up closer than EffectiveMinDistance. Cheap and
	/// direction-agnostic, so it works the same whether the overlap came from direct movement,
	/// NavMeshAgent overshoot, or knockback.</summary>
	void EnforceMinDistanceFromPlayer( PlayerStats player )
	{
		var toPlayer = player.WorldPosition - WorldPosition;
		toPlayer = new Vector3( toPlayer.x, toPlayer.y, 0 );
		var dist = toPlayer.Length;
		var minDist = EffectiveMinDistance;

		if ( dist >= minDist || dist < 0.01f )
			return;

		var away = -(toPlayer / dist);
		var pushedPos = player.WorldPosition + away * minDist;

		// Softly correct toward the resolved position instead of teleporting straight to it - an
		// instant snap read as a visible pop/stutter whenever a crowd jostled an enemy into the
		// player. The correction rate (20/sec) still resolves within a frame or two, so it doesn't
		// reopen the overlap/clipping bug this safety net exists to prevent.
		var corrected = Vector3.Lerp( WorldPosition, pushedPos, System.Math.Clamp( Time.Delta * 20f, 0f, 1f ) );
		WorldPosition = new Vector3( corrected.x, corrected.y, _spawnZ );
	}

	Vector3 ComputeSeparation()
	{
		var push = Vector3.Zero;
		foreach ( var other in All )
		{
			if ( other == this || other is null || other.IsDead )
				continue;

			var away = WorldPosition - other.WorldPosition;
			away = new Vector3( away.x, away.y, 0 );
			var d = away.Length;
			// Radius/strength both bumped - the old values were weak enough that a cluster of enemies
			// would happily overlap each other and pile into the same spot next to the player instead
			// of spreading out, which read as "stuck"/rubber-banding when several were pressed up
			// against the player at once.
			if ( d > 0.01f && d < 55f )
				push += (away / d) * (55f - d) * 0.09f;
		}
		return push;
	}

	void FacePoint( Vector3 point )
	{
		var dir = point - WorldPosition;
		dir = new Vector3( dir.x, dir.y, 0 );
		if ( dir.Length < 0.01f )
			return;

		var yaw = System.MathF.Atan2( dir.y, dir.x ) * (180f / System.MathF.PI);
		WorldRotation = Rotation.Slerp( WorldRotation, Rotation.FromYaw( yaw ), System.Math.Clamp( Time.Delta * 10f, 0f, 1f ) );
	}

	/// <summary>Override for archetype-specific attack resolution (default: flat damage if still in range).</summary>
	protected virtual void ResolveAttack()
	{
		PlayAttackAnimation();

		var player = PlayerStats.Local;
		if ( player is null )
			return;

		var dist = new Vector3( player.WorldPosition.x - WorldPosition.x, player.WorldPosition.y - WorldPosition.y, 0 ).Length;
		if ( dist <= AttackRange * 1.3f )
			player.TakeDamage( Damage );
	}

	/// <summary>Called by combat code when this enemy is hit by an attack or finisher.</summary>
	public void ApplyHit( float damage, Vector3 knockback, float staggerTime, bool fromFinisher = false, bool fromEnvironmental = false, bool showDamageNumber = true )
	{
		if ( IsDead )
			return;

		_lastHitDamage = damage;
		CurrentHP -= damage;
		FreezeUntil = System.MathF.Max( FreezeUntil, Time.Now + staggerTime );
		_knockbackVelocity += knockback;
		_hitFlashStartTime = Time.Now;
		_pendingWasFinisher |= fromFinisher;
		_pendingWasEnvironmental |= fromEnvironmental;

		if ( showDamageNumber )
			GameEvents.RaiseDamageNumber( WorldPosition + Vector3.Up * 62f, damage,
				fromFinisher ? DamageNumberKind.Finisher : DamageNumberKind.Normal );

		var severity01 = System.Math.Clamp( damage / 25f, 0.3f, 1.5f );
		var hitPos = WorldPosition + Vector3.Up * 55f;
		Vfx.HitSpark( hitPos, fromFinisher ? new Color( 1f, 0.4f, 0.2f ) : new Color( 1f, 0.75f, 0.3f ), severity01 );

		// BloodSpatter already centers itself at roughly chest height off WorldPosition - pass the
		// raw position, not hitPos (which is already +55 up), or the spatter doubles up to ~110
		// units above the ground and ends up floating over the enemy's head.
		Vfx.BloodSpatter( WorldPosition, severity01 );

		if ( CurrentHP <= 0 )
			Die();
	}

/// <summary>
	/// Finds where the ragdoll should materialize: above the real floor beneath the death spot
	/// (not the corpse's possibly-clipped z), skipping a site that would embed the body in
	/// geometry or another entity. If the death spot is blocked, it falls back to a plain vertical
	/// raise. Every probe is a short sphere sweep through the exact spawn elevation, because
	/// StartedSolid only reports "inside something" if the sweep actually travels through a solid
	/// volume; a long ground probe starting way up high never does.
	/// </summary>
	Vector3 FindRagdollSpawnPosition( Vector3 deathPos )
	{
		try
		{
			var probeTop = new Vector3( deathPos.x, deathPos.y, deathPos.z + RagdollSpawnProbeHeight );
			var probeBottom = new Vector3( deathPos.x, deathPos.y, deathPos.z - 50f );

			var ground = Scene.Trace.Sphere( RagdollSpawnBodyRadius, probeTop, probeBottom )
				.IgnoreGameObject( GameObject )
				.WithoutTags( "ragdoll" )
				.Run();

			var spawnAt = new Vector3( deathPos.x, deathPos.y, deathPos.z + RagdollSpawnLift );
			if ( ground.Hit )
				spawnAt.z = ground.HitPosition.z + RagdollSpawnLift;

			// The death spot is fine - spawn there. Otherwise take the vertical escape: an
			// airborne corpse that then drops is still better than one that materializes inside
			// a wall or another enemy's collider.
			if ( IsSpawnSiteClear( spawnAt ) )
				return spawnAt;

			return new Vector3( deathPos.x, deathPos.y, deathPos.z + RagdollSpawnFallbackLift );
		}
		catch
		{
			// Any trace failure falls back to the plain raised spot - dying can't break.
			return deathPos + Vector3.Up * RagdollSpawnLift;
		}
	}

	/// <summary>True when a corpse-wide sphere window around the spawn point is free of geometry and
	/// live enemies. Ignores this dying enemy and any existing ragdolls (dead bodies shouldn't force
	/// a live corpse elsewhere).</summary>
	bool IsSpawnSiteClear( Vector3 spawnAt )
	{
		try
		{
			var tr = Scene.Trace.Sphere( RagdollSpawnBodyRadius,
					new Vector3( spawnAt.x, spawnAt.y, spawnAt.z + 40f ),
					new Vector3( spawnAt.x, spawnAt.y, spawnAt.z - 40f ) )
				.IgnoreGameObject( GameObject )
				.WithoutTags( "ragdoll" )
				.Run();

			return !tr.StartedSolid && ( tr.Fraction >= 1f || tr.HitPosition.z <= spawnAt.z - 30f );
		}
		catch
		{
			return true;
		}
	}

	void Die()
	{
		if ( IsDead )
			return;

		IsDead = true;
		_deathStartTime = Time.Now;

		try
		{
			// Stop the animgraph from fighting the physics-driven pose.
			if ( AnimHelper is not null )
				AnimHelper.Enabled = false;

			if ( BodyRenderer?.SceneModel is not null )
				BodyRenderer.SceneModel.UseAnimGraph = false;

			// Disable the kinematic gameplay collider (added by HordeSpawner for character-vs-
			// character pushing) before handing off to ragdoll physics. Left enabled, it sits
			// exactly where the new per-bone ragdoll shapes spawn - guaranteed full overlap - and
			// the physics engine's own penetration-resolution shove was what actually launched
			// ragdolls "all over the place", independent of and much stronger than our knockback
			// velocity below. That knockback damping was correct; it just wasn't the cause.
			try
			{
				var kinematicCollider = Components.Get<ModelCollider>();
				if ( kinematicCollider is not null )
					kinematicCollider.Enabled = false;
			}
			catch { }

			// Stop the two components that still own the ROOT transform, before handing it to physics.
			// The ragdoll bodies ModelPhysics builds are children of this GameObject, so anything that
			// keeps moving the root drags the whole corpse with it:
			//  - NavMeshAgent keeps steering to its last MoveTo target (the standoff point near the
			//    player) every tick unless disabled. OnUpdate returns early once dead, before the
			//    alive-branch gate that normally toggles the agent, so a kill that lands while the
			//    agent is enabled leaves it driving the corpse toward that fixed spot - the "ragdolls
			//    move on their own toward a point on the map" bug.
			//  - The spawn Rigidbody (added by HordeSpawner for the fall from spawn) is left motion/
			//    gravity-off after landing, i.e. a kinematic body pinned at the death spot. With the
			//    root pinned, the torso-only kill toss tears the body away from the hips instead of
			//    carrying it - the mesh stretching across the view. ModelPhysics builds its own bodies,
			//    so this leftover one is safe to disable outright.
			try
			{
				if ( _navAgent is not null )
					_navAgent.Enabled = false;
			}
			catch { }

			try
			{
				if ( _rigidbody is not null )
					_rigidbody.Enabled = false;
			}
			catch { }

			// Release the attack bone-blend before physics takes the skeleton. DriveAttackBoneOverrides
			// writes the upper-body bones via SetBoneTransform, which routes to the SAME physics-bone
			// write path ModelPhysics uses, and its overrides persist until cleared. A death that
			// interrupts a swing means the blend's own ClearPhysicsBones never runs (OnUpdate returns
			// early once dead), so the last attack pose stays pinned - then ModelPhysics starts driving
			// the skeleton too, two writers feed one skin, and the mesh stretches to absurd proportions.
			// Clearing here, before the ragdoll exists, drops only those stale overrides.
			_attackSequenceActive = false;
			try { BodyRenderer?.ClearPhysicsBones(); } catch { }

			// Find a clear place for the ragdoll before physics spawns its per-bone bodies. The
			// corpse's own z can be flush with (or clipped into) the ground, and other enemies can
			// be standing exactly on the kill spot - spawning into either embeds the ragdoll and it
			// punches through / vanishes. See FindRagdollSpawnPosition.
			WorldPosition = FindRagdollSpawnPosition( WorldPosition );

			// Hand the body over to real physics. Citizen-based models ship with ragdoll bones
			// already set up, and this GameObject already carries the SkinnedModelRenderer, so
			// ModelPhysics picks it up the same way every other sibling-component setup in this
			// project auto-wires (AnimHelper -> renderer, etc.). The renderer/model links are set
			// explicitly anyway so the physics skeleton is always built from the correct model and
			// drives the renderer from the per-bone bodies (the field guide recommends this over
			// relying on auto-wiring; a mislinked ragdoll builds empty/phantom bodies on some spawns).
			var ragdollPhysics = Components.Create<Sandbox.ModelPhysics>();
			if ( ragdollPhysics is not null )
			{
				try
				{
					ragdollPhysics.Renderer = BodyRenderer;
					ragdollPhysics.Model = BodyRenderer?.Model;
					ragdollPhysics.MotionEnabled = true;
				}
				catch { }
			}

			// Toss the torso away from the player, scaled by the killing blow's damage. Split from
			// the knockback velocity so a haymaker-finisher kill launches the body while a weak
			// tap just tips it over - the impulse lands on the torso bone only, not the whole ragdoll.
			QueueRagdollKick();
		}
		catch { }

		var info = new EnemyKillInfo
		{
			Enemy = this,
			WasFinisher = _pendingWasFinisher,
			WasEnvironmental = _pendingWasEnvironmental,
			MultiKillIndex = MultiKillTracker.RegisterKillAndGetIndex(),
		};

		Vfx.DeathBurst( WorldPosition );
		Vfx.BloodSpatter( WorldPosition, 1.2f );

		GameEvents.RaiseEnemyKilled( info );
	}

	/// <summary>Computes the damage-proportional kill toss (flat away from the player, reversed
	/// facing if the kill happens dead-on, plus a damage-scaled upward lift) and stashes it as a
	/// pending impulse. It's applied later by TryApplyRagdollToss the moment the ragdoll's physics
	/// bodies exist.</summary>
	void QueueRagdollKick()
	{
		var playerPos = PlayerStats.Local?.WorldPosition;
		var away = playerPos is not null ? WorldPosition - playerPos.Value : -WorldRotation.Forward;
		away = new Vector3( away.x, away.y, 0 );
		if ( away.Length < 0.01f )
			away = -WorldRotation.Forward;
		away = away.Normal;

		var horiz = away * ( _lastHitDamage * RagdollDamageImpulseScale );
		var lift = System.MathF.Min( _lastHitDamage, RagdollDamageLiftCap ) * RagdollDamageLiftScale;
		_ragdollTossImpulse = new Vector3( horiz.x, horiz.y, lift );
		_ragdollTossPending = true;

		TryApplyRagdollToss();
	}

	/// <summary>Applies a pending kill toss to the ragdoll's torso body, once that body actually
	/// exists. ModelPhysics spawns its per-bone bodies a tick AFTER Components.Create returns, so
	/// this is called from the dead update every frame until the impulse lands (idempotent: clears
	/// the pending flag on first success, so the body never takes a double shove).</summary>
	void TryApplyRagdollToss()
	{
		if ( !_ragdollTossPending )
			return;

		var body = TorsoRagdollBody();
		if ( body is null )
			return;

		body.Value.Component.PhysicsBody.ApplyImpulse( _ragdollTossImpulse );
		_ragdollTossPending = false;
	}

	/// <summary>Resolves the ragdoll's torso physics body - the mid-spine body the kill toss pushes,
	/// matched by bone index through ModelPhysics.Bodies (the older PhysicsGroup form reads null on
	/// current builds). Falls back down the spine chain and finally to any body so the toss never
	/// silently drops on a variant rig.</summary>
	Sandbox.ModelPhysics.Body? TorsoRagdollBody()
	{
		try
		{
			var physics = Components.Get<Sandbox.ModelPhysics>();
			if ( physics?.Bodies is null || physics.Bodies.Count == 0 )
				return null;

			var model = physics.Model ?? BodyRenderer?.Model;
			if ( model?.Bones is not null )
			{
				foreach ( var name in RagdollTorsoCandidates )
				{
					var bone = model.Bones.GetBone( name );
					if ( bone is null )
						continue;

					foreach ( var b in physics.Bodies )
					{
						if ( b.Bone == bone.Index )
							return b;
					}
				}
			}

			return physics.Bodies[0];
		}
		catch
		{
			return null;
		}
	}

	void UpdateDeathVisual()
	{
		var elapsed = Time.Now - _deathStartTime;

		// Ragdoll physics (or nothing, if it failed to activate) fully owns position/rotation for the
		// entire hold+fade window, all the way to despawn - it should stay a real ragdoll the whole
		// time, never manually flipped, posed, or detached from code. (A previous version disabled
		// ModelPhysics and called ClearPhysicsBones once the fade began, meaning to stop it being a
		// physical obstacle - instead it snapped the body back to its bind/default pose mid-fade,
		// which looked broken. Reverted: the player already ignores ragdolls entirely via
		// CharacterController.IgnoreLayers, which is the fix that actually matters here.)
		if ( elapsed <= RagdollHoldDuration )
			return;

		var fadeT = System.Math.Clamp( (elapsed - RagdollHoldDuration) / FadeDuration, 0f, 1f );

		// Opacity fade - the body stays a full-size ragdoll the whole time and only turns
		// transparent, so it dissolves in place instead of shrinking the physics bodies
		// (a scale shrink mid-fade read as the corpse collapsing in on itself).
		try
		{
			if ( BodyRenderer is not null )
			{
				var baseTint = _hasCapturedBaseTint ? _bodyBaseTint : Color.White;
				BodyRenderer.Tint = new Color( baseTint.r, baseTint.g, baseTint.b, baseTint.a * (1f - fadeT) );
			}
		}
		catch { }

		if ( fadeT >= 1f )
			GameObject.Destroy();
	}

	/// <summary>Stamps the ragdoll ModelPhysics builds at death with the tags the live enemy carried,
	/// plus a dedicated "ragdoll" tag. The per-bone physics bodies are separate GameObjects that don't
	/// inherit the root's tags, so without this they resolve to the default solid rule and collide with
	/// playerblocker walls; tagged "enemy" they pass through (playerblocker defaults to Ignore) while
	/// "ragdoll" lets Collision.config make the player ignore corpses without also ignoring live enemies.
	/// Lazy for the same reason as the kill toss: the bodies don't exist until a tick after
	/// Components.Create.</summary>
	void TagRagdollHierarchy()
	{
		if ( _ragdollTagged )
			return;

		try
		{
			var physics = Components.Get<Sandbox.ModelPhysics>();
			if ( physics?.Bodies is null || physics.Bodies.Count == 0 )
				return;

			TagRecursive( GameObject, "enemy" );
			TagRecursive( GameObject, "ragdoll" );

			// Harden the per-bone bodies so a big kill toss can't catapult the corpse through the
			// floor/walls or leave it skating forever:
			//  - EnhancedCcd keeps fast bodies from tunneling through static geometry - exactly the
			//    bullets/rockets case the API docs describe. s&box CCD does NOT resolve against other
			//    EnhancedCcd bodies, so ragdoll limbs won't self-collide (the pitfall in other engines).
			//  - Damping bleeds the toss impulse and free-fall momentum quickly, so the corpse settles
			//    in place instead of sliding across the map after landing.
			foreach ( var b in physics.Bodies )
			{
				var pb = b.Component?.PhysicsBody;
				if ( pb is null )
					continue;

				pb.EnhancedCcd = true;
				pb.LinearDamping = RagdollLinearDamping;
				pb.AngularDamping = RagdollAngularDamping;
			}

			_ragdollTagged = true;
		}
		catch { }
	}

	/// <summary>Adds a tag to a GameObject and every descendant (recursively) - used to mark the whole
	/// ragdoll hierarchy ModelPhysics creates under an enemy at death, since tags set only on the root
	/// are not inherited by those per-bone bodies.</summary>
	static void TagRecursive( GameObject go, string tag )
	{
		go.Tags.Add( tag );

		foreach ( var child in go.Children )
			TagRecursive( child, tag );
	}
}
