// spec: Docs/RE/specs/login_flow.md §5.3 — opcode 3/7 (0x30007), 16-byte fixed block.
// (No packets/3-7_*.yaml exists; login_flow.md §5.3 is the authoritative field table for this body.)
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The 16-byte size is dispatch-confirmed (login_flow.md §5.3). The meaning of the three trailing
// spawn-param u32s is UNVERIFIED per the spec. Distinct from the 8-byte char-manage result (3/4);
// do NOT conflate (some raw notes mislabel the 3/4 manage handler as '3/7' — see opcodes.md).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 3/7 — spawn-into-world response that drives the local spawn after the enter-world handshake.
/// On Result != 0 the client spawns the local player from the cached 880-byte descriptor; on
/// Result == 0 a timed failure message is shown. Fixed 16-byte block.
/// spec: Docs/RE/specs/login_flow.md §5.3. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(3, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharSpawnResult
{
    /// <summary>Packed opcode 0x30007 (3/7). spec: Docs/RE/specs/login_flow.md §5.3.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgCharSpawnResult;

    /// <summary>Declared wire size in bytes. spec: login_flow.md §5.3 (16-byte block).</summary>
    public const int WireSize = 16;

    /// <summary>0x00 — 0 = failure (timed message shown); nonzero = proceed to spawn. spec: §5.3.</summary>
    public readonly byte Result;

    /// <summary>0x01 — character slot index. spec: login_flow.md §5.3.</summary>
    public readonly byte Slot;

    /// <summary>0x02 — alignment padding (u16). spec: login_flow.md §5.3.</summary>
    public readonly ushort Pad;

    /// <summary>0x04 — spawn param 1 passed to the spawn routine (LE u32). MEANING UNVERIFIED. spec: §5.3.</summary>
    public readonly uint SpawnParam1;

    /// <summary>0x08 — spawn param 2 passed to the spawn routine (LE u32). MEANING UNVERIFIED. spec: §5.3.</summary>
    public readonly uint SpawnParam2;

    /// <summary>0x0c — spawn param 3 passed to the spawn routine (LE u32). MEANING UNVERIFIED. spec: §5.3.</summary>
    public readonly uint SpawnParam3;
}
