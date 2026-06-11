using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Routing;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

/// <summary>
/// The single inbound sink the <see cref="PacketRouter"/> dispatches into. Each typed overload
/// validates the wire message, applies it to the Domain via the <see cref="ClientWorld"/> registry
/// and the <see cref="Actor"/> controlled mutators, then publishes an immutable UI event on the
/// outbound <see cref="IClientEventBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Float -&gt; fixed boundary.</b> The wire carries IEEE-754 <c>float</c> world coordinates
/// (spec: Docs/RE/structs/actor.md "Coordinate type"; packets carry XZ-plane floats). This handler
/// is the application/network boundary, so it converts to the deterministic
/// <see cref="Vector3Fixed"/> via <see cref="Vector3Fixed.FromFloat"/> here and nowhere deeper. World
/// Y is forced to 0 because the server never sends it (spec: same section).
/// </para>
/// <para>
/// <b>No game-rule math.</b> Handlers compute nothing; they translate wire fields to Domain method
/// calls. Damage/stat/leveling formulas live in Domain.
/// </para>
/// <para>
/// <b>Threading.</b> Invoked by the single network-reader logical owner; it mutates Domain and the
/// registry without locking. Events published are immutable snapshots, so the UI consumer never sees
/// torn Domain state.
/// </para>
/// </remarks>
public sealed class GamePacketHandler : IPacketHandler
{
    private readonly ClientWorld _world;
    private readonly IClientEventBus _eventBus;
    private readonly ClientStateMachine _stateMachine;
    private readonly IUnhandledOpcodeSink _unhandled;
    private readonly ILoginHandshakeDriver? _loginDriver;

    /// <summary>
    /// Resolves the vital capacities for a freshly-spawned actor from its wire-reported current HP.
    /// </summary>
    /// <remarks>
    /// The SpawnDescriptor carries only <em>current</em> HP/MP/stamina; max HP/MP are not wire fields
    /// (spec: Docs/RE/structs/actor.md "max_hp / max_mp are NOT stored as fields" — they are computed
    /// from base stats + equipment). The real growth formula is not yet documented (the stat block at
    /// descriptor +0xD4 is unmapped), so this seam is injected: the composition root supplies the
    /// resolution, and Domain owns any actual formula. The default seeds capacity from the reported
    /// current values so spawn HP is not clamped away — a transparent placeholder, not a game formula.
    /// </remarks>
    public Func<SpawnInfo, VitalStats> VitalsResolver { get; init; } = DefaultVitalsResolver;

    public GamePacketHandler(
        ClientWorld world,
        IClientEventBus eventBus,
        ClientStateMachine stateMachine,
        IUnhandledOpcodeSink unhandled,
        ILoginHandshakeDriver? loginDriver = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _unhandled = unhandled ?? throw new ArgumentNullException(nameof(unhandled));
        _loginDriver = loginDriver; // optional: only needed for the login handshake flow
    }

    // -------------------------------------------------------------------------
    // 5/13 — actor movement update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/13 — actor movement update. Converts the wire float XZ coords to <see cref="Vector3Fixed"/>
    /// at this boundary, updates the actor's position/target/yaw, and emits <see cref="ActorMovedEvent"/>.
    /// spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/structs/actor.md (coords float).
    /// </summary>
    public void Handle(in SmsgActorMovementUpdate packet)
    {
        EntitySort sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // Float -> fixed conversion happens HERE, at the network/application boundary. World Y = 0.
        // spec: Docs/RE/structs/actor.md ("the server never sends Y and the client forces it to 0").
        Vector3Fixed position = Vector3Fixed.FromFloat(packet.PosX, 0f, packet.PosZ);
        Vector3Fixed target = Vector3Fixed.FromFloat(packet.DestX, 0f, packet.DestZ);
        int yaw = Vector3Fixed.FromFloat(packet.Yaw, 0f, 0f).RawX; // reuse Q16.16 conversion for the yaw scalar

        if (!_world.TryGet(key, out Actor actor))
        {
            // Movement for an actor we have not seen spawn: register a minimal placeholder so the
            // world stays consistent. Vitals are zero; a later spawn/vitals push fills them in.
            actor = new Actor(
                key,
                level: 0,
                vitals: VitalStats.Zero,
                currentHp: 0,
                currentMp: 0,
                currentStamina: 0,
                position: position,
                moveSpeedRawPerSecond: 0,
                yaw: yaw);
            _world.Add(actor);
        }

        // MotionCode == 5 is the legacy "instant snap" branch. spec: 5-13 (MotionCode), actor.md SnapTo.
        const byte instantSnapMotion = 5;
        if (packet.MotionCode == instantSnapMotion)
        {
            actor.SnapTo(position);
        }
        else
        {
            actor.SnapTo(position);          // seed the current position from the network sample
            actor.SetMoveTarget(target);     // then interpolate toward the destination
        }

        actor.SetYaw(yaw);
        actor.SetLifecycle(packet.RunFlag != 0 ? LifecycleState.Running : LifecycleState.Walking);

        _eventBus.Publish(new ActorMovedEvent(
            key, actor.Position, actor.MoveTarget, actor.Yaw, packet.RunFlag != 0));
    }

