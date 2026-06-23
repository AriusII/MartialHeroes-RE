using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private bool HandleChatBroadcast(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgChatBroadcastHeader.HeaderSize) return false;

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgChatBroadcastHeader>(payload);

        var senderName = DecodeFixedText(header.SenderName);

        var text = DecodeLengthPrefixedBody(payload);

        var key = new ActorKey(header.SenderId, ToEntitySort(header.SenderSort));

        var colourArgb = ResolveChatColour(header.Channel);

        hudEventHub?.PublishChatLine(new ChatLineEvent(header.Channel, text, colourArgb, senderName));

        _eventBus.Publish(new ChatBroadcastEvent(
            key, senderName, header.Channel, header.ContextId, text));
        return true;
    }

    private static string DecodeLengthPrefixedBody(ReadOnlySpan<byte> payload)
    {
        var lengthOffset = SmsgChatBroadcastHeader.BodyLengthOffset;
        if (payload.Length < lengthOffset + sizeof(uint)) return string.Empty;

        var bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.Slice(lengthOffset, sizeof(uint)));

        var bodyStart = lengthOffset + sizeof(uint);
        var available = payload.Length - bodyStart;
        if (available <= 0) return string.Empty;

        var take = bodyLength <= (uint)available ? (int)bodyLength : available;
        return Cp949Text.Decode(payload.Slice(bodyStart, take));
    }

    private static uint ResolveChatColour(byte channel)
    {
        return channel switch
        {
            0 => SmsgChatBroadcastHeader.ColourSay,
            1 => SmsgChatBroadcastHeader.ColourWhisper,
            2 => SmsgChatBroadcastHeader.ColourParty,
            3 => SmsgChatBroadcastHeader.ColourGuild,
            6 => SmsgChatBroadcastHeader.ColourMisia,
            7 => SmsgChatBroadcastHeader.ColourSpecialMisia,
            9 => SmsgChatBroadcastHeader.ColourGmSystem,
            10 => SmsgChatBroadcastHeader.ColourNoticeYellow,
            15 => SmsgChatBroadcastHeader.ColourAlliance,
            16 or 17 => SmsgChatBroadcastHeader.ColourNoticeRed,
            _ => SmsgChatBroadcastHeader.ColourSay
        };
    }
}