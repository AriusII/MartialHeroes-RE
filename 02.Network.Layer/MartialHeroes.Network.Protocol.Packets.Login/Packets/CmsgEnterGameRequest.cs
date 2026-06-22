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
///     index, a 33-byte build-integrity self-checksum token, a 2-byte alignment gap, and a derived
///     32-bit version token. The version token is NOT a hardcoded constant: it is
///     <c>10 × (field 5 of the on-disk data/cursor/game.ver) + 9</c>. The enter path sends ONLY this
///     1/9 (triggered from the 3/14 handler after the server replies to the mode-1 1/7); the server
///     replies with S2C 3/5 <see cref="SmsgEnterGameAck" /> (scene → loading) and the world
///     self-snapshot arrives separately on S2C 4/1.
///     <para>
///         <b>SessionToken (CORRECTED — CYCLE 12 Phase 2):</b> the 33-byte field at +0x01 is the
///         <b>lowercase-hex MD5 digest of the client's OWN executable file</b> (argv[0] path run
///         through an MD5-of-file hasher): 32 lowercase-hex ASCII chars + a NUL terminator.
///         It is a build-integrity self-checksum, NOT a launcher/login session token and NOT the
///         typed account text. Populate it via <see cref="SessionTokenChecksum.WriteSelfChecksum" />.
///         spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
///     </para>
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
    ///     0x01 — 33-byte build-integrity self-checksum: the
    ///     <b>
    ///         lowercase-hex MD5 digest of the
    ///         client's own executable file
    ///     </b>
    ///     (argv[0] path), formatted as 32 lowercase-hex ASCII
    ///     chars + a NUL terminator. It is NOT a launcher/login session token and NOT the typed
    ///     account text. CODE-CONFIRMED source, format, and 33-byte width (CYCLE 12 Phase 2).
    ///     Offset is pinned at +0x01; exact digest bytes remain capture-pending (runtime-dependent).
    ///     Populate via <see cref="SessionTokenChecksum.WriteSelfChecksum" />.
    ///     spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
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

    /// <summary>
    ///     0x01 — 33-byte self-checksum field: 32 lowercase-hex MD5 chars + NUL terminator.
    ///     Populate via <see cref="SessionTokenChecksum" />.
    ///     spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01, "bytes[33]".
    /// </summary>
    [InlineArray(33)] // spec: Docs/RE/packets/cmsg_char_enter.yaml — "bytes[33]"
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