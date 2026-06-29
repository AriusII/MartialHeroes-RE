using System.Buffers.Binary;
using System.Collections.Immutable;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private void HandleQuestList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgQuestList.WireSize) return;

        var panelC = payload[0x08];
        var trackingFlag = payload[0x09];
        var panelB = payload[0x0A];

        const int entryCount = 20;
        const int idColumnOffset = 0x20;
        const int nameColumnOffset = 0x70;
        const int nameStride = 17;

        var builder = ImmutableArray.CreateBuilder<QuestLogEntry>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var questId = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.Slice(idColumnOffset + i * sizeof(uint), sizeof(uint)));
            var name = DecodeFixedText(payload.Slice(nameColumnOffset + i * nameStride, nameStride));
            builder.Add(new QuestLogEntry(questId, name));
        }

        _eventBus.Publish(new QuestLogChangedEvent(trackingFlag, panelB, panelC, builder.MoveToImmutable()));
    }

    private void HandleQuestComplete(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgQuestComplete.WireSize) return;

        const uint applyValue = 1;
        var applied = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, sizeof(uint))) == applyValue;
        if (!applied) return;

        const byte grantState = 1;
        var rewardState = payload[0x0C];

        _eventBus.Publish(new QuestCompletedEvent(true, rewardState, rewardState == grantState));
    }
}
