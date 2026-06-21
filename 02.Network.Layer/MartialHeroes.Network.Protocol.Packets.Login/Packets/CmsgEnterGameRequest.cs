// spec: Docs/RE/packets/cmsg_char_enter.yaml — opcode 1/9 (0x10009), 40-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the field layout is a static inference
// (capture_verified: false). The enter-selected-character helper zero-fills a 40-byte buffer, then
// partially fills it. CODE-CONFIRMED (static HIGH): byte0 = SlotIndex, a 33-byte SessionToken
// string at +0x01, a 2-byte Pad at +0x22, and a u32 VersionToken at +0x24. The intra-buffer offsets
// are pinned in cmsg_char_enter.yaml; the token bytes and pad semantics remain capture-pending.
//
// Field widths sum to size: 1 + 33 + 2 + 4 = 40. spec: Docs/RE/packets/cmsg_char_enter.yaml.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     1/9 — client enter-world request. A 40-byte zero-initialised buffer: the chosen character slot
///     index, a 33-byte launcher-supplied session/identity token string (copied from the process
///     command-line / argv0 global, NUL-bounded within — NOT the typed account text), a 2-byte
///     alignment gap, and a derived 32-bit version token. The version token is NOT a hardcoded
///     constant: it is <c>10 × (field 5 of the on-disk data/cursor/game.ver) + 9</c>. The enter path
///     sends ONLY this 1/9 (the 1/7 manage was already sent at slot click); the server replies with
///     S2C 3/5 <see cref="SmsgEnterGameAck" /> (scene → loading) and the world self-snapshot arrives
///     separately on S2C 4/1.
///     spec: Docs/RE/packets/cmsg_char_enter.yaml. CAPTURE-UNVERIFIED value semantics.
/// </summary>
[PacketOpcode(1, 9)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgEnterGameRequest
{
    /// <summary>Packed opcode 0x10009 (1/9). spec: packets/cmsg_char_enter.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgEnterGameRequest;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_enter.yaml (size: 40).</summary>
    public const int WireSize = 40;

    /// <summary>0x00 — selected character slot, range 0..4. CODE-CONFIRMED. spec: packets/cmsg_char_enter.yaml.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    ///     0x01 — 33-byte launcher-supplied session/identity token string (asciiz within). Copied from
    ///     the process command-line / argv0 global — NOT the typed account text. Decode as this
    ///     inline-array blob, trim at the first NUL — never a managed string on the wire. CODE-CONFIRMED
    ///     source + 33-byte width.
    ///     Offset is pinned at +0x01; token byte values remain capture-pending.
    ///     spec: cmsg_char_enter.yaml.
    /// </summary>
    public readonly SessionTokenBuffer SessionToken;

    /// <summary>
    ///     0x22 — 2-byte alignment gap before the version dword; observed zero.
    ///     Whether this Pad truly stays zero or carries a small trailing field remains capture-pending.
    ///     spec: packets/cmsg_char_enter.yaml.
    /// </summary>
    public readonly PadBuffer Pad;

    /// <summary>
    ///     0x24 — derived 32-bit version token (LE u32). DERIVED, not constant: the client reads field
    ///     index 5 of the on-disk data/cursor/game.ver and computes <c>10 × (field 5) + 9</c>.
    ///     CODE-CONFIRMED for the value formula and on-disk source.
    ///     Offset is pinned at +0x24; concrete runtime value still derives from game.ver.
    ///     spec: cmsg_char_enter.yaml.
    /// </summary>
    public readonly uint VersionToken;

    /// <summary>0x01 — 33-byte launcher session/identity token string (asciiz within). spec: packets/cmsg_char_enter.yaml.</summary>
    [InlineArray(33)]
    public struct SessionTokenBuffer
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