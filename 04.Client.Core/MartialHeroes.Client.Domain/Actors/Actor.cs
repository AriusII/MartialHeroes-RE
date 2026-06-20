using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors;

/// <summary>
/// A live in-world game entity (player character, monster, or NPC).
/// </summary>
/// <remarks>
/// <para>
/// Field selection is informed by the legacy actor layout. spec: Docs/RE/structs/actor.md
/// ("Actor - base fields the domain model needs"). Offsets cited below are documentation only —
/// this re-implementation does not mirror the binary layout, only the field meanings.
/// </para>
/// <para>
/// <b>Position type decision.</b> The legacy client stores world positions as <c>float</c>
/// (spec: Docs/RE/structs/actor.md "Coordinate type"), but this deterministic core uses
/// <see cref="Vector3Fixed"/> (Q16.16) as the authoritative logical position so the client and a
/// future headless server compute identical trajectories. Wire packets carry <c>float</c>; the
/// conversion to fixed-point happens at the application/handler boundary via
/// <see cref="Vector3Fixed.FromFloat"/>, never inside this layer. World Y stays 0 because the
/// server never sends Y (spec: same section).
/// </para>
/// <para>
/// <b>Computed maxima.</b> Maximum HP/MP/stamina are <em>not</em> stored fields; they are computed
/// from <see cref="Vitals"/> on demand (spec: Docs/RE/structs/actor.md "max_hp / max_mp are NOT
/// stored as fields"). Current HP is always capped against the computed maximum.
/// </para>
/// <para>
/// This is a mutable aggregate with controlled mutation methods; there are no public setters that
/// bypass invariants. Every mutation is deterministic and takes its time/elapsed inputs explicitly.
/// </para>
/// </remarks>
public sealed class Actor
{
    /// <summary>Composite identity (raw id + sort). spec: actor.md (id +0x5C, sort +0x60).</summary>
    public ActorKey Key { get; }

    /// <summary>Convenience accessor for the entity category. spec: actor.md (sort +0x60).</summary>
    public EntitySort Sort => Key.Sort;

    /// <summary>Character level. spec: actor.md / SpawnDescriptor level (+0x3A, boundary unverified).</summary>
    public ushort Level { get; private set; }

    /// <summary>Resolved vital capacities; supplies computed max HP/MP/stamina. spec: actor.md (computed maxima).</summary>
    public VitalStats Vitals { get; private set; }

    /// <summary>Current hit points, capped at <see cref="MaxHp"/>. spec: actor.md (current_hp +0xB0).</summary>
    public uint CurrentHp { get; private set; }

    /// <summary>Current mana / ki points, capped at <see cref="MaxMp"/>. spec: actor.md (current_mp +0xB4).</summary>
    public uint CurrentMp { get; private set; }

    /// <summary>Current stamina, capped at <see cref="MaxStamina"/>. spec: actor.md (current_stamina +0xB8).</summary>
    public uint CurrentStamina { get; private set; }

    /// <summary>Authoritative logical position (Q16.16). spec: actor.md (world_x +0xC0, world_z +0xC4; Y forced 0).</summary>
    public Vector3Fixed Position { get; private set; }

    /// <summary>Destination for movement interpolation (Q16.16). spec: actor.md (move_target +0x450).</summary>
    public Vector3Fixed MoveTarget { get; private set; }

    /// <summary>Facing yaw, raw Q16.16 radians-equivalent. spec: actor.md (yaw +0x4C0).</summary>
    public int Yaw { get; private set; }

    /// <summary>
    /// Movement speed in raw Q16.16 units per second.
    /// </summary>
    /// <remarks>
    /// The legacy <c>move_speed</c> (+0x64, default 1.0) is a float multiplier (spec: actor.md).
    /// This core stores the resolved speed as an integer raw-units-per-second so movement math stays
    /// integer-only and deterministic; conversion from the float multiplier happens at the boundary.
    /// </remarks>
    public long MoveSpeedRawPerSecond { get; private set; }

