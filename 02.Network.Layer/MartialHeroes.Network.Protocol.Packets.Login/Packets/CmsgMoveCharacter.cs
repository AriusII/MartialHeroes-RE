// spec: Docs/RE/packets/cmsg_char_move.yaml — opcode 1/14 (0x1000e), 1-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the field layout is a static inference
// (capture_verified: false). SlotIndex@0 is STATIC HIGH for placement.
//
// CONTESTED LABEL — CYCLE 12 DEFERRED (spec: Docs/RE/opcodes.md, row 0x1000e):
//   Wire binding:  [PacketOpcode(1, 14)] + 1-byte [slot index 0..4] — wire-confirmed.
//   Send-site:     C2S debug strings at the two build-site emitters read "is sending location"
//                  (implying slot MOVE / relocate).
//   Server effect: the only S2C reply attributed to 1/14 is 3/7 SmsgCharManageResult sub-byte=2,
//                  which decrements the account character count, clears the slot, and shows a
//                  delete-cooldown (i.e., the server performs DELETION).
//   Gap:           no separate C2S delete opcode exists in the binary; no "delete character"
//                  debug string is present at this send site.
//   Status:        MOVE-vs-DELETE canonical name is NOT statically resolvable; a capture/debugger
//                  oracle is required. Deferred to CYCLE 12. Do NOT rename this struct until
//                  the oracle confirms the canonical label.
//                  spec: Docs/RE/opcodes.md (0x1000e CONTESTED-label note)

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     1/14 — client slot-MOVE / relocate request from the character-select screen: a single byte, the
///     target slot index. Two emitters in the select-window dispatcher build the same 1-byte body, gated
///     by slot &lt;= 4 and the select-window net-busy latch. No server reply was attributed statically.
///     (Prior reading: delete; the move-vs-delete reconciliation is Tier-1's.) Fixed 1-byte payload.
///     spec: Docs/RE/packets/cmsg_char_move.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgMoveCharacter
{
    /// <summary>Packed opcode 0x1000e (1/14). spec: packets/cmsg_char_move.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgMoveCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_move.yaml (size: 1).</summary>
    public const int WireSize = 1;

    /// <summary>0x00 — target slot index, range 0..4. STATIC HIGH for placement. spec: packets/cmsg_char_move.yaml.</summary>
    public readonly byte SlotIndex;
}