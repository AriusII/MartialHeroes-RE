using System.Buffers.Binary;
using System.Collections.Immutable;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Core.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private void HandleItemPanelSlotChunk(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgItemPanelSlotChunk.HeaderSize) return;

        const byte guardOk = 1;
        if (payload[0x00] != guardOk) return;

        var ownerKey = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, sizeof(uint)));
        if (_world.LocalActorKey is { } localKey && localKey.RawId != ownerKey) return;

        const byte equipChunk = 0;
        var table = payload[0x08] == equipChunk ? InventoryTable.Equip : InventoryTable.Bag;
        var startIndex = payload[0x09];
        var count = payload[0x0A];

        var slots = DecodeItemSlotRun(payload, SmsgItemPanelSlotChunk.HeaderSize, count);
        _eventBus.Publish(new InventorySlotsChangedEvent(table, startIndex, false, slots));
    }

    private void HandleItemPanelSlotRefresh(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgItemPanelSlotRefresh.HeaderSize) return;

        const byte clearAllSentinel = 0xFF;
        var startSlot = payload[0x09];
        var count = payload[0x0A];

        if (startSlot == clearAllSentinel)
        {
            _eventBus.Publish(new InventorySlotsChangedEvent(
                InventoryTable.Bag, 0, true, ImmutableArray<InventorySlotRecord>.Empty));
            return;
        }

        var slots = DecodeItemSlotRun(payload, SmsgItemPanelSlotRefresh.HeaderSize, count);
        _eventBus.Publish(new InventorySlotsChangedEvent(InventoryTable.Bag, startSlot, false, slots));
    }

    private static ImmutableArray<InventorySlotRecord> DecodeItemSlotRun(
        ReadOnlySpan<byte> payload, int recordsOffset, int count)
    {
        const int stride = ItemSlotRecord.WireSize;
        var available = payload.Length - recordsOffset;
        var maxRecords = available > 0 ? available / stride : 0;
        if (count > maxRecords) count = maxRecords;
        if (count <= 0) return ImmutableArray<InventorySlotRecord>.Empty;

        var builder = ImmutableArray.CreateBuilder<InventorySlotRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var rec = payload.Slice(recordsOffset + i * stride, stride);
            builder.Add(new InventorySlotRecord(
                BinaryPrimitives.ReadUInt32LittleEndian(rec[..sizeof(uint)]),
                BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x04, sizeof(uint))),
                BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x08, sizeof(uint))),
                BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x0C, sizeof(uint)))));
        }

        return builder.MoveToImmutable();
    }

    public void Handle(in SmsgEquipItemResult packet)
    {
        const byte ok = 1;
        const byte titleSlot = 15;
        var success = packet.Result == ok;

        if (success)
            RecomputeCombatStats();

        _eventBus.Publish(new EquipResultEvent(
            success, packet.FromSlot, packet.ToSlot, packet.ToSlot == titleSlot));
    }


    public void Handle(in SmsgItemSlotStateAck packet)
    {
        const byte ok = 1;
        var success = packet.Result == ok;

        if (success)
            RecomputeCombatStats();

        _eventBus.Publish(new ItemSlotStateEvent(
            success, packet.FromSlot, packet.ToSlot, packet.BonusField1, packet.BonusField2, packet.BonusField3));
    }


    public void Handle(in SmsgNpcBuyOrAcquireAck packet)
    {
        const byte ok = 1;
        var success = packet.Result == ok;

        _eventBus.Publish(new NpcAcquireResultEvent(
            success, packet.ReasonCode, packet.BagSlotIndex, packet.ItemQuadB, packet.GoldLo));
    }
}