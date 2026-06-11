// spec: Docs/RE/packets/5-32_level_up.yaml — opcode 5/32 (0x50020), 48-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The HP/MP/stamina/level core is high-confidence; the 16-byte rank-XP
// tail packing (Tail20 / RankXpWithin / RankXpTailHi) is PROVISIONAL per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/32 — server notifies that an actor levelled up: new level, refreshed vitals (HP/MP/stamina),
/// and (for the local player) remaining stat points and rank-XP progress. Fixed 48-byte block.
/// spec: Docs/RE/packets/5-32_level_up.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 32)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgLevelUp
{
    /// <summary>Packed opcode 0x50020 (5/32). spec: packets/5-32_level_up.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgLevelUp;

    /// <summary>Declared wire size in bytes. spec: packets/5-32_level_up.yaml (size: 48).</summary>
    public const int WireSize = 48;

    /// <summary>0x00 — actor sort. spec: packets/5-32_level_up.yaml.</summary>
    public readonly byte Sort;

    /// <summary>0x01 — alignment padding. spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;
    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — actor id (LE u32). spec: packets/5-32_level_up.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — new level (LE u16). HIGH CONFIDENCE. spec: packets/5-32_level_up.yaml.</summary>
    public readonly ushort NewLevel;

    /// <summary>0x0a — alignment padding to the next dword. spec: same (Pad1: bytes[2]).</summary>
    private readonly byte _pad1_0;
    private readonly byte _pad1_1;

    /// <summary>0x0c — remaining allocatable stat points (local player). spec: same.</summary>
    public readonly int RemainingStatPoints;

    /// <summary>0x10 — secondary value stored alongside (local player). spec: same.</summary>
    public readonly int Value;

    /// <summary>
    /// 0x14 — current HP and current MP packed as two i32 halves into one i64 (the handler stores
    /// this as a single 8-byte value). HIGH CONFIDENCE for the HP/MP core. spec: same.
    /// </summary>
    public readonly long HpMpPacked;

    /// <summary>0x1c — current stamina. HIGH CONFIDENCE. spec: packets/5-32_level_up.yaml.</summary>
    public readonly int Stamina;

    /// <summary>0x20 — tail word of the rank-XP region; read but not decoded. PROVISIONAL. spec: same (Tail20: bytes[4]).</summary>
    public readonly Tail20Buffer Tail20;

    /// <summary>0x24 — rank-XP within the current rank (local player). PROVISIONAL packing. spec: same.</summary>
    public readonly long RankXpWithin;

    /// <summary>0x2c — high tail of the rank-XP region. PROVISIONAL. spec: same (RankXpTailHi: bytes[4]).</summary>
    public readonly RankXpTailHiBuffer RankXpTailHi;

    /// <summary>Typed view over <see cref="Sort"/>. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)Sort;

    /// <summary>0x20 — 4-byte rank-XP tail word. spec: packets/5-32_level_up.yaml.</summary>
    [System.Runtime.CompilerServices.InlineArray(4)]
    public struct Tail20Buffer
    {
        private byte _element0;
    }

    /// <summary>0x2c — 4-byte rank-XP high tail. spec: packets/5-32_level_up.yaml.</summary>
    [System.Runtime.CompilerServices.InlineArray(4)]
    public struct RankXpTailHiBuffer
    {
        private byte _element0;
    }
}
