// spec: Docs/RE/packets/2-83_chat_contextual.yaml — opcode 2/83 (0x20053), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The internal field breakdown of the 24-byte context header was NOT
// decoded this pass — it is modelled as one opaque buffer per the spec.
//
// THIS IS THE FIXED 24-BYTE HEADER ONLY. The variable text body — a length-prefixed block
// [u32 textLength][text bytes] (textLength = string length + 1, text length 0..199) — is NOT
// modelled as a struct; the caller (Client.Application) hand-codes it. No fixed WireSize / size
// assertion: this packet has no fixed size.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

/// <summary>
///     2/83 — FIXED 24-byte context header of the client contextual chat send (sender/target/channel
///     metadata, fields not yet decoded). Followed on the wire by a length-prefixed text body the
///     caller hand-codes.
///     spec: Docs/RE/packets/2-83_chat_contextual.yaml. CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(2, 83)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgChatContextualHeader
{
    /// <summary>Packed opcode 0x20053 (2/83). spec: packets/2-83_chat_contextual.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgChatContextual;

    /// <summary>Size of the FIXED header in bytes. spec: packets/2-83_chat_contextual.yaml (24-byte header).</summary>
    public const int HeaderSize = 24;

    /// <summary>
    ///     0x00 — 24-byte context/metadata struct (sender / target / channel metadata; fields not yet
    ///     decoded, modelled as one opaque buffer). spec: packets/2-83_chat_contextual.yaml.
    /// </summary>
    public readonly ContextHeaderBuffer ContextHeader;

    /// <summary>0x00 — 24-byte opaque context header. spec: packets/2-83_chat_contextual.yaml.</summary>
    [InlineArray(24)]
    public struct ContextHeaderBuffer
    {
        private byte _element0;
    }
}