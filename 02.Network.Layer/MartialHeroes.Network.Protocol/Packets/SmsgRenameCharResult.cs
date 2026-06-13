// spec: Docs/RE/specs/login_flow.md §5.6 — opcode 3/6 (0x30006), 19-byte (0x13) fixed block.
// (No packets/3-6_*.yaml exists; login_flow.md §5.6 is the authoritative field table for this body.)
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The 19-byte size is dispatch-confirmed (login_flow.md §5.6). The success/failure discrimination is
// by the result byte at offset 0. The NameOrError field is an overlay: on success it holds the new
// character name as a CP949 ASCIIZ string (up to 18 bytes incl. NUL); on failure byte +1 of the
// frame (i.e. NameOrError[0]) carries an error code in range 0xC8..0xD4 (mapped to UI strings).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 3/6 — rename-character result. A 19-byte block: a result byte, then an 18-byte field that is the
/// new CP949 character name (ASCIIZ within) on success, or an error code in its first byte on
/// failure. Result != 0 means success. spec: Docs/RE/specs/login_flow.md §5.6. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(3, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRenameCharResult
{
    /// <summary>Packed opcode 0x30006 (3/6). spec: Docs/RE/specs/login_flow.md §5.6.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgRenameCharResult;

    /// <summary>Declared wire size in bytes. spec: login_flow.md §5.6 (19-byte / 0x13 block).</summary>
    public const int WireSize = 19;

    /// <summary>0x00 — nonzero = success, 0 = failure (read NameOrError[0] as the error code). spec: §5.6.</summary>
    public readonly byte Result;

    /// <summary>
    /// 0x01 — overlay: on success, the new character name as a CP949 (Korean code page 949) ASCIIZ
    /// string (up to 18 bytes incl. NUL); on failure, NameOrError[0] is an error code in range
    /// 0xC8..0xD4. Decode as this inline-array blob, never a managed string on the wire. spec: §5.6.
    /// </summary>
    public readonly NameOrErrorBuffer NameOrError;

    /// <summary>0x01 — 18-byte CP949 name buffer (asciiz within) / error-code overlay. spec: login_flow.md §5.6.</summary>
    [InlineArray(18)]
    public struct NameOrErrorBuffer
    {
        private byte _element0;
    }
}
