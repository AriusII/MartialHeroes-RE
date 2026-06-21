// spec: Docs/RE/packets/3-21_chat_channel.yaml — opcode 3/21 (0x30015), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. Only the ChannelSelector at 0x04 is partly decoded; the rest of the
// 56-byte header is modelled as opaque buffers per the spec.
//
// THIS IS THE FIXED 56-BYTE HEADER ONLY. The variable text body — a length-prefixed block
// [u32 textLength][text bytes] (textLength = string length + 1; ordinary channels gate text < 200,
// the special channel where selector % 10 == 5 bypasses the gate) — is NOT modelled as a struct;
// the caller (Client.Application) hand-codes it. No fixed WireSize / size assertion.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/21 — FIXED 56-byte context header of the client general / channel chat send (sender + channel
///     /scope metadata; only the channel selector at 0x04 is partly decoded). Followed on the wire by a
///     length-prefixed text body the caller hand-codes.
///     spec: Docs/RE/packets/3-21_chat_channel.yaml. CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(3, 21)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgChatChannelHeader
{
    /// <summary>Packed opcode 0x30015 (3/21). spec: packets/3-21_chat_channel.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgChatChannel;

    /// <summary>Size of the FIXED header in bytes. spec: packets/3-21_chat_channel.yaml (56-byte header).</summary>
    public const int HeaderSize = 56;

    /// <summary>0x00 — leading 4 bytes of the context header (not decoded). spec: same (HeaderPrefix: bytes[4]).</summary>
    public readonly HeaderPrefixBuffer HeaderPrefix;

    /// <summary>
    ///     0x04 — channel / scope selector (LE u32). A value where selector % 10 == 5 is a special
    ///     channel that bypasses the empty/length gate. PARTIAL CONFIDENCE. spec: packets/3-21_chat_channel.yaml.
    /// </summary>
    public readonly uint ChannelSelector;

    /// <summary>0x08 — remaining 48 bytes of the context header (not decoded). spec: same (HeaderRest: bytes[48]).</summary>
    public readonly HeaderRestBuffer HeaderRest;

    /// <summary>0x00 — 4-byte opaque header prefix. spec: packets/3-21_chat_channel.yaml.</summary>
    [InlineArray(4)]
    public struct HeaderPrefixBuffer
    {
        private byte _element0;
    }

    /// <summary>0x08 — 48-byte opaque header remainder. spec: packets/3-21_chat_channel.yaml.</summary>
    [InlineArray(48)]
    public struct HeaderRestBuffer
    {
        private byte _element0;
    }
}