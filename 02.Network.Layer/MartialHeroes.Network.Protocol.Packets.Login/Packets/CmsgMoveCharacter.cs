// spec: Docs/RE/packets/cmsg_char_move.yaml — opcode 1/14 (0x1000e), 1-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the field layout is a static inference
// (capture_verified: false). SlotIndex@0 is STATIC HIGH for placement. NAMING NOTE (load-bearing):
// a prior committed catalog/spec called 1/14 "delete"; the workflow-spine dossier re-attributes it
// to slot-MOVE / relocate. Whether 1/14 is move or delete (or both, by context) and which canonical
// name wins is a Tier-1 reconciliation — this struct records the static slot-move builder finding.

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