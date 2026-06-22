// spec: Docs/RE/packets/3-14_char_spawn_response.yaml + Docs/RE/specs/login_flow.md §5.3 —
//       opcode 3/14 (0x3000e), 16-byte fixed block.
//
// Note: opcode routing confirmed — 3/14 = enter/spawn-confirm; 3/7 = manage/delete result; 3/4 = scene-entity update.
// (opcodes.md catalog name: SmsgCharSpawnResponse; struct retains SmsgCharSpawnResult — rename is Tier-1-owned.)
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The 16-byte size is dispatch-confirmed (login_flow.md §5.3; opcodes.md 3/14). The meaning of the
// three trailing spawn-param u32s is UNVERIFIED per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/14 — char enter-into-world bridge / spawn-confirm response that drives the local spawn after the
///     enter-world handshake. On Result != 0 the client re-enters the enter-builder from the carried
///     fields; on Result == 0 a fallback timeout starts. Fixed 16-byte block. (opcodes.md catalog name:
///     SmsgCharSpawnResponse.) spec: Docs/RE/packets/3-14_char_spawn_response.yaml; login_flow.md §5.3.
///     CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(3, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharSpawnResult
{
    /// <summary>Packed opcode 0x3000e (3/14). spec: packets/3-14_char_spawn_response.yaml; login_flow.md §5.3.</summary>
    public const uint OpcodeId = Opcodes.SmsgCharSpawnResult;

    /// <summary>Declared wire size in bytes. spec: packets/3-14_char_spawn_response.yaml (size: 16).</summary>
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