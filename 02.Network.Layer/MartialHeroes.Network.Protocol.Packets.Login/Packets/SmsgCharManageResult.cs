// spec: Docs/RE/packets/3-7_char_manage_result.yaml + Docs/RE/specs/login_flow.md §5.5 —
//       opcode 3/7 (0x30007), 8-byte fixed block.
//
// Note: opcode routing confirmed — 3/7 = manage/delete result; 3/4 = scene-entity update; 3/14 = spawn confirm.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field VALUE semantics are a hypothesis
// until a live capture confirms them.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/7 — character delete/select/rename MANAGE result. An 8-byte block: a result byte, a subtype byte
///     that selects which character operation the result is for, and a 4-byte ready_time used to drive the
///     same-day delete-cooldown timer. Subtype 2 is delete-confirm (decrements the account char count).
///     Fixed 8-byte block. spec: Docs/RE/packets/3-7_char_manage_result.yaml; login_flow.md §5.5.
///     CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(3, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharManageResult
{
    /// <summary>Packed opcode 0x30007 (3/7). spec: packets/3-7_char_manage_result.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgCharManageResult;

    /// <summary>Declared wire size in bytes. spec: packets/3-7_char_manage_result.yaml (size: 8).</summary>
    public const int WireSize = 8;

    /// <summary>0x00 — result code: 1 = success path, 0 = blocked/cooldown (ReadyTime drives the wait). spec: §5.5.</summary>
    public readonly byte Result;

    /// <summary>0x01 — reserved/padding byte between Result and Subtype, not consumed by the handler. spec: §5.5.</summary>
    public readonly byte Reserved1;

    /// <summary>0x02 — manage action selector: 0 = generic refresh, 1 = rename applied, 2 = delete-confirm. spec: §5.5.</summary>
    public readonly byte Subtype;

    /// <summary>0x03 — reserved/padding byte before the 4-byte ReadyTime, not consumed by the handler. spec: §5.5.</summary>
    public readonly byte Reserved3;

    /// <summary>
    ///     0x04 — delete-cooldown ready timestamp (LE u32). On the Result == 0 path the client formats a
    ///     "wait HH:MM" message from (ReadyTime - now). Epoch/units UNVERIFIED. spec: §5.5.
    /// </summary>
    public readonly uint ReadyTime;
}