    /// <summary>Current target actor's raw id, 0 when none. spec: actor.md (target_id +0x6E8, default 0).</summary>
    public uint TargetRawId { get; private set; }

    /// <summary>Alive flag. spec: actor.md (alive +0x6EC, default 1).</summary>
    public bool IsAlive { get; private set; }

    /// <summary>PK (player-kill) mode flag. spec: actor.md (pk_flag +0x704, default 0).</summary>
    public bool IsPkEnabled { get; private set; }

    /// <summary>In-combat flag. spec: actor.md (combat_flag +0x705, default 0).</summary>
    public bool IsInCombat { get; private set; }

    /// <summary>Lifecycle / motion state. spec: actor.md (lifecycle_state +0x58C).</summary>
    public LifecycleState Lifecycle { get; private set; }

    /// <summary>
    /// The actor's current motion intent — the recovered movement classification the presentation
    /// drives an animation clip from. Set from the 5/13 movement update via
    /// <see cref="SetMotionIntent"/> alongside (not in place of) the existing snap/move-target
    /// behaviour. spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/specs/skinning.md §10.
    /// </summary>
    public MotionIntent Intent { get; private set; }

    /// <summary>Computed maximum hit points (base + equipment). spec: actor.md (computed maxima).</summary>
    public uint MaxHp => Vitals.MaxHp;

    /// <summary>Computed maximum mana / ki points (base + equipment). spec: actor.md (computed maxima).</summary>
    public uint MaxMp => Vitals.MaxMp;

    /// <summary>Computed maximum stamina (base + equipment). spec: actor.md (computed maxima).</summary>
    public uint MaxStamina => Vitals.MaxStamina;

    /// <summary>True when the actor has reached its current move target.</summary>
    public bool HasArrived => Position == MoveTarget;

    /// <summary>
    /// Spawns a live actor from already-decoded values.
    /// </summary>
    /// <remarks>
    /// All inputs are post-boundary deterministic values; the caller converts wire floats to
    /// <see cref="Vector3Fixed"/>/raw speed before calling. Current HP/MP/stamina are clamped to the
    /// computed maxima on construction (spec: actor.md vitals cap).
    /// </remarks>
    public Actor(
        ActorKey key,
        ushort level,
        VitalStats vitals,
        uint currentHp,
        uint currentMp,
        uint currentStamina,
        Vector3Fixed position,
        long moveSpeedRawPerSecond = 0,
        int yaw = 0)
    {
        if (moveSpeedRawPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(moveSpeedRawPerSecond), "Speed must be non-negative.");
        }

