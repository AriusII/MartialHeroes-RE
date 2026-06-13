// spec: Docs/RE/packets/1-14_delete_character.yaml — opcode 1/14 (0x1000e), 1-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. SlotIndex is MEDIUM-HIGH confidence per the spec; no confirmation
// token was observed to accompany the slot byte.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/14 — client delete-character request from the character-select screen: a single byte, the
/// slot index of the character to delete. The server answers with the 8-byte char-manage result
/// (S2C 3/4, subtype 2 = delete confirmed; or result 0 + a same-day delete-cooldown ready_time).
/// Fixed 1-byte payload.
/// spec: Docs/RE/packets/1-14_delete_character.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgDeleteCharacter
{
    /// <summary>Packed opcode 0x1000e (1/14). spec: packets/1-14_delete_character.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgDeleteCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/1-14_delete_character.yaml (size: 1).</summary>
    public const int WireSize = 1;

    /// <summary>0x00 — character slot to delete, range 0..4 (the highlighted slot). MEDIUM-HIGH confidence. spec: same.</summary>
    public readonly byte SlotIndex;
}
