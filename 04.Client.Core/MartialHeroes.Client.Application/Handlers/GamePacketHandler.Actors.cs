using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 5/13 — actor movement update
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/13 — actor movement update. Converts the wire float XZ coords to <see cref="Vector3Fixed" />
    ///     at this boundary, updates the actor's position/target/yaw, and emits <see cref="ActorMovedEvent" />.
    ///     spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/structs/actor.md (coords float).
    /// </summary>
    public void Handle(in SmsgActorMovementUpdate packet)
    {
        var sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // Float -> fixed conversion happens HERE, at the network/application boundary. World Y = 0.
        // spec: Docs/RE/structs/actor.md ("the server never sends Y and the client forces it to 0").
        var position = Vector3Fixed.FromFloat(packet.PosX, 0f, packet.PosZ);
        var target = Vector3Fixed.FromFloat(packet.DestX, 0f, packet.DestZ);
        var yaw = Vector3Fixed.FromFloat(packet.Yaw, 0f, 0f).RawX; // reuse Q16.16 conversion for the yaw scalar

        if (!_world.TryGet(key, out var actor))
        {
            // Movement for an actor we have not seen spawn: register a minimal placeholder so the
            // world stays consistent. Vitals are zero; a later spawn/vitals push fills them in.
            actor = new Actor(
                key,
                0,
                VitalStats.Zero,
                0,
                0,
                0,
                position,
                0,
                yaw);
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

        // Derive the pure motion intent from the wire MotionCode (@+0x24) and RunFlag (@+0x1C) and
        // attach it WITHOUT changing the snap/move-target behaviour above (the existing SnapTo /
        // SetMoveTarget calls are unchanged; this only adds the animation-classification intent).
        // spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/specs/skinning.md §10.
        actor.SetMotionIntent(MotionIntentMap.Resolve(packet.MotionCode, packet.RunFlag));

        _eventBus.Publish(new ActorMovedEvent(
            key, actor.Position, actor.MoveTarget, actor.Yaw, packet.RunFlag != 0));
    }

    // -------------------------------------------------------------------------
    // 5/3 — actor spawn
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/3 — actor spawn (908-byte frame; 8-byte prefix + 880-byte SpawnDescriptor). Per the CYCLE 11
    ///     spec the spawn REBUILDS the actor from scratch: remove any existing actor with the same
    ///     <c>(Sort, ActorId)</c> key first, then decode the descriptor (name, level, vitals, world XZ,
    ///     server class), build a fresh Domain <see cref="Actor" />, seed its world position (Y forced 0),
    ///     and re-register it. The Sort low byte distinguishes 1=player / 2=mob-NPC / 3=ground-item. The
    ///     world floats sit at descriptor +0x4C/+0x50 (= 5/3 WIRE +0x54/+0x58 under the 8-byte prefix);
    ///     the <see cref="SpawnDescriptorReader" /> reads them SD-relative, so the per-opcode prefix is
    ///     handled by giving it the descriptor span. Emits <see cref="ActorSpawnedEvent" />. spec:
    ///     Docs/RE/packets/5-3_char_spawn.yaml; Docs/RE/structs/spawn_descriptor.md (880B; world floats
    ///     +0x4C/+0x50; HP qword +0x3C); Docs/RE/specs/world_systems.md §13.1.
    /// </summary>
    public void Handle(in SmsgCharSpawn packet)
    {
        // Sort low byte: 1=player, 2=mob/NPC, 3=ground-item — distinct handling each. spec: 5-3 yaml
        // (Sort low byte = real selector); spawn_descriptor.md (CYCLE 11 prefix note).
        var sortByte = (byte)packet.Sort;
        var sort = ToEntitySort(sortByte);
        var key = new ActorKey(packet.ActorId, sort);

        // REBUILD: tear down any existing actor under this (Sort, ActorId) key before re-inserting, so a
        // re-spawn never accretes onto stale state. spec: Docs/RE/specs/world_systems.md §13.1
        // (spawn rebuilds the actor); structs/spawn_descriptor.md (fixed-size descriptor copy on spawn).
        _world.Remove(key);

        // The SpawnDescriptor is an [InlineArray(880)] inside the packet; the language projects it to
        // a ReadOnlySpan<byte> with no copy. spec: packets/5-3_char_spawn.yaml (opaque 880-byte blob).
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        var name = reader.ReadName();
        var level = reader.ReadLevel();
        // HP-qword correction: HP is ONE int64 @ desc +0x3C (clamped to u32); the single MP/stamina-class
        // vital is @ +0x44. +0x40 is HP-HIGH, NOT MP. Both lower vital slots take the one vital_b
        // (MP-vs-stamina pending). spec: Docs/RE/structs/spawn_descriptor.md (HP-qword correction).
        var currentHp = reader.ReadCurrentHpClamped();
        var vitalB = reader.ReadVitalB();
        var serverClass = reader.ReadServerClass();

        // Float -> fixed at the boundary; world Y forced to 0 on spawn. spec: spawn_descriptor.md
        // (world_x +0x4C / world_z +0x50 confirmed floats; "World Y is forced to 0.0 on spawn").
        var position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, vitalB, vitalB, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(
            key,
            level,
            vitals,
            currentHp,
            vitalB,
            vitalB,
            position);

        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    // -------------------------------------------------------------------------
    // 5/0 — actor despawn
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/0 — actor despawn. Removes the actor from the registry and emits
    ///     <see cref="ActorDespawnedEvent" />. spec: Docs/RE/packets/5-0_char_despawn.yaml.
    /// </summary>
    public void Handle(in SmsgCharDespawn packet)
    {
        var sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        _world.Remove(key);

        const byte leaveEffectBit = 0x01; // bit0 => play "left" SFX + chat line. spec: 5-0 Flags.
        var playLeaveEffect = (packet.Flags & leaveEffectBit) != 0;
        _eventBus.Publish(new ActorDespawnedEvent(key, playLeaveEffect));
    }

    // -------------------------------------------------------------------------
    // 5/53 — actor vitals and pair state
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/53 — current vitals push. Updates the actor's HP/MP/stamina (capped against its computed
    ///     maxima by Domain) and emits <see cref="ActorVitalsChangedEvent" />. The sort value 8 normalises
    ///     to 1. spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    public void Handle(in SmsgActorVitalsAndPairState packet)
    {
        var rawSort = packet.Sort == 8 ? (byte)1 : packet.Sort; // spec: 5-53 (sort 8 -> 1)
        var key = new ActorKey(packet.ActorId, ToEntitySort(rawSort));
        if (!_world.TryGet(key, out var actor))
        {
            // No known actor: nothing to update. A later spawn fills state in.
            _unhandled.Record(Opcodes.SmsgActorVitalsAndPairState, SmsgActorVitalsAndPairState.WireSize);
            return;
        }

        // CurrentHp HIGH CONFIDENCE; VitalC is the third vital mirrored to the local-player MP global;
        // Stamina HIGH CONFIDENCE. spec: 5-53 (vital ordering otherwise unconfirmed). Domain clamps the
        // current HP (and floors it at 0) against the client-computed MaxHp.
        // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (CurrentHp@0x10 = canonical HP-bar source).
        actor.SetCurrentHp(packet.CurrentHp);
        actor.SetCurrentMp(packet.VitalC);
        actor.SetCurrentStamina(packet.Stamina);

        _eventBus.Publish(new ActorVitalsChangedEvent(
            key, actor.CurrentHp, actor.CurrentMp, actor.CurrentStamina));

        // Local-player vitals also feed the World-Scene HUD right-edge gauge: the handler mirrors the
        // updated current vitals into the local-player vital state, each CLAMPED against the
        // client-computed maximum (the server sends only current values, never the maxima). The Domain
        // Actor already applied that clamp via SetCurrent*, so we publish the post-clamp current + the
        // Actor's client-computed maxima as an immutable HUD snapshot (latest-wins). Non-local actors
        // do not feed the local gauge. spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml
        // (LOCAL-PLAYER CLAMP: current clamped to client-computed max, floored at 0);
        // Docs/RE/specs/combat.md §12.2 (5/53 = canonical HP-bar source).
        if (_hudEventHub is not null && _world.LocalActorKey == key)
            _hudEventHub.PublishVitals(new HudVitalsEvent(
                actor.CurrentHp, actor.MaxHp,
                actor.CurrentMp, actor.MaxMp,
                actor.CurrentStamina, actor.MaxStamina));
    }

    // -------------------------------------------------------------------------
    // 5/1 — extended actor spawn
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/1 — extended actor spawn (912-byte frame; 12-byte prefix + 880-byte SpawnDescriptor). Like 5/3
    ///     this REBUILDS the actor: remove the existing <c>(Sort, ActorId)</c> entry first, decode the
    ///     descriptor, seed the world position (Y forced 0), and re-register. The Sort byte (@wire +0x00,
    ///     low byte only) is 1=player / 2=mob-NPC / 3=ground-item.
    ///     <para>
    ///         PLAYER BRANCH (Sort == 1): per the CYCLE 11 spec the player HP is read as the ONE 64-bit HP
    ///         qword via the layer-02 <see cref="SmsgActorSpawnExtended.PlayerHpQword" /> accessor
    ///         (descriptor +0x3C/+0x40 = wire +0x48/+0x4C) — NOT two independent vitals. Mob/NPC and
    ///         ground-item branches fall back to the SD reader's clamped HP (the bytes differ off the
    ///         player branch). The 12-byte prefix shifts the world floats to wire +0x58/+0x5C; the SD
    ///         reader reads them SD-relative (+0x4C/+0x50) off the descriptor span, so the prefix is
    ///         handled by the struct layout, not hardcoded here.
    ///     </para>
    ///     Emits <see cref="ActorSpawnedEvent" />. spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml
    ///     (Sort low byte; PlayerHpQword on Sort==1; world floats wire +0x58/+0x5C);
    ///     Docs/RE/structs/spawn_descriptor.md (HP qword +0x3C; per-opcode prefix);
    ///     Docs/RE/specs/world_systems.md §13.1.
    /// </summary>
    public void Handle(in SmsgActorSpawnExtended packet)
    {
        // Sort low byte: 1=player, 2=mob/NPC, 3=ground-item — distinct handling each. spec: 5-1 yaml.
        var sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // REBUILD: tear down any existing actor under this (Sort, ActorId) key before re-inserting.
        // spec: Docs/RE/specs/world_systems.md §13.1 (spawn rebuilds the actor).
        _world.Remove(key);

        // The 880-byte descriptor is an [InlineArray] projected to a span with no copy. spec: 5-1.
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        var name = reader.ReadName(); // SD +0x00. spec: spawn_descriptor.md
        var level = reader.ReadLevel(); // SD +0x3A. spec: spawn_descriptor.md

        // HP: on the PLAYER branch (Sort==1) read the ONE i64 HP qword via the layer-02 accessor
        // (descriptor +0x3C/+0x40 = wire +0x48/+0x4C), NOT two vitals. Off the player branch the bytes
        // mean something else, so use the SD reader's clamped HP there. spec:
        // Docs/RE/packets/5-1_actor_spawn_extended.yaml (PlayerHpQword on Sort==1; HP-qword correction).
        var currentHp = packet.IsPlayerBranch
            ? ClampHpQword(packet.PlayerHpQword) // wire +0x48/+0x4C (one i64). spec: 5-1 yaml (Sort==1).
            : reader.ReadCurrentHpClamped(); // SD +0x3C clamped (mob/NPC/ground). spec: spawn_descriptor.md
        var vitalB = reader.ReadVitalB(); // SD +0x44 vital_b. spec: spawn_descriptor.md
        var serverClass = reader.ReadServerClass(); // SD +0x74. spec: spawn_descriptor.md

        // Float -> fixed at the boundary; world Y forced 0 on spawn. spec: spawn_descriptor.md (+0x4C/+0x50).
        var position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, vitalB, vitalB, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(key, level, vitals, currentHp, vitalB, vitalB, position);
        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    /// <summary>
    ///     Clamps a 64-bit HP qword into the non-negative <see cref="uint" /> the Domain Actor stores. Live
    ///     HP fits a u32; a negative or &gt;u32 qword is clamped to the u32 range. Mirrors
    ///     <see cref="SpawnDescriptorReader.ReadCurrentHpClamped" /> so the player-branch i64 HP and the
    ///     SD-reader HP clamp identically. spec: Docs/RE/structs/spawn_descriptor.md (HP-qword correction).
    /// </summary>
    private static uint ClampHpQword(long hp)
    {
        if (hp <= 0) return 0u;
        return hp >= uint.MaxValue ? uint.MaxValue : (uint)hp;
    }
}