using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{

    public void Handle(in SmsgEnterGameAck packet)
    {
        _ = packet.BillingFlag;

        var enterRequestPending = inFlightLatch?.IsArmed ?? false;
        sceneStateMachine?.OnEnterGameAck(enterRequestPending);

        accountCharacters?.Set((int)Math.Min(packet.CharacterCount, int.MaxValue));
    }


    public void Handle(in SmsgCharSpawnResult packet)
    {
        if (packet.Result == 0)
        {
            inFlightLatch?.Clear();
            _eventBus.Publish(new LocalPlayerSpawnFailedEvent(packet.Slot));
            return;
        }

        if (enterWorldEmitter is not null)
        {
            _ = enterWorldEmitter(packet.Slot, CancellationToken.None);
            return;
        }

        _unhandled.Record(Opcodes.SmsgCharSpawnResult, SmsgCharSpawnResult.WireSize);
    }


    public void Handle(in SmsgCharManageResult packet)
    {
        inFlightLatch?.Clear();

        const byte success = 1;
        const byte deleteConfirmSubtype = 2;
        var ok = packet.Result == success;

        var subtype = packet.Subtype switch
        {
            0 => CharManageSubtype.GenericRefresh,
            1 => CharManageSubtype.RenameApplied,
            2 => CharManageSubtype.DeleteConfirm,
            _ => CharManageSubtype.Other
        };

        var charCount = accountCharacters?.CharacterCount ?? 0;
        if (ok && packet.Subtype == deleteConfirmSubtype && accountCharacters is not null)
            charCount = accountCharacters.Decrement();

        _eventBus.Publish(new CharManageResultEvent(
            ok, subtype, packet.Subtype, packet.ReadyTime, charCount));
    }


    public void Handle(in SmsgRenameCharResult packet)
    {
        inFlightLatch?.Clear();

        var ok = packet.Result != 0;

        if (ok)
        {
            _eventBus.Publish(new CharRenameResultEvent(true, string.Empty, 0));
            return;
        }

        _eventBus.Publish(new CharRenameResultEvent(false, string.Empty, packet.ErrorCode));
    }


    public void Handle(in SmsgCharStatusBytesByName packet)
    {
        _eventBus.Publish(new CharStatusBytesByNameEvent(
            packet.HasCustomText != 0,
            packet.StatusCode,
            packet.StatusValue,
            packet.Level));
    }

    public void Handle(in SmsgCharActionResult packet)
    {
        _eventBus.Publish(new CharActionResultEvent(packet.Result, packet.Result != 0));

        var result = packet.Result > int.MaxValue ? int.MaxValue : (int)packet.Result;
        sceneStateMachine?.OnCharActionResult(result, _world.LocalActor is not null);
    }


    private bool HandleCharacterList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCharacterListHeader.HeaderSize) return false;

        inFlightLatch?.Clear();

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        characterSelection?.Reset();

        var slots =
            DecodeAndRetainRoster(in header, payload);

        sceneStateMachine?.OnCharacterListReceived();

        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, slots));
        return true;
    }

    private ImmutableArray<CharacterListSlot> DecodeAndRetainRoster(
        in SmsgCharacterListHeader header, ReadOnlySpan<byte> payload)
    {
        var builder = ImmutableArray.CreateBuilder<CharacterListSlot>();
        var cursor = SmsgCharacterListHeader.HeaderSize;

        Span<uint> gearScratch = stackalloc uint[SpawnDescriptorReader.VisibleGearSlots.Length];

        for (var slot = 0; slot < CharacterSelectionStore.MaxSlots; slot++)
        {
            if ((header.SlotMask & (1 << slot)) == 0) continue;

            if (cursor + SmsgCharacterListHeader.SlotRecordSize > payload.Length) break;

            var record = payload.Slice(cursor, SmsgCharacterListHeader.SlotRecordSize);
            cursor += SmsgCharacterListHeader.SlotRecordSize;

            var reader = new SpawnDescriptorReader(record[..SpawnDescriptorReader.Size]);
            reader.ReadVisibleGearGids(gearScratch);
            var equipGids = ImmutableArray.Create(gearScratch);

            const int descriptorAndStatsSize = SpawnDescriptorReader.Size + 96;
            const int flagsWordOffset = descriptorAndStatsSize + 1;
            var slotFlag = record.Length > descriptorAndStatsSize ? record[descriptorAndStatsSize] : (byte)0;
            var billingFlags = record.Length >= flagsWordOffset + sizeof(uint)
                ? BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(flagsWordOffset, sizeof(uint)))
                : 0u;

            builder.Add(new CharacterListSlot(
                slot, reader.ReadName(), reader.ReadLevel(), reader.ReadServerClass(),
                reader.ReadCurrentHpClamped(),
                reader.ReadWorldX(), reader.ReadWorldZ(),
                reader.ReadInternalClass(), reader.ReadAppearanceVariant(), reader.ReadFaceA(), equipGids,
                slotFlag, billingFlags));

            characterSelection?.Retain(
                new CharacterSlotRecord(slot, record[..descriptorAndStatsSize], slotFlag, billingFlags));
        }

        return builder.ToImmutable();
    }


    private bool HandleSceneEntityUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCharacterListHeader.HeaderSize) return false;

        inFlightLatch?.Clear();

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        const byte refillForm = 1;
        if (header.ServerId !=
            refillForm)
            return true;

        characterSelection?.Reset();
        var slots =
            DecodeAndRetainRoster(in header, payload);
        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, slots));
        return true;
    }

    private bool TryCreateLocalPlayerFromCachedDescriptor(
        Vector3Fixed spawnPosition,
        out Actor? actor,
        out int slotIndex,
        out string name,
        out ushort serverClass,
        out ImmutableArray<uint> equipGids)
    {
        actor = null;
        slotIndex = -1;
        name = string.Empty;
        serverClass = 0;
        equipGids = [];

        var cached = characterSelection?.Chosen;
        if (cached is null || cached.RawDescriptor.Length < SpawnDescriptorReader.Size) return false;

        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);
        var reader = new SpawnDescriptorReader(cached.RawDescriptor.Span[..SpawnDescriptorReader.Size]);
        var level = reader.ReadLevel();
        var currentHp = reader.ReadCurrentHpClamped();
        var vitalB = reader.ReadVitalB();
        serverClass = reader.ReadServerClass();

        var spawnInfo = new SpawnInfo(key, level, currentHp, vitalB, vitalB, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        actor = new Actor(key, level, vitals, currentHp, vitalB, vitalB, spawnPosition);
        _world.Add(actor);
        _world.LocalActorKey = key;
        slotIndex = cached.SlotIndex;
        name = cached.Name;
        equipGids = cached.EquipGids;
        return true;
    }
}