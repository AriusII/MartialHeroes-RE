// spec: Docs/RE/packets/1-9_enter_game_request.yaml — opcode 1/9 (0x10009), 40-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. Only the leading SlotIndex byte is high-confidence; the VersionToken
// derivation and whether Tail stays zero are UNKNOWN per the spec.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/9 — client enter-world / select-character request: a character slot index plus a
/// client-version handshake token. Fixed 40-byte zero-initialised buffer. The server replies with
/// S2C 3/5 EnterGameAck.
/// spec: Docs/RE/packets/1-9_enter_game_request.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 9)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgEnterGameRequest
{
    /// <summary>Packed opcode 0x10009 (1/9). spec: packets/1-9_enter_game_request.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgEnterGameRequest;

    /// <summary>Declared wire size in bytes. spec: packets/1-9_enter_game_request.yaml (size: 40).</summary>
    public const int WireSize = 40;

    /// <summary>0x00 — selected character slot (0..4). HIGH CONFIDENCE. spec: same.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    /// 0x01 — client/version handshake token: a 33-byte fixed buffer holding a NUL-terminated ASCII
    /// string. LOWER CONFIDENCE (derivation unconfirmed). spec: packets/1-9_enter_game_request.yaml.
    /// </summary>
    public readonly VersionTokenBuffer VersionToken;

    /// <summary>0x22 — zero-filled remainder of the 40-byte buffer. spec: same (Tail: bytes[6]).</summary>
    public readonly TailBuffer Tail;

    /// <summary>0x01 — 33-byte version-token buffer (asciiz within). spec: packets/1-9_enter_game_request.yaml.</summary>
    [InlineArray(33)]
    public struct VersionTokenBuffer
    {
        private byte _element0;
    }

    /// <summary>0x22 — 6-byte zero tail. spec: packets/1-9_enter_game_request.yaml.</summary>
    [InlineArray(6)]
    public struct TailBuffer
    {
        private byte _element0;
    }
}