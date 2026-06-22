// spec: Docs/RE/packets/cmsg_char_select.yaml — opcode 1/7 (0x10007), 2-byte fixed payload.
//
// CYCLE 11 / Block A (IDB 263bd994): 1/7 carries BOTH slot-select AND delete-confirm — they share
// the same opcode and the same 2-byte {SlotIndex, Mode} body, distinguished only by the Mode byte.
// spec: Docs/RE/specs/frontend_scenes.md §A.2 (delete uses 1/7 with the delete-context flag).
//
// The earlier reading in cmsg_char_select.yaml ("both emit sites are SELECT paths") represents the
// static-analysis view of two specific call sites; §A.2 (addendum, IDB 263bd994) confirms a
// delete-confirm action ALSO sends 1/7 with the flag set. Both readings are consistent: the
// builder is shared; the Mode byte is the discriminator.
//
// Mode byte VALUE semantics (capture/debugger-pending):
//   mode 0 = slot-lock / pre-play (site B)
//   mode 1 = select-and-play (site A); ALSO the delete-confirm path per frontend_scenes.md §5
// The runtime meaning of each mode value and exactly which mode the delete-confirm sends is the
// only remaining capture-pending fact. The 2-byte layout and the shared opcode are CONFIRMED.
// spec: Docs/RE/packets/cmsg_char_select.yaml; Docs/RE/specs/frontend_scenes.md §A.2 and §5.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     1/7 — character-SELECT slot commit or DELETE-confirm on the character-select screen.
///     A 2-byte body: slot index and a mode byte that distinguishes the two actions.
///     <para>
///         Both ordinary slot-select and delete-confirm send the <b>same</b> <c>1/7</c> opcode with the
///         same <c>{SlotIndex, Mode}</c> layout; they differ only by the <see cref="Mode" /> byte
///         (the delete-context flag). The Mode byte value semantics are capture/debugger-pending:
///         mode 1 is the select-and-play / delete-confirm path; mode 0 is the slot-lock / pre-play path.
///     </para>
///     <para>
///         There is no dedicated major-1 char-delete opcode separate from 1/7 on this build.
///         Delete outcomes also surface via the inbound <c>3/7 SmsgCharManageResult</c> (subtype 2).
///     </para>
///     Fixed 2 bytes. spec: Docs/RE/packets/cmsg_char_select.yaml;
///     Docs/RE/specs/frontend_scenes.md §A.2 (1/7 carries select AND delete-confirm via mode byte).
///     CAPTURE-UNVERIFIED mode value semantics.
/// </summary>
[PacketOpcode(1, 7)] // spec: Docs/RE/opcodes.md row 1/7
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgSelectCharacterSlot
{
    /// <summary>Packed opcode 0x10007 (1/7). spec: Docs/RE/packets/cmsg_char_select.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgSelectCharacterSlot; // spec: Docs/RE/opcodes.md row 1/7

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/cmsg_char_select.yaml (size: 2).</summary>
    public const int WireSize = 2; // spec: Docs/RE/packets/cmsg_char_select.yaml

    /// <summary>
    ///     0x00 — selected character slot index, range 0..4.
    ///     spec: Docs/RE/packets/cmsg_char_select.yaml SlotIndex @0x00.
    /// </summary>
    public readonly byte SlotIndex;

    /// <summary>
    ///     0x01 — mode byte: distinguishes slot-select from delete-confirm via the same 1/7 opcode.
    ///     <list type="bullet">
    ///         <item>
    ///             <c>mode 1</c> = select-and-play confirm (binary-confirmed at site A) / delete-confirm
    ///             (spec: Docs/RE/specs/frontend_scenes.md §5 and §A.2).
    ///         </item>
    ///         <item><c>mode 0</c> = slot-lock / pre-play confirm (binary-confirmed at site B).</item>
    ///     </list>
    ///     The runtime meaning of each mode value is capture/debugger-pending.
    ///     spec: Docs/RE/packets/cmsg_char_select.yaml Mode @0x01;
    ///     Docs/RE/specs/frontend_scenes.md §A.2 (flag-byte discriminator for select vs delete-context).
    /// </summary>
    public readonly byte Mode;
}