using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Stats;
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
        IUnhandledOpcodeSink unhandled)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _unhandled = unhandled ?? throw new ArgumentNullException(nameof(unhandled));
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
    /// Any opcode without a specced fixed-size struct. Counted/logged via the injected sink; never
    /// throws, never blocks. spec: Docs/RE/opcodes.md (confirmed-routing but unspecced layouts).
    /// </summary>
    public void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload) =>
        _unhandled.Record(packedOpcode, payload.Length);

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

    private static VitalStats DefaultVitalsResolver(SpawnInfo info) =>
        // Placeholder resolution: seed capacity from the wire-reported current values so spawn HP is
        // not clamped away. NOT an original-game formula (spec: actor.md "max_hp NOT stored");
        // replace once the growth curve is documented.
        new(info.CurrentHp, info.CurrentMp, info.CurrentStamina, 0, 0, 0);
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
