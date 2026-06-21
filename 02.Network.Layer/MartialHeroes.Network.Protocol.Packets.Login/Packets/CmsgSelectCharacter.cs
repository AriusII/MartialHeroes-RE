// spec: Docs/RE/packets/cmsg_char_select.yaml — opcode 1/7 (0x10007), 2-byte fixed payload.
//
// Binary-confirmed (Phase 2b, build 263bd994): 1/7 is a character-SELECT commit, NOT a delete
// carrier. The single send-builder has exactly two call sites in the select-window command handler:
//   mode 1 = select-and-play (the "play / select this slot" confirm)
//   mode 0 = slot-lock / pre-play (the "lock / pre-stage" confirm)
// The prior "CmsgManageCharacter / DELETE overloads mode=1" reading is REFUTED.
// There is NO major-1 char-delete opcode on this build.
// Character removal surfaces only via the inbound 3/7 SmsgCharManageResult (subtype 2).
// The runtime meaning of mode 1 vs mode 0 is the only part still capture/debugger-pending.
// spec: Docs/RE/specs/net_contracts.md §2.2; Docs/RE/specs/login_flow.md §3.6.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     1/7 — character-SELECT slot request on the character-select screen.
///     A 2-byte body: slot index and a mode byte.
///     Binary-confirmed (Phase 2b, build 263bd994):
///     <list type="bullet">
///         <item><c>mode 1</c> = <b>select-and-play</b> (the "play / select this slot" confirm path).</item>
///         <item><c>mode 0</c> = <b>slot-lock / pre-play</b> (the slot-lock / pre-stage confirm path).</item>
///     </list>
///     There is NO delete mode on 1/7 and NO dedicated major-1 char-delete opcode on this build.
///     Character removal surfaces only via the inbound S2C <c>3/7 SmsgCharManageResult</c> (subtype 2).
///     Fixed 2 bytes. spec: Docs/RE/packets/cmsg_char_select.yaml;
///     Docs/RE/specs/net_contracts.md §2.2; Docs/RE/specs/login_flow.md §3.6.
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
    ///     0x00 — selected character slot index, range 0..4. spec: Docs/RE/packets/cmsg_char_select.yaml SlotIndex @0x00.
    /// </summary>
    public readonly byte SlotIndex;

    /// <summary>
    ///     0x01 — mode byte selecting the select action:
    ///     <c>1</c> = select-and-play (binary-confirmed, Phase 2b); <c>0</c> = slot-lock / pre-play
    ///     (binary-confirmed, Phase 2b). Runtime meaning of each mode is still capture/debugger-pending.
    ///     There is NO delete mode here. spec: Docs/RE/packets/cmsg_char_select.yaml Mode @0x01;
    ///     Docs/RE/specs/login_flow.md §3.6; Docs/RE/specs/net_contracts.md §2.2.
    /// </summary>
    public readonly byte Mode;
}