    // -------------------------------------------------------------------------
    // 5/3 — actor spawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/3 — actor spawn. Decodes the embedded 880-byte SpawnDescriptor (name, level, vitals, world
    /// XZ, server class), converts the float coords to <see cref="Vector3Fixed"/> at this boundary,
    /// creates and registers the Domain <see cref="Actor"/>, and emits <see cref="ActorSpawnedEvent"/>.
    /// spec: Docs/RE/packets/5-3_char_spawn.yaml; Docs/RE/structs/actor.md (SpawnDescriptor).
    /// </summary>
    public void Handle(in SmsgCharSpawn packet)
    {
        EntitySort sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // The SpawnDescriptor is an [InlineArray(880)] inside the packet; the language projects it to
        // a ReadOnlySpan<byte> with no copy. spec: packets/5-3_char_spawn.yaml (opaque 880-byte blob).
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        string name = reader.ReadName();
        ushort level = reader.ReadLevel();
        uint currentHp = reader.ReadCurrentHp();
        uint currentMp = reader.ReadCurrentMp();
        uint currentStamina = reader.ReadCurrentStamina();
        ushort serverClass = reader.ReadServerClass();

        // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
        Vector3Fixed position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(
            key,
            level,
            vitals,
            currentHp,
            currentMp,
            currentStamina,
            position);

        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    // -------------------------------------------------------------------------
    // 5/0 — actor despawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/0 — actor despawn. Removes the actor from the registry and emits
    /// <see cref="ActorDespawnedEvent"/>. spec: Docs/RE/packets/5-0_char_despawn.yaml.
    /// </summary>
    public void Handle(in SmsgCharDespawn packet)
    {
        EntitySort sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        _world.Remove(key);

        const byte leaveEffectBit = 0x01; // bit0 => play "left" SFX + chat line. spec: 5-0 Flags.
        bool playLeaveEffect = (packet.Flags & leaveEffectBit) != 0;
        _eventBus.Publish(new ActorDespawnedEvent(key, playLeaveEffect));
    }

    // -------------------------------------------------------------------------
    // 3/5 — enter-game ack
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/5 — enter-world acknowledgement. Drives the FSM into <see cref="ClientState.World"/> (which
    /// itself emits the <see cref="ClientStateChangedEvent"/>). spec:
    /// Docs/RE/packets/3-5_enter_game_response.yaml; Docs/RE/opcodes.md (3/5 transitions in-world).
    /// </summary>
    public void Handle(in SmsgEnterGameAck packet)
    {
        // BillingState / CharacterCount are available on the packet for a future use case; the
        // lifecycle transition is the load-bearing effect here.
        _ = packet.BillingState;
        _stateMachine.OnEnterWorld();
    }

    // -------------------------------------------------------------------------
    // Unhandled
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opcodes the typed <see cref="PacketRouter"/> seam does not dispatch (it routes only the core 4).
    /// We decode the additional S2C packets here by reinterpreting the payload via
    /// <see cref="MemoryMarshal.AsRef{T}"/> over the Network.Protocol struct, and drive the login
    /// handshake on 0/0. Anything else is counted via the injected sink; never throws, never blocks.
    /// spec: Docs/RE/opcodes.md.
    /// </summary>
    public void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload)
    {
        switch (packedOpcode)
        {
            case Opcodes.SmsgKeyExchange:                 // 0/0 — login key exchange
                HandleKeyExchange(payload);
                return;

            case Opcodes.SmsgActorVitalsAndPairState:     // 5/53 — actor vitals
                if (payload.Length >= SmsgActorVitalsAndPairState.WireSize)
                {
                    HandleVitals(in MemoryMarshal.AsRef<SmsgActorVitalsAndPairState>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgActorSpawnExtended:          // 5/1 — extended actor spawn
                if (payload.Length >= SmsgActorSpawnExtended.WireSize)
                {
                    HandleSpawnExtended(in MemoryMarshal.AsRef<SmsgActorSpawnExtended>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgStatUpdate:                  // 4/29 — stat update
                if (payload.Length >= SmsgStatUpdate.WireSize)
                {
                    HandleStatUpdate(in MemoryMarshal.AsRef<SmsgStatUpdate>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgLevelUp:                     // 5/32 — level up
                if (payload.Length >= SmsgLevelUp.WireSize)
                {
                    HandleLevelUp(in MemoryMarshal.AsRef<SmsgLevelUp>(payload));
                    return;
                }

                break;
        }

        _unhandled.Record(packedOpcode, payload.Length);
    }

    // -------------------------------------------------------------------------
    // 0/0 — login key exchange -> 1/4 Auth reply
    // -------------------------------------------------------------------------

    /// <summary>
    /// 0/0 — server KeyExchange. Delegates to the injected login driver, which parses the payload,
    /// builds the 1/4 reply from the staged credential, and sends it. spec: Docs/RE/specs/crypto.md §6.
    /// </summary>
    private void HandleKeyExchange(ReadOnlySpan<byte> payload)
    {
        if (_loginDriver is null)
        {
            _unhandled.Record(Opcodes.SmsgKeyExchange, payload.Length);
            return;
        }

        int replyBytes = _loginDriver.OnKeyExchange(payload);
        _eventBus.Publish(new LoginHandshakeCompletedEvent(replyBytes));
    }

    // -------------------------------------------------------------------------
    // 5/53 — actor vitals and pair state
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/53 — current vitals push. Updates the actor's HP/MP/stamina (capped against its computed
    /// maxima by Domain) and emits <see cref="ActorVitalsChangedEvent"/>. The sort value 8 normalises
    /// to 1. spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    private void HandleVitals(in SmsgActorVitalsAndPairState packet)
    {
        byte rawSort = packet.Sort == 8 ? (byte)1 : packet.Sort; // spec: 5-53 (sort 8 -> 1)
        var key = new ActorKey(packet.ActorId, ToEntitySort(rawSort));
        if (!_world.TryGet(key, out Actor actor))
        {
            // No known actor: nothing to update. A later spawn fills state in.
            _unhandled.Record(Opcodes.SmsgActorVitalsAndPairState, SmsgActorVitalsAndPairState.WireSize);
            return;
        }

        // CurrentHp HIGH CONFIDENCE; VitalC is the third vital mirrored to the local-player MP global;
        // Stamina HIGH CONFIDENCE. spec: 5-53 (vital ordering otherwise unconfirmed).
        actor.SetCurrentHp(packet.CurrentHp);
        actor.SetCurrentMp(packet.VitalC);
        actor.SetCurrentStamina(packet.Stamina);

        _eventBus.Publish(new ActorVitalsChangedEvent(
            key, actor.CurrentHp, actor.CurrentMp, actor.CurrentStamina));
    }

    // -------------------------------------------------------------------------
    // 5/1 — extended actor spawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/1 — extended actor spawn. Decodes the embedded 880-byte SpawnDescriptor (name, level, vitals,
    /// world XZ, class) via <see cref="SpawnDescriptorReader"/>, converts coords to fixed-point at this
    /// boundary, registers the Domain actor, and emits <see cref="ActorSpawnedEvent"/>. spec:
    /// Docs/RE/packets/5-1_actor_spawn_extended.yaml; Docs/RE/structs/spawn_descriptor.md.
    /// </summary>
    private void HandleSpawnExtended(in SmsgActorSpawnExtended packet)
    {
        EntitySort sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // The 880-byte descriptor is an [InlineArray] projected to a span with no copy. spec: 5-1.
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        string name = reader.ReadName();                 // SD +0x00. spec: spawn_descriptor.md
        ushort level = reader.ReadLevel();               // SD +0x3A. spec: spawn_descriptor.md
        uint currentHp = reader.ReadCurrentHp();         // SD +0x3C. spec: spawn_descriptor.md
        uint currentMp = reader.ReadCurrentMp();         // SD +0x40. spec: spawn_descriptor.md
        uint currentStamina = reader.ReadCurrentStamina(); // SD +0x44. spec: spawn_descriptor.md
        ushort serverClass = reader.ReadServerClass();   // SD +0x74. spec: spawn_descriptor.md

        // Float -> fixed at the boundary; world Y forced 0. spec: spawn_descriptor.md (+0x4C/+0x50).
        Vector3Fixed position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, position);
        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    // -------------------------------------------------------------------------
    // 4/29 — stat update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/29 — stat-allocation ack. Applied only when ResultOk == 1; emits
    /// <see cref="ActorStatsChangedEvent"/> with the five echoed absolute stats and remaining points.
    /// The five stat values are wire echoes (Domain owns no stat-allocation mutator yet), so this
    /// handler publishes the snapshot without re-deriving anything. spec:
    /// Docs/RE/packets/4-29_stat_update.yaml.
    /// </summary>
    private void HandleStatUpdate(in SmsgStatUpdate packet)
    {
        const byte applied = 1; // ResultOk == 1 applies the update. spec: 4-29.
        if (packet.ResultOk != applied)
        {
            return;
        }

        // The stat update targets the local player; key it on the known local actor when present.
        ActorKey key = _world.LocalActorKey ?? new ActorKey(packet.Handle, EntitySort.PlayerCharacter);

        _eventBus.Publish(new ActorStatsChangedEvent(
            key, packet.Stat0, packet.Stat1, packet.Stat2, packet.Stat3, packet.Stat4,
            packet.RemainingStatPoints));
    }

    // -------------------------------------------------------------------------
    // 5/32 — level up
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/32 — level up. Updates the actor's level and refreshed vitals, then emits
    /// <see cref="ActorLeveledUpEvent"/>. HP/MP are packed as two i32 halves in one i64 (HP = low,
    /// MP = high). spec: Docs/RE/packets/5-32_level_up.yaml (HpMpPacked, HIGH CONFIDENCE core).
    /// </summary>
    private void HandleLevelUp(in SmsgLevelUp packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(packet.Sort));

        // HP = low i32 half, MP = high i32 half of the packed value. spec: 5-32 (HpMpPacked).
        uint currentHp = unchecked((uint)(packet.HpMpPacked & 0xFFFF_FFFF));        // 0x14 low
        uint currentMp = unchecked((uint)((packet.HpMpPacked >> 32) & 0xFFFF_FFFF)); // 0x14 high
        uint currentStamina = unchecked((uint)packet.Stamina);                       // 0x1c

        if (_world.TryGet(key, out Actor actor))
        {
            actor.SetLevel(packet.NewLevel);
            actor.SetCurrentHp(currentHp);
            actor.SetCurrentMp(currentMp);
            actor.SetCurrentStamina(currentStamina);
        }

        _eventBus.Publish(new ActorLeveledUpEvent(
            key, packet.NewLevel, currentHp, currentMp, currentStamina, packet.RemainingStatPoints));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static EntitySort ToEntitySort(byte sort) => sort switch
    {
        1 => EntitySort.PlayerCharacter,   // spec: actor.md sort == 1
        2 => EntitySort.Monster,           // spec: actor.md sort == 2
        3 => EntitySort.NonPlayerCharacter,// spec: actor.md sort == 3
        _ => EntitySort.None,
    };

    /// <summary>
    /// Resolves vital capacities for a freshly-spawned actor via the recovered Domain formula
    /// (<see cref="VitalStats.FromFormula"/>). spec: Docs/RE/structs/stats.md.
    /// </summary>
    /// <remarks>
    /// PROVISIONAL: the spawn packet carries current HP/MP/stamina but not the primary stats or the
    /// external level/server bases the formula needs, so we feed what we have (class id from the
    /// spawn's server class; stats/bases left at their provisional 0 defaults). The resulting HP/MP
    /// maxima are structurally-correct but numerically-provisional until catalog/server data exists
    /// (spec: stats.md "External inputs (UNVERIFIED)"). Compose a richer resolver from above when the
    /// spawn's primary stats / equipment are decoded.
    /// <para>
    /// <b>Server-authoritative current-value guard.</b> The server already enforced the HP/MP/stamina
    /// cap before sending current values (spec: stats.md "the server enforces the cap"). Because the
    /// provisional zero-base formula yields unrealistically small maxima, the computed max is raised to
    /// at least the reported current value so the server-authoritative current HP/MP is not clamped
    /// away. Stamina has no growth curve in stats.md, so its max is the reported current. This guard is
    /// PROVISIONAL and removed once real bases/stats feed the formula.
    /// </para>
    /// </remarks>
    private static VitalStats DefaultVitalsResolver(SpawnInfo info)
    {
        var inputs = VitalFormulaInputs.Empty with
        {
            // ClassId indexes the per-class HP table; mapping UNVERIFIED (spec: stats.md). The server
            // class is a u16 here; the table is byte-indexed, so take the low byte. PROVISIONAL.
            ClassId = unchecked((byte)info.ServerClass),
            // EquipmentHpFlat/MpFlat, set bonuses, level/server bases left at 0 (provisional).
        };

        VitalStats formula = VitalStats.FromFormula(in inputs, maxStamina: info.CurrentStamina);

        // Provisional guard: never clamp the server-authoritative current values below what was sent.
        return new VitalStats(
            Math.Max(formula.MaxHp, info.CurrentHp),
            Math.Max(formula.MaxMp, info.CurrentMp),
            Math.Max(formula.MaxStamina, info.CurrentStamina));
    }
}

/// <summary>
/// Immutable inputs available when resolving a freshly-spawned actor's vital capacities.
/// </summary>
public readonly record struct SpawnInfo(
    ActorKey Key,
    ushort Level,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    ushort ServerClass);
