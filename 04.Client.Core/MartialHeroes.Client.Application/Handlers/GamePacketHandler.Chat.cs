using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 5/7 — chat broadcast
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/7 — server chat broadcast. Decodes the 36-byte header struct, then the body, which (CYCLE 11
    ///     RESOLVED) is exactly one length-prefixed segment <c>[u32 BodyLength][BodyLength CP949 bytes]</c>
    ///     (the client NUL-appends; the NUL is NOT counted). Past that, the channel byte (@+0x0E) selects
    ///     BOTH the routing (codes &lt; 100 → chat-log ring; codes &gt; 100 → floating notice list) AND the
    ///     ARGB colour ladder. CP949 -&gt; managed string at this presentation boundary.
    ///     RD residual: the u32 BodyLength endianness is debugger-pending; implemented as LE (platform
    ///     default). spec: Docs/RE/packets/5-7_chat_broadcast.yaml (BODY framing CORRECTED, CYCLE 11);
    ///     Docs/RE/specs/chat.md §3 (channel → colour) / §6.3 (channel &lt; 100 vs &gt; 100 routing) / §8.2.
    /// </summary>
    private bool HandleChatBroadcast(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgChatBroadcastHeader.HeaderSize) return false;

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgChatBroadcastHeader>(payload);

        var senderName = DecodeFixedText(header.SenderName);

        // CYCLE 11 RESOLVED body framing: [u32 BodyLength][BodyLength CP949 bytes] at payload +0x24.
        // The length word is read LITTLE-ENDIAN (RD residual: endianness debugger-pending — implement LE).
        // spec: Docs/RE/packets/5-7_chat_broadcast.yaml (BodyLengthOffset = 36); Docs/RE/specs/chat.md §8.2.
        var text = DecodeLengthPrefixedBody(payload);

        var key = new ActorKey(header.SenderId, ToEntitySort(header.SenderSort));

        // Channel byte (@+0x0E) selects the per-channel ARGB colour from the layer-02 ladder consts.
        // spec: Docs/RE/specs/chat.md §3 (channel → colour table); §8.2 (S2C notice codes 8/10/16/17).
        var colourArgb = ResolveChatColour(header.Channel);

        // The same channel byte selects routing: codes < 100 land in the chat-log ring; codes > 100 route
        // to the floating system/notice list (a separate text system). spec: Docs/RE/specs/chat.md §6.3.
        // The HUD chat sink carries the (text, channel, colour) triple; the notice list is a distinct sink.
        _hudEventHub?.PublishChatLine(new ChatLineEvent(header.Channel, text, colourArgb, senderName));

        _eventBus.Publish(new ChatBroadcastEvent(
            key, senderName, header.Channel, header.ContextId, text));
        return true;
    }

    /// <summary>
    ///     Decodes the 5/7 body: one length-prefixed segment <c>[u32 BodyLength][BodyLength CP949 bytes]</c>
    ///     immediately after the 36-byte header (CYCLE 11 RESOLVED). Reads the length LE (RD residual:
    ///     endianness debugger-pending). The decoded run is CP949 (the client NUL-terminates, NUL not
    ///     counted). spec: Docs/RE/packets/5-7_chat_broadcast.yaml (BodyLengthOffset; CYCLE 11);
    ///     Docs/RE/specs/chat.md §8.2.
    /// </summary>
    private static string DecodeLengthPrefixedBody(ReadOnlySpan<byte> payload)
    {
        var lengthOffset = SmsgChatBroadcastHeader.BodyLengthOffset; // = 36 (0x24). spec: 5-7 yaml.
        if (payload.Length < lengthOffset + sizeof(uint)) return string.Empty;

        // u32 BodyLength, LE (RD residual: endianness debugger-pending). spec: chat.md §8.2 / 5-7 yaml.
        var bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.Slice(lengthOffset, sizeof(uint)));

        var bodyStart = lengthOffset + sizeof(uint);
        var available = payload.Length - bodyStart;
        if (available <= 0) return string.Empty;

        // Clamp to the bytes actually present (a short/over-stated length never reads past the frame).
        var take = bodyLength <= (uint)available ? (int)bodyLength : available;
        return Cp949Text.Decode(payload.Slice(bodyStart, take));
    }

    /// <summary>
    ///     Maps the 5/7 channel byte (@+0x0E) to its ARGB log/notice colour using the layer-02 ladder
    ///     constants (the wire layout owner). Unknown codes fall back to white (the say colour). spec:
    ///     Docs/RE/specs/chat.md §3 (codes 0/1/2/3/6/7/9/15) / §8.2 (S2C-only notice codes 10 / 16 / 17).
    /// </summary>
    private static uint ResolveChatColour(byte channel)
    {
        return channel switch
        {
            0 => SmsgChatBroadcastHeader.ColourSay, // white. spec: chat.md §3 code 0
            1 => SmsgChatBroadcastHeader.ColourWhisper, // lavender. spec: chat.md §3 code 1
            2 => SmsgChatBroadcastHeader.ColourParty, // cyan. spec: chat.md §3 code 2
            3 => SmsgChatBroadcastHeader.ColourGuild, // green. spec: chat.md §3 code 3
            6 => SmsgChatBroadcastHeader.ColourMisia, // yellow. spec: chat.md §3 code 6
            7 => SmsgChatBroadcastHeader.ColourSpecialMisia, // pink 0xFFFF797C. spec: chat.md §3 code 7
            9 => SmsgChatBroadcastHeader.ColourGmSystem, // pink (GM/system). spec: chat.md §3 code 9
            10 => SmsgChatBroadcastHeader.ColourNoticeYellow, // yellow 49079 notice. spec: chat.md §8.2 code 10
            15 => SmsgChatBroadcastHeader.ColourAlliance, // blue. spec: chat.md §3 code 15
            16 or 17 => SmsgChatBroadcastHeader.ColourNoticeRed, // red/orange notice. spec: chat.md §8.2 codes 16/17
            _ => SmsgChatBroadcastHeader.ColourSay // unknown → white (say). spec: chat.md §3.
        };
    }
}