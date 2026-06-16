using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Routing;
using MartialHeroes.Shared.Kernel.Ids;
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
    private readonly LocalPlayerState? _localPlayer;
    private readonly CharacterSelectionStore? _characterSelection;
    private readonly AccountCharacterState? _accountCharacters;

    /// <summary>
    /// The combat-stat recompute seam: invoked whenever an equip / buff / level change should re-accumulate
    /// the local player's derived combat-stat aggregate. The composition root supplies the resolver (it owns
    /// the injected equipment / buff / server-base data the recompose needs); when absent, the recompose is
    /// skipped. The default is a no-op that returns the current aggregate unchanged. spec:
    /// Docs/RE/specs/combat.md §1 / §2 (re-accumulate on input change).
    /// </summary>
    public Func<CombatStats, CombatStats>? CombatStatsRecompute { get; init; }

    /// <summary>
    /// Per-skill cooldown duration resolver (ms) used when the 5/33 hotbar overwrite arms a recast slot.
    /// The Application/Assets layer owns the skills.scr catalogue lookup; when absent the duration is 0
    /// (a ready slot). spec: Docs/RE/specs/skills.md §4 (duration = cooldown_centiseconds × 100).
    /// </summary>
    public Func<SkillId, int>? CooldownDurationResolver { get; init; }

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
        ILoginHandshakeDriver? loginDriver = null,
        LocalPlayerState? localPlayer = null,
        CharacterSelectionStore? characterSelection = null,
        AccountCharacterState? accountCharacters = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _unhandled = unhandled ?? throw new ArgumentNullException(nameof(unhandled));
        _loginDriver = loginDriver; // optional: only needed for the login handshake flow
        _localPlayer = localPlayer; // optional: only needed for the skill/buff/combat subsystems
        _characterSelection = characterSelection; // optional: only needed for the 3/1 cache + 3/14 spawn
        _accountCharacters = accountCharacters; // optional: tracks the create/delete char-count deltas
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
            actor.SnapTo(position); // seed the current position from the network sample
            actor.SetMoveTarget(target); // then interpolate toward the destination
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
            case Opcodes.SmsgKeyExchange: // 0/0 — login key exchange
                HandleKeyExchange(payload);
                return;

            case Opcodes.SmsgActorVitalsAndPairState: // 5/53 — actor vitals
                if (payload.Length >= SmsgActorVitalsAndPairState.WireSize)
                {
                    HandleVitals(in MemoryMarshal.AsRef<SmsgActorVitalsAndPairState>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgActorSpawnExtended: // 5/1 — extended actor spawn
                if (payload.Length >= SmsgActorSpawnExtended.WireSize)
                {
                    HandleSpawnExtended(in MemoryMarshal.AsRef<SmsgActorSpawnExtended>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgStatUpdate: // 4/29 — stat update
                if (payload.Length >= SmsgStatUpdate.WireSize)
                {
                    HandleStatUpdate(in MemoryMarshal.AsRef<SmsgStatUpdate>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgLevelUp: // 5/32 — level up
                if (payload.Length >= SmsgLevelUp.WireSize)
                {
                    HandleLevelUp(in MemoryMarshal.AsRef<SmsgLevelUp>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgEquipItemResult: // 4/12 — equip/unequip result
                if (payload.Length >= SmsgEquipItemResult.WireSize)
                {
                    HandleEquipResult(in MemoryMarshal.AsRef<SmsgEquipItemResult>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgItemSlotStateAck: // 4/22 — item-slot state ack
                if (payload.Length >= SmsgItemSlotStateAck.WireSize)
                {
                    HandleItemSlotState(in MemoryMarshal.AsRef<SmsgItemSlotStateAck>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgNpcBuyOrAcquireAck: // 4/19 — NPC buy / acquire ack
                if (payload.Length >= SmsgNpcBuyOrAcquireAck.WireSize)
                {
                    HandleNpcAcquire(in MemoryMarshal.AsRef<SmsgNpcBuyOrAcquireAck>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgSkillHotbarSlotSet: // 5/33 — hotbar slot overwrite
                if (payload.Length >= SmsgSkillHotbarSlotSet.WireSize)
                {
                    HandleHotbarSlotSet(in MemoryMarshal.AsRef<SmsgSkillHotbarSlotSet>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgSkillHotbarAssignResult: // 4/41 — hotbar assign result
                if (payload.Length >= SmsgSkillHotbarAssignResult.WireSize)
                {
                    HandleHotbarAssignResult(in MemoryMarshal.AsRef<SmsgSkillHotbarAssignResult>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgSkillPointUpdate: // 4/150 — skill-point / level update (fixed 16-byte header)
                if (payload.Length >= SmsgSkillPointUpdateHeader.HeaderSize)
                {
                    HandleSkillPointUpdate(in MemoryMarshal.AsRef<SmsgSkillPointUpdateHeader>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgBuffSlotUpdate: // 5/31 — buff/status slot update
                if (HandleBuffSlotUpdate(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgStatsUpdate: // 5/67 — world-entry stat sync
                if (HandleStatsUpdate(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgCombatAttackUpdate: // 4/100 — combat attack / charge update
                if (HandleCombatAttackUpdate(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgChatBroadcast: // 5/7 — chat broadcast (36-byte header + text)
                if (HandleChatBroadcast(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgCharacterList: // 3/1 — character-select list (3-byte header + per-slot records)
                if (HandleCharacterList(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgCharSpawnResult: // 3/14 — enter-game spawn result (16-byte block). spec: opcodes.md (CAMPAIGN-10 ladder de-swap)
                if (payload.Length >= SmsgCharSpawnResult.WireSize)
                {
                    HandleCharSpawnResult(in MemoryMarshal.AsRef<SmsgCharSpawnResult>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgCharManageResult: // 3/7 — char manage / delete result (8-byte block). spec: opcodes.md (CAMPAIGN-10 ladder de-swap)
                if (payload.Length >= SmsgCharManageResult.WireSize)
                {
                    HandleCharManageResult(in MemoryMarshal.AsRef<SmsgCharManageResult>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgRenameCharResult: // 3/6 — rename result (19-byte block)
                if (payload.Length >= SmsgRenameCharResult.WireSize)
                {
                    HandleRenameCharResult(payload);
                    return;
                }

                break;

            case Opcodes.SmsgCharCreateResult: // 3/23 — character-create result (12-byte block)
                if (payload.Length >= SmsgCharCreateResult.WireSize)
                {
                    HandleCharCreateResult(in MemoryMarshal.AsRef<SmsgCharCreateResult>(payload));
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

        string name = reader.ReadName(); // SD +0x00. spec: spawn_descriptor.md
        ushort level = reader.ReadLevel(); // SD +0x3A. spec: spawn_descriptor.md
        uint currentHp = reader.ReadCurrentHp(); // SD +0x3C. spec: spawn_descriptor.md
        uint currentMp = reader.ReadCurrentMp(); // SD +0x40. spec: spawn_descriptor.md
        uint currentStamina = reader.ReadCurrentStamina(); // SD +0x44. spec: spawn_descriptor.md
        ushort serverClass = reader.ReadServerClass(); // SD +0x74. spec: spawn_descriptor.md

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
        uint currentHp = unchecked((uint)(packet.HpMpPacked & 0xFFFF_FFFF)); // 0x14 low
        uint currentMp = unchecked((uint)((packet.HpMpPacked >> 32) & 0xFFFF_FFFF)); // 0x14 high
        uint currentStamina = unchecked((uint)packet.Stamina); // 0x1c

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
    // 4/12 — equip/unequip result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/12 — equip/unequip result. On success applies the equipment-slot/visual update to the local
    /// player and triggers a combat-stat recompute (equipment changed); ToSlot 15 forces a title-slot
    /// visual rebuild. spec: Docs/RE/specs/handlers.md §3 (4/12); Docs/RE/structs/item.md.
    /// </summary>
    private void HandleEquipResult(in SmsgEquipItemResult packet)
    {
        const byte ok = 1; // result 1 = success. spec: handlers.md §3 (4/12 result byte).
        const byte titleSlot = 15; // ToSlot 15 = title/gear visual rebuild. spec: handlers.md §3 / item.md.
        bool success = packet.Result == ok;

        if (success)
        {
            // Equipment changed -> the derived combat-stat aggregate must be re-accumulated. spec: combat.md §2.
            RecomputeCombatStats();
        }

        _eventBus.Publish(new EquipResultEvent(
            success, packet.FromSlot, packet.ToSlot, packet.ToSlot == titleSlot));
    }

    // -------------------------------------------------------------------------
    // 4/22 — item-slot state ack
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/22 — item-slot state ack: a slot's state plus stat/enchant fields. On success a recompute is
    /// triggered (the slot's stats may feed the aggregate). spec: Docs/RE/specs/handlers.md §13 Group B
    /// (4/22); Docs/RE/structs/item.md.
    /// </summary>
    private void HandleItemSlotState(in SmsgItemSlotStateAck packet)
    {
        const byte ok = 1; // result 1 = ok. spec: item.md (4/22 result byte).
        bool success = packet.Result == ok;

        if (success)
        {
            RecomputeCombatStats(); // a slot's stat/enchant fields may change the aggregate. spec: combat.md §2.
        }

        _eventBus.Publish(new ItemSlotStateEvent(
            success, packet.FromSlot, packet.ToSlot, packet.BonusField1, packet.BonusField2, packet.BonusField3));
    }

    // -------------------------------------------------------------------------
    // 4/19 — NPC buy / acquire ack
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/19 — NPC buy / inventory-acquire ack. Publishes the acquire outcome (slot, item actor id, gold).
    /// spec: Docs/RE/specs/handlers.md §13 Group B (4/19); Docs/RE/structs/item.md.
    /// </summary>
    private void HandleNpcAcquire(in SmsgNpcBuyOrAcquireAck packet)
    {
        const byte ok = 1; // result 1 = ok. spec: item.md (4/19 result byte).
        bool success = packet.Result == ok;

        _eventBus.Publish(new NpcAcquireResultEvent(
            success, packet.ReasonCode, packet.BagSlotIndex, packet.ItemQuadB, packet.GoldLo));
    }

    // -------------------------------------------------------------------------
    // 5/33 — skill hotbar slot overwrite
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/33 — authoritative server overwrite of one skill-hotbar slot for the local player. Writes the
    /// {skill, points} entry into the 240-slot hotbar (mirroring the cooldown duration), then emits the
    /// snapshot. Ignored when no <see cref="LocalPlayerState"/> is wired. spec: Docs/RE/specs/handlers.md §4
    /// (5/33); Docs/RE/structs/skill.md.
    /// </summary>
    private void HandleHotbarSlotSet(in SmsgSkillHotbarSlotSet packet)
    {
        // HotbarSlot must be < 240. spec: structs/skill.md (hotbar_slot < 0xF0).
        if (packet.HotbarSlot >= SmsgSkillHotbarSlotSet.HotbarSlotCount)
        {
            _unhandled.Record(Opcodes.SmsgSkillHotbarSlotSet, SmsgSkillHotbarSlotSet.WireSize);
            return;
        }

        var skill = new SkillId(unchecked((uint)packet.SkillId));
        if (_localPlayer is not null)
        {
            int cooldownMs =
                CooldownDurationResolver?.Invoke(skill) ?? 0; // skills.scr lookup; 0 = ready. spec: skills.md §4.
            _localPlayer.SetHotbarSlot(packet.HotbarSlot, skill, packet.SkillPoints, cooldownMs);
        }

        _eventBus.Publish(new SkillHotbarSlotSetEvent(packet.HotbarSlot, skill, packet.SkillPoints));
    }

    // -------------------------------------------------------------------------
    // 4/41 — skill hotbar assign result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/41 — result of a client-initiated hotbar assignment. spec: Docs/RE/specs/handlers.md §13 Group C
    /// (4/41); Docs/RE/structs/skill.md.
    /// </summary>
    private void HandleHotbarAssignResult(in SmsgSkillHotbarAssignResult packet)
    {
        const byte ok = 1; // gate 1 = apply/ok. spec: structs/skill.md (4/41 gate).
        bool success = packet.Gate == ok;

        _eventBus.Publish(new SkillHotbarAssignResultEvent(
            success, packet.ResultCode, packet.HotbarSlotEcho,
            new SkillId(unchecked((uint)packet.SkillIdEcho)), packet.SkillPointPool));
    }

    // -------------------------------------------------------------------------
    // 4/150 — skill-point / level update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/150 — skill-point update (fixed 16-byte header). Mode 1 sets the total skill-point pool; mode 2
    /// is a level-up notice (Value = new level), which also updates the local actor's level. The 255
    /// display cap is UI-only; the wire value is not clamped. spec: Docs/RE/specs/handlers.md §13 Group F
    /// (4/150); Docs/RE/structs/skill.md.
    /// </summary>
    private void HandleSkillPointUpdate(in SmsgSkillPointUpdateHeader packet)
    {
        const byte valid = 1; // +0 must equal 1. spec: structs/skill.md (valid).
        if (packet.Valid != valid)
        {
            _unhandled.Record(Opcodes.SmsgSkillPointUpdate, SmsgSkillPointUpdateHeader.HeaderSize);
            return;
        }

        const uint levelUpMode = 2; // mode 2 = level-up notice; Value = new level. spec: structs/skill.md (mode).
        if (packet.Mode == levelUpMode
            && _world.LocalActor is { } local
            && packet.Value <= ushort.MaxValue)
        {
            local.SetLevel((ushort)packet.Value);
            RecomputeCombatStats(); // level changed -> recompose. spec: combat.md §2.
        }

        _eventBus.Publish(new SkillPointUpdateEvent(packet.Mode, packet.Value));
    }

    // -------------------------------------------------------------------------
    // 5/31 — buff/status slot update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/31 — buff/status slot update. Writes the 12-byte status entry into the per-actor buff table; for
    /// the local player it mirrors into <see cref="LocalPlayerState.Buffs"/> and recomputes combat stats
    /// (a buff is a stat contribution). Only the per-actor table regime (slot 0..30) mutates Domain;
    /// larger slot regimes are surfaced as events only. spec: Docs/RE/specs/handlers.md §4 (5/31);
    /// Docs/RE/specs/skills.md §6.1.
    /// </summary>
    private bool HandleBuffSlotUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 56; // Min fixed payload 56 (0x38). spec: handlers.md §4 (5/31).
        if (payload.Length < minSize)
        {
            return false;
        }

        // (sort@+0, id@+4) actor key; slot@+8; effect code@+12; value/duration@+16; extra/param@+20.
        // spec: handlers.md §4 (5/31 fields).
        byte sort = payload[0x00];
        uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        uint slot = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        uint effectCode = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        uint duration = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x10, 4));
        uint extra = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x14, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        // Per-actor table regime: small slots (0..30) write the local mirror. The >30 / >=1,000,000 regimes
        // are global / per-actor-only and not modelled in this 31-slot local table. spec: skills.md §6.1.
        bool isLocal = _world.LocalActorKey == key;
        if (isLocal && _localPlayer is not null && slot < (uint)BuffTable.SlotCount)
        {
            // Magnitude (the parallel secondary-table strength) is not in this 56-byte read; default 0.
            // spec: skills.md §6.1 (secondary magnitude table separate).
            _localPlayer.Buffs.Apply(
                (int)slot, unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra), magnitude: 0);
            RecomputeCombatStats(); // a buff changed -> recompose. spec: combat.md §2.2.
        }

        _eventBus.Publish(new BuffSlotChangedEvent(
            key, unchecked((int)slot), unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra)));
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/67 — world-entry stat sync
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/67 — world-entry stat sync. Writes the neutral stat slots and current XP onto the actor's level
    /// (XP only) and publishes the snapshot. The neutral slot numbering (stat0/2/4/5/6) is preserved
    /// pending a named-stat mapping; no game-rule remap happens here. spec: Docs/RE/specs/handlers.md §4
    /// (5/67).
    /// </summary>
    private bool HandleStatsUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 36; // Min fixed payload 36 (0x24). spec: handlers.md §4 (5/67).
        if (payload.Length < minSize)
        {
            return false;
        }

        // sort@+0; id@+4; stat0@+8; stat2@+12; current-XP i64@+16; stat6@+24; stat4@+28; stat5@+32.
        // spec: handlers.md §4 (5/67 fields).
        byte sort = payload[0x00];
        uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        uint stat0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        uint stat2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        long currentXp = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0x10, 8));
        uint stat6 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x18, 4));
        uint stat4 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x1C, 4));
        uint stat5 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x20, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        _eventBus.Publish(new ActorStatSyncEvent(key, stat0, stat2, stat4, stat5, stat6, currentXp));
        return true;
    }

    // -------------------------------------------------------------------------
    // 4/100 — combat attack / charge update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/100 — combat-attack / charge UI state update. Decodes only the documented phase/sub-kind/value
    /// fields (the remaining ~176 bytes are opaque per the spec and not surfaced). Phase 3 starts a timed
    /// charge; phase 5 ends it. spec: Docs/RE/specs/handlers.md §3 (4/100).
    /// </summary>
    private bool HandleCombatAttackUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 188; // Min fixed payload 188 (0xBC). spec: handlers.md §3 (4/100).
        if (payload.Length < minSize)
        {
            return false;
        }

        // phase@+8 (u8); sub-kind@+10 (i8, 0xFF = reset); value@+12 (u32). spec: handlers.md §3 (4/100).
        byte phase = payload[0x08];
        sbyte subKind = unchecked((sbyte)payload[0x0A]);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));

        const byte startCharge = 3; // phase 3 starts a timed charge. spec: handlers.md §3 (4/100).
        const byte endCharge = 5; // phase 5 ends it. spec: handlers.md §3 (4/100).

        _eventBus.Publish(new CombatAttackUpdateEvent(
            phase, subKind, value, phase == startCharge, phase == endCharge));
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/7 — chat broadcast
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/7 — server chat broadcast. Decodes the 36-byte header struct, then the variable text body that
    /// follows it. The body length encoding is unconfirmed; we read a length-prefixed block when one is
    /// present and otherwise treat the remainder as the text (decoding the leading printable run).
    /// CP949 -&gt; managed string at this presentation boundary. spec:
    /// Docs/RE/packets/5-7_chat_broadcast.yaml; Docs/RE/specs/handlers.md §17.12.
    /// </summary>
    private bool HandleChatBroadcast(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgChatBroadcastHeader.HeaderSize)
        {
            return false;
        }

        ref readonly SmsgChatBroadcastHeader header =
            ref MemoryMarshal.AsRef<SmsgChatBroadcastHeader>(payload);

        string senderName = DecodeFixedText(header.SenderName);
        ReadOnlySpan<byte> body = payload[SmsgChatBroadcastHeader.HeaderSize..];
        string text = DecodeChatBody(body);

        var key = new ActorKey(header.SenderId, ToEntitySort(header.SenderSort));
        _eventBus.Publish(new ChatBroadcastEvent(
            key, senderName, header.Channel, header.ContextId, text));
        return true;
    }

    // -------------------------------------------------------------------------
    // 3/1 — character-select list
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/1 — character-select list. Decodes the 3-byte header, then one 981-byte per-slot record for each
    /// set bit in the slot mask (LSB-first, up to 8 slots), pulling the name/level/class/HP out of each
    /// record's embedded 880-byte SpawnDescriptor. Switches the FSM to the select screen and emits the
    /// list snapshot. spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/specs/handlers.md §2 / §17.1.
    /// </summary>
    private bool HandleCharacterList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCharacterListHeader.HeaderSize)
        {
            return false;
        }

        ref readonly SmsgCharacterListHeader header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        // A fresh list replaces the prior roster (and any stale chosen-slot cache). spec: login_flow.md §3.2.
        _characterSelection?.Reset();

        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<CharacterListSlot>();
        int cursor = SmsgCharacterListHeader.HeaderSize;

        // Hard, bounded iteration of exactly 5 slots (indices 0..4); the list never references a slot
        // beyond 4. spec: login_flow.md §3.2 / §7 (Char-list maximum slots = 5).
        for (int slot = 0; slot < CharacterSelectionStore.MaxSlots; slot++)
        {
            if ((header.SlotMask & (1 << slot)) == 0)
            {
                continue; // bit clear -> no record for this slot. spec: 3-1.
            }

            // Each set bit consumes one 981-byte record = 880 descriptor + 96 stats + 1 flag + 4 timestamp.
            // spec: 3-1_character_list.yaml (SlotRecordSize). A short/truncated frame ends the loop.
            if (cursor + SmsgCharacterListHeader.SlotRecordSize > payload.Length)
            {
                break;
            }

            ReadOnlySpan<byte> record = payload.Slice(cursor, SmsgCharacterListHeader.SlotRecordSize);
            cursor += SmsgCharacterListHeader.SlotRecordSize;

            // The descriptor is the first 880 bytes of the record. spec: 3-1 / spawn_descriptor.md.
            var reader = new SpawnDescriptorReader(record[..SpawnDescriptorReader.Size]);
            builder.Add(new CharacterListSlot(
                slot, reader.ReadName(), reader.ReadLevel(), reader.ReadServerClass(), reader.ReadCurrentHp()));

            // Retain the RAW per-slot record (880 descriptor + 96 stats + 1 flag byte) so SelectCharacterAsync
            // can detect "@BLANK@", and the 3/14 handler can materialize the local player. spec: login_flow.md §3.5.
            // The 880 + 96 = 976-byte descriptor+stats span; the flag byte is at record +976. spec: §3.2.
            const int descriptorAndStatsSize = SpawnDescriptorReader.Size + 96; // 976
            byte slotFlag = record.Length > descriptorAndStatsSize ? record[descriptorAndStatsSize] : (byte)0;
            _characterSelection?.Retain(
                new CharacterSlotRecord(slot, record[..descriptorAndStatsSize], slotFlag));
        }

        // 3/1 switches the client to the character-select screen. spec: opcodes.md (3/1 "switches to the
        // select screen"). Drive the FSM there only when not already in-world.
        _stateMachine.OnCharacterListReceived();

        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, builder.ToImmutable()));
        return true;
    }

    // -------------------------------------------------------------------------
    // 3/14 — char-spawn result (the actual local-player spawn)
    // spec: opcodes.md (CAMPAIGN-10 ladder de-swap — 3/14 SmsgCharSpawnResponse is the 16-byte spawn confirm)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/14 — enter-game spawn result. On Result != 0 the client materializes the local player from the
    /// slot descriptor cached at select time (Section 3.5) and publishes <see cref="LocalPlayerSpawnedEvent"/>;
    /// on Result == 0 it publishes <see cref="LocalPlayerSpawnFailedEvent"/> (a timed failure message).
    /// The local player is registered as the controlled actor (<see cref="ClientWorld.LocalActorKey"/>),
    /// so the move/skill use cases can source its position. spec: Docs/RE/specs/login_flow.md §3.5 / §5.3.
    /// </summary>
    private void HandleCharSpawnResult(in SmsgCharSpawnResult packet)
    {
        // NOTE (debugger-pending): opcodes.md notes the local-player WORLD spawn is ultimately driven by
        // 4/1, not the 3/14 enter/spawn-confirm bridge, and the 3/14-vs-4/1 ARRIVAL ORDER is the single
        // load-bearing fact static analysis cannot pin (needs a capture). This handler materializes the
        // local player from the cached descriptor on 3/14; reconcile against 4/1 once a capture lands.
        // spec: Docs/RE/opcodes.md (3/14 row); Docs/RE/specs/login_flow.md §3.5 / §5.3.

        // Result 0 = failure (a timed message is shown). spec: login_flow.md §5.3.
        if (packet.Result == 0)
        {
            _eventBus.Publish(new LocalPlayerSpawnFailedEvent(packet.Slot));
            return;
        }

        // Success: materialize the local player from the CACHED slot descriptor (Section 3.5). Without a
        // cache (no store wired, or no slot confirmed) there is nothing to spawn from; record and bail.
        CharacterSlotRecord? cached = _characterSelection?.Chosen;
        if (cached is null)
        {
            _unhandled.Record(Opcodes.SmsgCharSpawnResult, SmsgCharSpawnResult.WireSize);
            return;
        }

        // The local player's actor id is not carried by the 16-byte 3/14 block (only result + slot + 3
        // opaque spawn-param u32s; their meaning is UNVERIFIED — spec §5.3). Key the local player on the
        // PlayerCharacter sort with the unassigned-id sentinel until a self-spawn (5/3) supplies the real
        // id. spec: Docs/RE/structs/actor.md (id initialised to 0xFFFFFFFF before spawn).
        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);

        // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
        Vector3Fixed position = Vector3Fixed.FromFloat(cached.WorldX, 0f, cached.WorldZ);

        var spawnInfo = new SpawnInfo(
            key, cached.Level, cached.CurrentHp, cached.CurrentMp, cached.CurrentStamina, cached.ServerClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(
            key, cached.Level, vitals, cached.CurrentHp, cached.CurrentMp, cached.CurrentStamina, position);
        _world.Add(actor);
        _world.LocalActorKey = key; // mark the controlled actor for the move/skill use cases.

        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            key, packet.Slot, cached.Name, cached.Level, actor.Position, actor.CurrentHp, actor.MaxHp,
            cached.ServerClass));
    }

    // -------------------------------------------------------------------------
    // 3/7 — char manage / delete result (8-byte block)
    // spec: opcodes.md (CAMPAIGN-10 ladder de-swap — 3/7 SmsgCharManageResult is the 8-byte manage result)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/7 — character manage / delete result. Classifies the subtype (subtype 2 = delete-confirm,
    /// which decrements the account char count) and forwards the ReadyTime so the presentation can
    /// format a "wait HH:MM" delete-cooldown message on the blocked path. spec:
    /// Docs/RE/specs/login_flow.md §5.5; Docs/RE/packets/3-4_char_manage_result.yaml (the yaml retains
    /// the stale pre-de-swap "3-4" filename; its content describes the 3/7 manage result).
    /// </summary>
    private void HandleCharManageResult(in SmsgCharManageResult packet)
    {
        const byte success = 1; // result 1 = success path. spec: §5.5.
        const byte deleteConfirmSubtype = 2; // subtype 2 = delete-confirm. spec: §5.5.
        bool ok = packet.Result == success;

        CharManageSubtype subtype = packet.Subtype switch
        {
            0 => CharManageSubtype.GenericRefresh, // spec: §5.5 (semantics UNVERIFIED)
            1 => CharManageSubtype.RenameApplied, // spec: §5.5 (semantics UNVERIFIED)
            2 => CharManageSubtype.DeleteConfirm, // spec: §5.5 (delete-confirm)
            _ => CharManageSubtype.Other,
        };

        // A successful delete-confirm decrements the account char count. spec: §5.5.
        int charCount = _accountCharacters?.CharacterCount ?? 0;
        if (ok && packet.Subtype == deleteConfirmSubtype && _accountCharacters is not null)
        {
            charCount = _accountCharacters.Decrement();
        }

        _eventBus.Publish(new CharManageResultEvent(
            ok, subtype, packet.Subtype, packet.ReadyTime, charCount));
    }

    // -------------------------------------------------------------------------
    // 3/6 — rename-character result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/6 — rename-character result. On success the 18-byte overlay holds the new CP949 character name
    /// (decoded to a managed string at this boundary); on failure its first byte is an error code
    /// (0xC8..0xD4). spec: Docs/RE/specs/login_flow.md §5.6; Docs/RE/packets/SmsgRenameCharResult.
    /// </summary>
    private void HandleRenameCharResult(ReadOnlySpan<byte> payload)
    {
        // Result @0x00: nonzero = success. The 18-byte NameOrError overlay starts at +1. spec: §5.6.
        byte result = payload[0x00];
        bool ok = result != 0;
        ReadOnlySpan<byte> nameOrError = payload.Slice(0x01, SmsgRenameCharResult.WireSize - 1); // 18 bytes

        if (ok)
        {
            // Success: the overlay is the new name as CP949 ASCIIZ (up to 18 bytes incl. NUL). spec: §5.6.
            _eventBus.Publish(new CharRenameResultEvent(true, DecodeFixedText(nameOrError), ErrorCode: 0));
            return;
        }

        // Failure: NameOrError[0] is the error code (0xC8..0xD4, mapped to a UI string). spec: §5.6.
        _eventBus.Publish(new CharRenameResultEvent(false, string.Empty, nameOrError[0]));
    }

    // -------------------------------------------------------------------------
    // 3/23 — character-create result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/23 — character-create result. Pairs with the CreateCharacterRequestedEvent the select use-case
    /// emits for a blank slot. On success the Code byte is the assigned slot id and the account char
    /// count is incremented; on failure Code is an error code (0xC8..0xD4). spec:
    /// Docs/RE/specs/login_flow.md §5.4; Docs/RE/packets/SmsgCharCreateResult.
    /// </summary>
    private void HandleCharCreateResult(in SmsgCharCreateResult packet)
    {
        const byte success = 1; // result 1 = success. spec: §5.4.
        bool ok = packet.Result == success;

        // On success the account char count is incremented. spec: §5.4.
        int charCount = _accountCharacters?.CharacterCount ?? 0;
        if (ok && _accountCharacters is not null)
        {
            charCount = _accountCharacters.Increment();
        }

        // Code is the assigned slot id on success, or the error code on failure. spec: §5.4.
        byte assignedSlotId = ok ? packet.Code : (byte)0;
        byte errorCode = ok ? (byte)0 : packet.Code;

        _eventBus.Publish(new CharCreateResultEvent(
            ok, assignedSlotId, errorCode, packet.Value1, packet.Value2, charCount));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Re-accumulates the local player's derived combat-stat aggregate via the injected
    /// <see cref="CombatStatsRecompute"/> seam, stores it on <see cref="LocalPlayerState"/>, and emits a
    /// <see cref="CombatStatsRecomputedEvent"/>. No-op when the recompute seam or local-player state is
    /// absent. spec: Docs/RE/specs/combat.md §1 / §2.
    /// </summary>
    private void RecomputeCombatStats()
    {
        if (CombatStatsRecompute is null || _localPlayer is null || _world.LocalActorKey is not { } key)
        {
            return;
        }

        CombatStats recomputed = CombatStatsRecompute(_localPlayer.Combat);
        _localPlayer.Combat = recomputed;
        _eventBus.Publish(new CombatStatsRecomputedEvent(key, recomputed));
    }

    /// <summary>
    /// Decodes a NUL-terminated CP949 fixed buffer to a managed string. Routed through
    /// <see cref="Cp949Text.Decode"/>, the single site that registers
    /// <c>CodePagesEncodingProvider.Instance</c> (code page 949 is not built into .NET) and trims at the
    /// first NUL. spec: handlers.md (Korean text fields are CP949-encoded); CLAUDE.md (register the
    /// code-pages provider once).
    /// </summary>
    private static string DecodeFixedText(ReadOnlySpan<byte> buffer) => Cp949Text.Decode(buffer);

    /// <summary>
    /// Decodes the variable chat-body region: a leading length-prefixed <c>[u32 len][text]</c> block when
    /// the prefix is plausible, else the printable run from the start of the body. spec:
    /// Docs/RE/specs/handlers.md §17.12 (body length encoding unconfirmed).
    /// </summary>
    private static string DecodeChatBody(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return string.Empty;
        }

        // Try the length-prefixed form (matching the C2S chat senders): [u32 len incl NUL][text].
        // spec: handlers.md §17.12 / 2-7 / 3-21 framing.
        if (body.Length >= sizeof(uint))
        {
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(body[..sizeof(uint)]);
            if (len >= 1 && len <= (uint)(body.Length - sizeof(uint)))
            {
                // Cp949Text.Decode trims at the first NUL and decodes via the registered code page 949.
                return Cp949Text.Decode(body.Slice(sizeof(uint), (int)len));
            }
        }

        // Fall back to the printable run from the body start.
        return DecodeFixedText(body);
    }

    private static EntitySort ToEntitySort(byte sort) => sort switch
    {
        1 => EntitySort.PlayerCharacter, // spec: actor.md sort == 1
        2 => EntitySort.Monster, // spec: actor.md sort == 2
        3 => EntitySort.NonPlayerCharacter, // spec: actor.md sort == 3
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