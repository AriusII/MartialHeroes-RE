using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public void Handle(in SmsgSkillWindowStateUpdate packet)
    {
        if (hudEventHub is null)
            return;

        var slots = ImmutableArray.CreateBuilder<BuffSlot>(
            SmsgSkillWindowStateUpdate.BuffRecordCount);

        AddBuffSlot(slots, packet.Buff00Id);
        AddBuffSlot(slots, packet.Buff01Id);
        AddBuffSlot(slots, packet.Buff02Id);
        AddBuffSlot(slots, packet.Buff03Id);
        AddBuffSlot(slots, packet.Buff04Id);
        AddBuffSlot(slots, packet.Buff05Id);
        AddBuffSlot(slots, packet.Buff06Id);
        AddBuffSlot(slots, packet.Buff07Id);
        AddBuffSlot(slots, packet.Buff08Id);
        AddBuffSlot(slots, packet.Buff09Id);
        AddBuffSlot(slots, packet.Buff10Id);
        AddBuffSlot(slots, packet.Buff11Id);
        AddBuffSlot(slots, packet.Buff12Id);
        AddBuffSlot(slots, packet.Buff13Id);
        AddBuffSlot(slots, packet.Buff14Id);
        AddBuffSlot(slots, packet.Buff15Id);
        AddBuffSlot(slots, packet.Buff16Id);
        AddBuffSlot(slots, packet.Buff17Id);
        AddBuffSlot(slots, packet.Buff18Id);
        AddBuffSlot(slots, packet.Buff19Id);
        AddBuffSlot(slots, packet.Buff20Id);
        AddBuffSlot(slots, packet.Buff21Id);
        AddBuffSlot(slots, packet.Buff22Id);
        AddBuffSlot(slots, packet.Buff23Id);
        AddBuffSlot(slots, packet.Buff24Id);
        AddBuffSlot(slots, packet.Buff25Id);
        AddBuffSlot(slots, packet.Buff26Id);
        AddBuffSlot(slots, packet.Buff27Id);
        AddBuffSlot(slots, packet.Buff28Id);
        AddBuffSlot(slots, packet.Buff29Id);

        hudEventHub.PublishBuffState(BuffStateEvent.FromSlots(slots.MoveToImmutable()));
    }


    public void Handle(in SmsgSkillHotbarSlotSet packet)
    {
        if (packet.HotbarSlot >= SmsgSkillHotbarSlotSet.HotbarSlotCount)
        {
            _unhandled.Record();
            return;
        }

        var skill = new SkillId(unchecked((uint)packet.SkillId));
        if (localPlayer is not null)
        {
            var cooldownMs =
                CooldownDurationResolver?.Invoke(skill) ?? 0;
            localPlayer.SetHotbarSlot(packet.HotbarSlot, skill, packet.SkillPoints, cooldownMs);
        }

        _eventBus.Publish(new SkillHotbarSlotSetEvent(packet.HotbarSlot, skill, packet.SkillPoints));
    }


    public void Handle(in SmsgSkillHotbarAssignResult packet)
    {
        const byte ok = 1;
        var success = packet.Gate == ok;

        _eventBus.Publish(new SkillHotbarAssignResultEvent(
            success, packet.ResultCode, packet.HotbarSlotEcho,
            new SkillId(unchecked((uint)packet.SkillIdEcho)), packet.SkillPointPool));
    }

    private bool HandleActorSkillAction(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgActorSkillAction.HeaderSize) return false;

        ref readonly var header = ref MemoryMarshal.AsRef<SmsgActorSkillAction>(payload);
        var skillId = header.SkillId;

        var attackerKey = new ActorKey(header.CasterId, ToEntitySort(header.CasterSort));
        _eventBus.Publish(new ActorSkillActionEvent(attackerKey, skillId, header.ActionCode));

        var casterIsLocal = localPlayer is not null && _world.LocalActorKey == attackerKey;

        if (header.CastFlag != 0 && casterIsLocal)
            ConfirmLocalCast(new SkillId(skillId), header.TargetCount);

        var records = payload[SmsgActorSkillAction.HeaderSize..];

        var accumulator = new SkillDamageAccumulator();

        for (var t = 0; t < header.TargetCount; t++)
        {
            var recordStart = t * SmsgActorSkillAction.TargetRecordStride;
            if (recordStart + SmsgActorSkillAction.TargetRecordStride > records.Length) break;

            var recordBytes = records.Slice(recordStart, SmsgActorSkillAction.TargetRecordStride);
            ref readonly var record =
                ref MemoryMarshal.AsRef<SmsgActorSkillAction.TargetRecord>(recordBytes);

            var hit = record.HitMagnitude;
            accumulator.Add(hit);

            var key = new ActorKey(record.TargetKey, ToEntitySort(record.TargetSubKey));

            hudEventHub?.PublishCombatText(new CombatTextEvent(
                key,
                0,
                CombatTextEvent.MinKind,
                false,
                skillId,
                hit,
                0L));
        }

        if (casterIsLocal && accumulator.HasDisplayDamage)
            hudEventHub?.PublishCombatText(new CombatTextEvent(
                attackerKey,
                accumulator.RecordCount,
                CombatTextEvent.MinKind,
                false,
                skillId,
                accumulator.DisplayTotal,
                0L));

        return true;
    }

    private void ConfirmLocalCast(SkillId skill, int targetCount)
    {
        if (localPlayer is null) return;

        if (SkillDefinitionResolver?.Invoke(skill) is not { } definition) return;

        localPlayer.ConsumeCastCost(definition.HpCost, definition.StaminaCost, targetCount, 0);

        if (!definition.IsCooldownArmExempt)
            localPlayer.Cooldowns.Arm(skill, localPlayer.ClockMs);
    }

    private static void AddBuffSlot(
        ImmutableArray<BuffSlot>.Builder slots, uint buffId)
    {
        if (buffId == 0u)
        {
            slots.Add(new BuffSlot(BuffSlot.EmptyBuffId, null));
            return;
        }

        slots.Add(new BuffSlot(unchecked((ushort)buffId), null));
    }


    private bool HandleBuffSlotUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 56;
        if (payload.Length < minSize) return false;

        var sort = payload[0x00];
        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        var slot = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        var effectCode = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        var duration = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x10, 4));
        var extra = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x14, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        var isLocal = _world.LocalActorKey == key;
        if (isLocal && localPlayer is not null && slot < BuffTable.SlotCount)
        {
            localPlayer.Buffs.Apply(
                (int)slot, unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra), 0);
            RecomputeCombatStats();
        }

        _eventBus.Publish(new BuffSlotChangedEvent(
            key, unchecked((int)slot), unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra)));
        return true;
    }
}