        Key = key;
        Level = level;
        Vitals = vitals;
        CurrentHp = Math.Min(currentHp, vitals.MaxHp);
        CurrentMp = Math.Min(currentMp, vitals.MaxMp);
        CurrentStamina = Math.Min(currentStamina, vitals.MaxStamina);
        Position = position;
        MoveTarget = position;
        Yaw = yaw;
        MoveSpeedRawPerSecond = moveSpeedRawPerSecond;
        TargetRawId = 0;
        IsAlive = true; // spec: actor.md alive default 1
        IsPkEnabled = false; // spec: actor.md pk_flag default 0
        IsInCombat = false; // spec: actor.md combat_flag default 0
        Lifecycle = LifecycleState.Refreshing;
    }

    // -------------------------------------------------------------------------
    // Vitals
    // -------------------------------------------------------------------------

    /// <summary>Replaces the resolved vital capacities and re-clamps current values to the new maxima.</summary>
    public void SetVitals(VitalStats vitals)
    {
        Vitals = vitals;
        CurrentHp = Math.Min(CurrentHp, vitals.MaxHp);
        CurrentMp = Math.Min(CurrentMp, vitals.MaxMp);
        CurrentStamina = Math.Min(CurrentStamina, vitals.MaxStamina);
    }

    /// <summary>Sets current HP, clamped to [0, <see cref="MaxHp"/>]. Reaching 0 marks the actor dead.</summary>
    public void SetCurrentHp(uint value)
    {
        CurrentHp = Math.Min(value, MaxHp);
        if (CurrentHp == 0)
        {
            Kill();
        }
    }

    /// <summary>Sets current MP, clamped to [0, <see cref="MaxMp"/>].</summary>
    public void SetCurrentMp(uint value) => CurrentMp = Math.Min(value, MaxMp);

    /// <summary>Sets current stamina, clamped to [0, <see cref="MaxStamina"/>].</summary>
    public void SetCurrentStamina(uint value) => CurrentStamina = Math.Min(value, MaxStamina);

    /// <summary>Applies <paramref name="amount"/> of damage, clamping HP at 0 and killing the actor when it reaches 0.</summary>
    public void ApplyDamage(uint amount)
    {
        if (!IsAlive)
        {
            return;
        }

        CurrentHp = amount >= CurrentHp ? 0 : CurrentHp - amount;
        if (CurrentHp == 0)
        {
            Kill();
        }
    }

    /// <summary>Heals <paramref name="amount"/> HP, saturating at <see cref="MaxHp"/>. No effect on a dead actor.</summary>
    public void Heal(uint amount)
    {
        if (!IsAlive)
        {
            return;
        }

        ulong healed = (ulong)CurrentHp + amount;
        CurrentHp = healed > MaxHp ? MaxHp : (uint)healed;
    }

    /// <summary>Restores <paramref name="amount"/> MP, saturating at <see cref="MaxMp"/>.</summary>
    public void RestoreMp(uint amount)
    {
        ulong restored = (ulong)CurrentMp + amount;
        CurrentMp = restored > MaxMp ? MaxMp : (uint)restored;
    }

    /// <summary>Restores <paramref name="amount"/> stamina, saturating at <see cref="MaxStamina"/>.</summary>
    public void RestoreStamina(uint amount)
    {
        ulong restored = (ulong)CurrentStamina + amount;
        CurrentStamina = restored > MaxStamina ? MaxStamina : (uint)restored;
    }

    // -------------------------------------------------------------------------
    // Regeneration tick
    // -------------------------------------------------------------------------

    /// <summary>
    /// Advances an HP regen ticker by <paramref name="deltaMs"/> and applies the regenerated HP,
    /// returning the updated ticker. Frame-rate independent and deterministic.
    /// </summary>
    public RegenTicker TickHpRegen(RegenTicker ticker, uint deltaMs)
    {
        (RegenTicker next, uint steps) = ticker.Advance(deltaMs);
        if (steps > 0 && IsAlive)
        {
            Heal(ticker.AmountFor(steps));
        }

        return next;
    }

    /// <summary>
    /// Advances an MP regen ticker by <paramref name="deltaMs"/> and applies the regenerated MP,
    /// returning the updated ticker. Frame-rate independent and deterministic.
    /// </summary>
    public RegenTicker TickMpRegen(RegenTicker ticker, uint deltaMs)
    {
        (RegenTicker next, uint steps) = ticker.Advance(deltaMs);
        if (steps > 0)
        {
            RestoreMp(ticker.AmountFor(steps));
        }

        return next;
    }

    // -------------------------------------------------------------------------
    // Movement
    // -------------------------------------------------------------------------

    /// <summary>Sets the movement destination (Q16.16) and switches lifecycle to walking when alive.</summary>
    public void SetMoveTarget(Vector3Fixed target)
    {
        MoveTarget = target;
        if (IsAlive && Position != target && Lifecycle != LifecycleState.Dead)
        {
            Lifecycle = LifecycleState.Walking;
        }
    }

    /// <summary>Snaps the position (and clears any pending movement) to <paramref name="position"/>.</summary>
    /// <remarks>Mirrors the legacy "instant snap" motion branch (spec: 5-13 MotionCode == 5).</remarks>
    public void SnapTo(Vector3Fixed position)
    {
        Position = position;
        MoveTarget = position;
    }

    /// <summary>Sets the facing yaw (raw Q16.16). spec: actor.md (yaw +0x4C0).</summary>
    public void SetYaw(int yaw) => Yaw = yaw;

    /// <summary>Sets the movement speed in raw Q16.16 units per second.</summary>
    public void SetMoveSpeed(long rawPerSecond)
    {
        if (rawPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawPerSecond), "Speed must be non-negative.");
        }

        MoveSpeedRawPerSecond = rawPerSecond;
    }

    /// <summary>
    /// Advances the actor toward <see cref="MoveTarget"/> by <paramref name="deltaMs"/> using its
    /// current speed; arrives exactly on the target with no overshoot. Dead actors do not move.
    /// </summary>
    /// <returns><c>true</c> if the actor reached (or already sat on) the target this tick.</returns>
    public bool AdvanceMovement(uint deltaMs)
    {
        if (!IsAlive || Position == MoveTarget)
        {
            return Position == MoveTarget;
        }

        (Vector3Fixed next, bool arrived) =
            LinearMovement.Step(Position, MoveTarget, MoveSpeedRawPerSecond, deltaMs);
        Position = next;
        return arrived;
    }

    // -------------------------------------------------------------------------
    // State flags & lifecycle
    // -------------------------------------------------------------------------

    /// <summary>Sets the current target actor id (0 clears the target). spec: actor.md (target_id +0x6E8).</summary>
    public void SetTarget(uint rawId) => TargetRawId = rawId;

    /// <summary>Sets the PK mode flag. spec: actor.md (pk_flag +0x704).</summary>
    public void SetPkEnabled(bool enabled) => IsPkEnabled = enabled;

    /// <summary>Sets the in-combat flag. spec: actor.md (combat_flag +0x705).</summary>
    public void SetInCombat(bool inCombat) => IsInCombat = inCombat;

    /// <summary>Sets the level. spec: actor.md (level, boundary unverified).</summary>
    public void SetLevel(ushort level) => Level = level;

    /// <summary>
    /// Transitions the lifecycle/motion state. Rejects setting <see cref="LifecycleState.Walking"/>
    /// or <see cref="LifecycleState.Running"/> on a dead actor; otherwise the transition is total.
    /// </summary>
    /// <returns><c>true</c> if the transition was accepted.</returns>
    public bool SetLifecycle(LifecycleState state)
    {
        if (!IsAlive && (state == LifecycleState.Walking || state == LifecycleState.Running))
        {
            return false;
        }

        Lifecycle = state;
        if (state == LifecycleState.Dead)
        {
            IsAlive = false;
        }

        return true;
    }

    /// <summary>
    /// Sets the actor's motion intent (the animation classification). Total and unconditional: the
    /// intent is a pure derived classification, not a lifecycle guard. spec:
    /// Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/specs/skinning.md §10.
    /// </summary>
    public void SetMotionIntent(MotionIntent intent) => Intent = intent;

    /// <summary>Marks the actor dead: HP to 0, out of combat, lifecycle to <see cref="LifecycleState.Dead"/>.</summary>
    public void Kill()
    {
        IsAlive = false;
        CurrentHp = 0;
        IsInCombat = false;
        Lifecycle = LifecycleState.Dead;
        MoveTarget = Position;
    }

    /// <summary>
    /// Revives a dead actor at <paramref name="hp"/> hit points (clamped to <see cref="MaxHp"/>,
    /// minimum 1) and resets lifecycle to <see cref="LifecycleState.Refreshing"/>.
    /// </summary>
    /// <returns><c>true</c> if the actor was dead and is now alive; <c>false</c> if already alive.</returns>
    public bool Revive(uint hp)
    {
        if (IsAlive)
        {
            return false;
        }

        IsAlive = true;
        uint clamped = Math.Min(hp == 0 ? 1u : hp, MaxHp);
        CurrentHp = clamped == 0 ? 0 : clamped;
        Lifecycle = LifecycleState.Refreshing;
        return true;
    }
}