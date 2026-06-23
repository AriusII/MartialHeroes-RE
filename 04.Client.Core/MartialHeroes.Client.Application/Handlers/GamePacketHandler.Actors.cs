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
    public void Handle(in SmsgActorMovementUpdate packet)
    {
        var sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        var position = Vector3Fixed.FromFloat(packet.PosX, 0f, packet.PosZ);
        var target = Vector3Fixed.FromFloat(packet.DestX, 0f, packet.DestZ);
        var yaw = Vector3Fixed.FromFloat(packet.Yaw, 0f, 0f).RawX;

        if (!_world.TryGet(key, out var actor))
        {
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

        const byte instantSnapMotion = 5;
        if (packet.MotionCode == instantSnapMotion)
        {
            actor.SnapTo(position);
        }
        else
        {
            actor.SnapTo(position);
            actor.SetMoveTarget(target);
        }

        actor.SetYaw(yaw);
        actor.SetLifecycle(packet.RunFlag != 0 ? LifecycleState.Running : LifecycleState.Walking);

        actor.SetMotionIntent(MotionIntentMap.Resolve(packet.MotionCode, packet.RunFlag));

        _eventBus.Publish(new ActorMovedEvent(
            key, actor.Position, actor.MoveTarget, actor.Yaw, packet.RunFlag != 0));
    }


    public void Handle(in SmsgCharSpawn packet)
    {
        var sortByte = (byte)packet.Sort;
        var sort = ToEntitySort(sortByte);
        var key = new ActorKey(packet.ActorId, sort);

        _world.Remove(key);

        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        var name = reader.ReadName();
        var level = reader.ReadLevel();
        var currentHp = reader.ReadCurrentHpClamped();
        var vitalB = reader.ReadVitalB();
        var serverClass = reader.ReadServerClass();

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


    public void Handle(in SmsgCharDespawn packet)
    {
        var sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        _world.Remove(key);

        const byte leaveEffectBit = 0x01;
        var playLeaveEffect = (packet.Flags & leaveEffectBit) != 0;
        _eventBus.Publish(new ActorDespawnedEvent(key, playLeaveEffect));
    }


    public void Handle(in SmsgActorVitalsAndPairState packet)
    {
        var rawSort = packet.Sort == 8 ? (byte)1 : packet.Sort;
        var key = new ActorKey(packet.ActorId, ToEntitySort(rawSort));
        if (!_world.TryGet(key, out var actor))
        {
            _unhandled.Record(Opcodes.SmsgActorVitalsAndPairState, SmsgActorVitalsAndPairState.WireSize);
            return;
        }

        actor.SetCurrentHp(packet.CurrentHp);
        actor.SetCurrentMp(packet.VitalC);
        actor.SetCurrentStamina(packet.Stamina);

        _eventBus.Publish(new ActorVitalsChangedEvent(
            key, actor.CurrentHp, actor.CurrentMp, actor.CurrentStamina));

        if (hudEventHub is not null && _world.LocalActorKey == key)
            hudEventHub.PublishVitals(new HudVitalsEvent(
                actor.CurrentHp, actor.MaxHp,
                actor.CurrentMp, actor.MaxMp,
                actor.CurrentStamina, actor.MaxStamina));
    }


    public void Handle(in SmsgActorSpawnExtended packet)
    {
        var sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        _world.Remove(key);

        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        var name = reader.ReadName();
        var level = reader.ReadLevel();

        var currentHp = packet.IsPlayerBranch
            ? ClampHpQword(packet.PlayerHpQword)
            : reader.ReadCurrentHpClamped();
        var vitalB = reader.ReadVitalB();
        var serverClass = reader.ReadServerClass();

        var position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, vitalB, vitalB, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(key, level, vitals, currentHp, vitalB, vitalB, position);
        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    private static uint ClampHpQword(long hp)
    {
        if (hp <= 0) return 0u;
        return hp >= uint.MaxValue ? uint.MaxValue : (uint)hp;
    }
}