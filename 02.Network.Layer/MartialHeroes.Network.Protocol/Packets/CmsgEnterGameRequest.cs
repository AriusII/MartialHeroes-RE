// spec: Docs/RE/packets/cmsg_char_enter.yaml — opcode 1/9 (0x10009), 40-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the field layout is a static inference
// (capture_verified: false). The enter-selected-character helper memsets a 40-byte buffer to zero,
// then partially fills it. STATIC HIGH: SlotIndex@0, the 33-byte VersionBlob@1, and the u32
// VersionCheck@0x24 placement. STATIC MEDIUM: the 2-byte Pad@0x22 (observed unwritten/zero, but the
// exact pad-vs-int boundary is capture work) and the exact VersionCheck derivation.
//
// Field widths sum to size: 1 + 33 + 2 + 4 = 40.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/9 — client enter-world request. A 40-byte zero-initialised buffer: the chosen character slot
/// index, a 33-byte client/game-version blob (NUL-terminated ASCII within), a 2-byte alignment gap,
/// and a derived 32-bit version-check value (an anti-tamper / build stamp). The server replies with
/// S2C 3/5 SmsgEnterGameAck and the client transitions into the world-enter flow.
/// spec: Docs/RE/packets/cmsg_char_enter.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 9)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgEnterGameRequest
{
    /// <summary>Packed opcode 0x10009 (1/9). spec: packets/cmsg_char_enter.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgEnterGameRequest;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_enter.yaml (size: 40).</summary>
    public const int WireSize = 40;

    /// <summary>0x00 — selected character slot, range 0..4. STATIC HIGH. spec: packets/cmsg_char_enter.yaml.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    /// 0x01 — client/game-version string: a fixed 33-byte buffer holding a NUL-terminated ASCII
    /// string. Decode as this inline-array blob, trim at the first NUL — never a managed string on
    /// the wire. STATIC HIGH for the 33-byte field. spec: packets/cmsg_char_enter.yaml.
    /// </summary>
    public readonly VersionBlobBuffer VersionBlob;

    /// <summary>
    /// 0x22 — 2-byte alignment gap before the u32 version-check value; observed unwritten (zero).
    /// STATIC MEDIUM. spec: packets/cmsg_char_enter.yaml.
    /// </summary>
    public readonly PadBuffer Pad;

    /// <summary>
    /// 0x24 — derived 32-bit version-check value (build stamp / anti-tamper) computed from the local
    /// version table. STATIC HIGH for placement, MEDIUM for the exact derivation.
    /// spec: packets/cmsg_char_enter.yaml.
    /// </summary>
    public readonly uint VersionCheck;

    /// <summary>0x01 — 33-byte client/game-version blob (asciiz within). spec: packets/cmsg_char_enter.yaml.</summary>
    [InlineArray(33)]
    public struct VersionBlobBuffer
    {
        private byte _element0;
    }

    /// <summary>0x22 — 2-byte zero alignment gap. spec: packets/cmsg_char_enter.yaml.</summary>
    [InlineArray(2)]
    public struct PadBuffer
    {
        private byte _element0;
    }
}