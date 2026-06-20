// spec: Docs/RE/packets/3-6_rename_char_result.yaml — opcode 3/6 (0x30006), 12-byte fixed block.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/6 — rename-character result. A 12-byte block: result code, error code, padding, slot index, and an unverified
///     dword.
///     spec: Docs/RE/packets/3-6_rename_char_result.yaml.
/// </summary>
[PacketOpcode(3, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRenameCharResult
{
    /// <summary>Packed opcode 0x30006 (3/6). spec: Docs/RE/packets/3-6_rename_char_result.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgRenameCharResult;

    /// <summary>Declared wire size in bytes. spec: packets/3-6_rename_char_result.yaml (size: 12).</summary>
    public const int WireSize = 12;

    /// <summary>0x00 — result status: 1 = success, 0 = failure.</summary>
    public readonly byte Result;

    /// <summary>0x01 — error sub-code (used when Result == 0).</summary>
    public readonly byte ErrorCode;

    /// <summary>0x02 — alignment padding.</summary>
    public readonly PadBuffer Pad;

    /// <summary>0x04 — slot index of the renamed character.</summary>
    public readonly uint SlotIndex;

    /// <summary>0x08 — unverified dword field.</summary>
    public readonly uint Unk;

    /// <summary>Fixed 2-byte inline buffer. spec: packets/3-6_rename_char_result.yaml</summary>
    [InlineArray(2)]
    public struct PadBuffer
    {
        private byte _element0;
    }
}