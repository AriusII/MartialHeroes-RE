// spec: Docs/RE/packets/4-29_stat_update.yaml — opcode 4/29 (0x4001d), 36-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The Handle/SessionToken split at 0x00/0x04 and the meaning of the
// three bytes at 0x09 are UNKNOWN per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     4/29 — server acknowledges a stat-allocation request: echoes the five resulting absolute stats
///     and the remaining stat points; applied only when ResultOk == 1. Fixed 36-byte block.
///     Pairs with the C2S 2/29 StatAllocate request.
///     spec: Docs/RE/packets/4-29_stat_update.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(4, 29)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgStatUpdate
{
    /// <summary>Packed opcode 0x4001d (4/29). spec: packets/4-29_stat_update.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgStatUpdate;

    /// <summary>Declared wire size in bytes. spec: packets/4-29_stat_update.yaml (size: 36).</summary>
    public const int WireSize = 36;

    /// <summary>0x00 — handle / actor or request id (split vs SessionToken unconfirmed). spec: same.</summary>
    public readonly uint Handle;

    /// <summary>0x04 — session token (split vs Handle unconfirmed). spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint SessionToken;

    /// <summary>0x08 — result flag: value 1 applies the update. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly byte ResultOk;

    /// <summary>0x09 — read but not decoded / padding. spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x0c — stat[0] echo. HIGH CONFIDENCE. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint Stat0;

    /// <summary>0x10 — stat[1] echo. HIGH CONFIDENCE. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint Stat1;

    /// <summary>0x14 — stat[2] echo. HIGH CONFIDENCE. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint Stat2;

    /// <summary>0x18 — stat[3] echo. HIGH CONFIDENCE. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint Stat3;

    /// <summary>0x1c — stat[4] echo. HIGH CONFIDENCE. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint Stat4;

    /// <summary>0x20 — remaining allocatable stat points. spec: packets/4-29_stat_update.yaml.</summary>
    public readonly uint RemainingStatPoints;
}