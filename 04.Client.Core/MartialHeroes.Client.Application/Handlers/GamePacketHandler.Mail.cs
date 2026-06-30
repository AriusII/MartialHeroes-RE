using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private bool HandleDeliveryRecord(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgDeliveryRecord.Size) return false;

        ref readonly var packet = ref MemoryMarshal.AsRef<SmsgDeliveryRecord>(payload);
        ReadOnlySpan<byte> senderSpan = packet.SenderName;

        _eventBus.Publish(new DeliveryRecordUpdatedEvent(
            packet.ResultCode,
            packet.SubAction,
            DecodeFixedText(senderSpan),
            packet.Money,
            packet.EntryKey));
        return true;
    }

    private bool HandleLetterReceived(ReadOnlySpan<byte> payload)
    {
        var fixedSize = Unsafe.SizeOf<SmsgSrvLetterReceived>();
        if (payload.Length < fixedSize) return false;

        ref readonly var packet = ref MemoryMarshal.AsRef<SmsgSrvLetterReceived>(payload);
        ReadOnlySpan<byte> senderSpan = packet.Sender;
        ReadOnlySpan<byte> dateSpan = packet.DateString;
        ReadOnlySpan<byte> subjectSpan = packet.SubjectString;

        _eventBus.Publish(new MailLetterArrivedEvent(
            packet.LetterId,
            DecodeFixedText(senderSpan),
            packet.LetterType,
            packet.AttachmentGold,
            packet.AttachmentItemId,
            packet.StatusFlags,
            DecodeFixedText(dateSpan),
            DecodeFixedText(subjectSpan),
            DecodeLengthPrefixedText(payload, fixedSize)));
        return true;
    }

    private static string DecodeLengthPrefixedText(ReadOnlySpan<byte> payload, int offset)
    {
        if (payload.Length < offset + sizeof(uint)) return string.Empty;

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, sizeof(uint)));
        var available = payload.Length - offset - sizeof(uint);
        if (declared == 0u || available <= 0) return string.Empty;

        var take = (int)Math.Min(declared, (uint)available);
        return DecodeFixedText(payload.Slice(offset + sizeof(uint), take));
    }
}
