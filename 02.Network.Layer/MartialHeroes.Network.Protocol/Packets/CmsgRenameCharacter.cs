// spec: Docs/RE/packets/cmsg_char_rename.yaml — opcode 1/13 (0x1000d), 18-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the field layout is a static inference
// (capture_verified: false). The workflow-spine analysis refines the earlier "18-byte name-only"
// model: the body is a contiguous 1 + 17 run (slot byte + 17-byte CP949 name), NOT a single 18-byte
// name buffer. Both fields are STATIC HIGH. Field widths sum to size: 1 + 17 = 18.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/13 — client rename-character request from the character-select screen: a leading slot-index
/// byte then a 17-byte fixed CP949 (Korean code page 949) new-name buffer, NUL-terminated within.
/// The server answers with S2C 3/6 SmsgRenameCharResult (and a 3/4 subtype-1 slot refresh). Fixed
/// 18-byte payload.
/// spec: Docs/RE/packets/cmsg_char_rename.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgRenameCharacter
{
    /// <summary>Packed opcode 0x1000d (1/13). spec: packets/cmsg_char_rename.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgRenameCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_rename.yaml (size: 18).</summary>
    public const int WireSize = 18;

    /// <summary>0x00 — slot being renamed (the currently selected slot). STATIC HIGH. spec: packets/cmsg_char_rename.yaml.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    /// 0x01 — new character name, a fixed 17-byte CP949 buffer, NUL-terminated within. Decode as this
    /// inline-array blob, trim at the first NUL, then CP949-decode — never a managed string on the
    /// wire. STATIC HIGH. spec: packets/cmsg_char_rename.yaml.
    /// </summary>
    public readonly NewNameBuffer NewName;

    /// <summary>0x01 — 17-byte CP949 name buffer (NUL-terminated within). spec: packets/cmsg_char_rename.yaml.</summary>
    [InlineArray(17)]
    public struct NewNameBuffer
    {
        private byte _element0;
    }
}