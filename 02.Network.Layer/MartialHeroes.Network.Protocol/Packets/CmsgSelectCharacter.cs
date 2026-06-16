// spec: Docs/RE/packets/cmsg_char_select.yaml — opcode 1/7 (0x10007), 2-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED VALUE SEMANTICS !!!
// The (major:minor) routing and the fixed 2-byte size are control-flow CONFIRMED; the delete
// MODE-byte literal (1) is code-confirmed; the plain-select mode VALUE split (0 = view vs another
// split) stays capture/debugger-pending (capture_verified: false in the spec).
// spec: Docs/RE/packets/cmsg_char_select.yaml (VERIFICATION block).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/7 — character-MANAGE request on the character-select screen: a slot index plus a MODE byte that
/// discriminates the action. DELETE OVERLOADS this op (there is NO dedicated delete opcode in major 1):
/// <c>{slot, 0}</c> = plain select/view, <c>{slot, 1}</c> = delete the slot (code-confirmed literal).
/// Two emitters build the plain-select form; a third (the delete-confirm Yes path) builds the delete
/// form. The plain-select form pre-stages a slot, then 1/9 (cmsg_char_enter.yaml) drives world entry.
/// The delete/manage RESULT returns on S2C 3/7 SmsgCharManageResult (subtype 2 = delete-confirmed).
/// Fixed 2 bytes. spec: Docs/RE/packets/cmsg_char_select.yaml. CAPTURE-UNVERIFIED value semantics.
/// </summary>
[PacketOpcode(1, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgSelectCharacter
{
    /// <summary>Packed opcode 0x10007 (1/7). spec: packets/cmsg_char_select.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgSelectCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_select.yaml (size: 2).</summary>
    public const int WireSize = 2;

    /// <summary>0x00 — selected character slot index, range 0..4. STATIC HIGH. spec: packets/cmsg_char_select.yaml.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    /// 0x01 — mode discriminator selecting which manage action this 1/7 carries:
    /// <c>0</c> = plain select / view a slot (STATIC HIGH for the select path); <c>1</c> = DELETE the
    /// slot (code-confirmed literal — the delete-confirm path writes 1 here, so the delete body is
    /// <c>{slot, 1}</c>; the earlier "2 by elimination" reading is refuted). DELETE rides this op:
    /// there is no dedicated delete opcode. The delete RESULT arrives on S2C 3/7 SmsgCharManageResult.
    /// spec: Docs/RE/packets/cmsg_char_select.yaml (Mode: 0 = select/view, 1 = delete).
    /// </summary>
    public readonly byte Mode;
}
