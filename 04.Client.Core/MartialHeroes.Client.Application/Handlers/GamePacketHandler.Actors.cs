using MartialHeroes.Client.Application.Contracts.Events;
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
    ///     5/3 — actor spawn. Decodes the embedded 880-byte SpawnDescriptor (name, level, vitals, world
    ///     XZ, server class), converts the float coords to <see cref="Vector3Fixed" /> at this boundary,
    ///     creates and registers the Domain <see cref="Actor" />, and emits <see cref="ActorSpawnedEvent" />.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml; Docs/RE/structs/actor.md (SpawnDescriptor).
    /// </summary>
    public void Handle(in SmsgCharSpawn packet)
    {
        var sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // The SpawnDescriptor is an [InlineArray(880)] inside the packet; the language projects it to
        // a ReadOnlySpan<byte> with no copy. spec: packets/5-3_char_spawn.yaml (opaque 880-byte blob).
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        var name = reader.ReadName();
        var level = reader.ReadLevel();
        var currentHp = reader.ReadCurrentHp();
        var currentMp = reader.ReadCurrentMp();
        var currentStamina = reader.ReadCurrentStamina();
        var serverClass = reader.ReadServerClass();

        // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
        var position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        var vitals = VitalsResolver(spawnInfo);

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
    ///     5/1 — extended actor spawn. Decodes the embedded 880-byte SpawnDescriptor (name, level, vitals,
    ///     world XZ, class) via <see cref="SpawnDescriptorReader" />, converts coords to fixed-point at this
    ///     boundary, registers the Domain actor, and emits <see cref="ActorSpawnedEvent" />. spec:
    ///     Docs/RE/packets/5-1_actor_spawn_extended.yaml; Docs/RE/structs/spawn_descriptor.md.
    /// </summary>
    public void Handle(in SmsgActorSpawnExtended packet)
    {
        var sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // The 880-byte descriptor is an [InlineArray] projected to a span with no copy. spec: 5-1.
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        var name = reader.ReadName(); // SD +0x00. spec: spawn_descriptor.md
        var level = reader.ReadLevel(); // SD +0x3A. spec: spawn_descriptor.md
        var currentHp = reader.ReadCurrentHp(); // SD +0x3C. spec: spawn_descriptor.md
        var currentMp = reader.ReadCurrentMp(); // SD +0x40. spec: spawn_descriptor.md
        var currentStamina = reader.ReadCurrentStamina(); // SD +0x44. spec: spawn_descriptor.md
        var serverClass = reader.ReadServerClass(); // SD +0x74. spec: spawn_descriptor.md

        // Float -> fixed at the boundary; world Y forced 0. spec: spawn_descriptor.md (+0x4C/+0x50).
        var position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, position);
        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }
}