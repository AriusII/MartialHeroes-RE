// spec: Docs/RE/specs/login_flow.md §5.4 — opcode 3/23 (0x30017), 12-byte fixed block.
// (No packets/3-23_*.yaml exists; login_flow.md §5.4 is the authoritative field table for this body.)
//
// !!! CAPTURE-UNVERIFIED INTERNALS (size is capture-verified) !!!
// opcodes.md carries a capture-verified 12-byte example for this message; the field internals beyond
// result/code are otherwise UNVERIFIED (login_flow.md §5.4). On success the account char count is
// incremented. The 0xC8..0xD4 failure error-code range in Code is shared with the rename result (§5.6).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 3/23 — character-create result. A 12-byte block: a result byte, a code byte (assigned slot id on
/// success, error code 0xC8..0xD4 on failure), and two trailing u32 values passed to slot refresh on
/// success. Result 1 = success, 0 = failure. spec: Docs/RE/specs/login_flow.md §5.4. Size capture-verified.
/// </summary>
[PacketOpcode(3, 23)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharCreateResult
{
    /// <summary>Packed opcode 0x30017 (3/23). spec: Docs/RE/specs/login_flow.md §5.4.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgCharCreateResult;

    /// <summary>Declared wire size in bytes. spec: login_flow.md §5.4 (12-byte block, capture-verified).</summary>
    public const int WireSize = 12;

    /// <summary>0x00 — 1 = success, 0 = failure. spec: login_flow.md §5.4.</summary>
    public readonly byte Result;

    /// <summary>0x01 — on success: the assigned slot id; on failure: an error code (0xC8..0xD4). spec: §5.4.</summary>
    public readonly byte Code;

    /// <summary>0x02 — alignment padding (u16). spec: login_flow.md §5.4.</summary>
    public readonly ushort Pad;

    /// <summary>0x04 — value passed to slot refresh on success (LE u32). MEANING UNVERIFIED. spec: §5.4.</summary>
    public readonly uint Value1;

    /// <summary>0x08 — value passed to slot refresh on success (LE u32). MEANING UNVERIFIED. spec: §5.4.</summary>
    public readonly uint Value2;
}