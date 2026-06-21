// spec: Docs/RE/opcodes.md — opcode 3/100 (0x30064), 4-byte fixed action/result code.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/100 — generic character-management action/result code. The 4-byte result is the discriminator
///     for the scene-state table in client_runtime.md §7.5.2 (0, 1..4/7, 202/203/232, out-of-range).
///     spec: Docs/RE/opcodes.md; Docs/RE/specs/client_runtime.md §7.5.2.
/// </summary>
[PacketOpcode(3, 100)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharActionResult
{
    /// <summary>Packed opcode 0x30064 (3/100). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.SmsgCharActionResult;

    /// <summary>Declared wire size in bytes. spec: opcodes.md (3/100 reads a leading u32).</summary>
    public const int WireSize = 4;

    /// <summary>0x00 — action/result code. spec: client_runtime.md §7.5.2.</summary>
    public readonly uint Result;
}