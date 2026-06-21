using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 5/7 — chat broadcast
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/7 — server chat broadcast. Decodes the 36-byte header struct, then the variable text body that
    ///     follows it. The body length encoding is unconfirmed; we read a length-prefixed block when one is
    ///     present and otherwise treat the remainder as the text (decoding the leading printable run).
    ///     CP949 -&gt; managed string at this presentation boundary. spec:
    ///     Docs/RE/packets/5-7_chat_broadcast.yaml; Docs/RE/specs/handlers.md §17.12.
    /// </summary>
    private bool HandleChatBroadcast(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgChatBroadcastHeader.HeaderSize) return false;

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgChatBroadcastHeader>(payload);

        var senderName = DecodeFixedText(header.SenderName);
        var body = payload[SmsgChatBroadcastHeader.HeaderSize..];
        var text = DecodeChatBody(body);

        var key = new ActorKey(header.SenderId, ToEntitySort(header.SenderSort));
        _eventBus.Publish(new ChatBroadcastEvent(
            key, senderName, header.Channel, header.ContextId, text));
        return true;
    }
}