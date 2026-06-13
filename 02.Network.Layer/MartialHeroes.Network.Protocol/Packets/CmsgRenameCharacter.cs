// spec: Docs/RE/packets/1-13_rename_character.yaml — opcode 1/13 (0x1000d), 18-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The select screen caps display names at 17 bytes, leaving room for
// one NUL terminator in the 18-byte buffer per the spec.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/13 — client rename-character request from the character-select screen: a fixed 18-byte buffer
/// holding the new character name as a CP949 (Korean code page 949) string, NUL-terminated within
/// the buffer. The target slot is the currently selected one (no slot byte travels with the name).
/// The server answers with S2C 3/6 SmsgRenameCharResult (and a 3/4 subtype-1 slot refresh).
/// spec: Docs/RE/packets/1-13_rename_character.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgRenameCharacter
{
    /// <summary>Packed opcode 0x1000d (1/13). spec: packets/1-13_rename_character.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgRenameCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/1-13_rename_character.yaml (size: 18).</summary>
    public const int WireSize = 18;

    /// <summary>
    /// 0x00 — new character name, a fixed 18-byte CP949 buffer, NUL-terminated within. Decode as
    /// this inline-array blob, trim at the first NUL, then CP949-decode — never a managed string on
    /// the wire. spec: packets/1-13_rename_character.yaml.
    /// </summary>
    public readonly NewNameBuffer NewName;

    /// <summary>0x00 — 18-byte CP949 name buffer (asciiz/NUL-terminated within). spec: packets/1-13_rename_character.yaml.</summary>
    [InlineArray(18)]
    public struct NewNameBuffer
    {
        private byte _element0;
    }
}