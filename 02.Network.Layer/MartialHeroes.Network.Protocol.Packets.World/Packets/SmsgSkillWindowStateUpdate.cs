// spec: Docs/RE/packets/4-102_buff_state.yaml — opcode 4/102 (0x40066), 476-byte fixed block.
//
// VERIFICATION: routing + size (476 bytes = 0x1DC) + record stride (30 x 12 bytes starting at
// offset 116) are CODE-CONFIRMED. Per-record 12-byte field roles are CAPTURE-UNVERIFIED (two
// competing interpretations: {id,X,Y} vs {id,?,duration,stack,flag} — see spec notes).
// StatBlock sub-offsets are best-effort; modelled opaque. spec: Docs/RE/packets/4-102_buff_state.yaml.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     4/102 — full skill/state-window snapshot. Carries a 116-byte player stat block (level, class,
///     primary stats, HP/MP, counters) followed by 30 active-buff records of 12 bytes each
///     (30 x 12 = 360, total = 476). The handler clears all 30 buff slots then re-shows active ones
///     (server owns slot assignment). Fixed 476-byte payload.
///     spec: Docs/RE/packets/4-102_buff_state.yaml. CAPTURE-UNVERIFIED field roles.
/// </summary>
[PacketOpcode(4, 102)] // spec: Docs/RE/packets/4-102_buff_state.yaml (opcode 4/102 = 0x40066)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillWindowStateUpdate
{
    /// <summary>Packed opcode 0x40066 (4/102). spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgSkillWindowStateUpdate;

    /// <summary>
    ///     Declared wire size in bytes (116-byte StatBlock + 30 x 12-byte buff records = 476).
    ///     spec: Docs/RE/packets/4-102_buff_state.yaml (size: 476).
    /// </summary>
    public const int WireSize = 476; // spec: Docs/RE/packets/4-102_buff_state.yaml

    /// <summary>Size of the opaque stat block at the head of the payload. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public const int StatBlockSize = 116; // spec: Docs/RE/packets/4-102_buff_state.yaml

    /// <summary>Number of fixed 12-byte buff records that follow the stat block. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public const int BuffRecordCount = 30; // spec: Docs/RE/packets/4-102_buff_state.yaml

    /// <summary>Stride of one buff record in bytes. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public const int BuffRecordStride = 12; // spec: Docs/RE/packets/4-102_buff_state.yaml

    /// <summary>
    ///     0x000 — opaque 116-byte player stat block (level, class, primary stats, HP/MP, counters).
    ///     Sub-offsets are best-effort; modelled opaque to reserve the correct span.
    ///     spec: Docs/RE/packets/4-102_buff_state.yaml (StatBlock: bytes[116]).
    /// </summary>
    public readonly StatBlockBuffer StatBlock;

    // --- 30 active-buff records, 12 bytes each, base at payload offset 116 (0x074) ---
    // Per-record layout: {u32 buffId @+0, i32 paramX @+4, i32 paramY @+8}.
    // buffId == 0 = empty slot (skipped). buffId <= 80 = buff cell; > 80 = state/debuff cell.
    // CAPTURE-UNVERIFIED: competing interpretation {u16 id, u16, u32 duration, u16 stack, u8 flag, u8}.
    // spec: Docs/RE/packets/4-102_buff_state.yaml.

    /// <summary>Buff slot 0 catalog id; 0 = empty. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff00Id;

    /// <summary>
    ///     Buff slot 0 param X (icon source X or duration per competing reading). spec:
    ///     Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    public readonly int Buff00X;

    /// <summary>
    ///     Buff slot 0 param Y (icon source Y or stack/flag per competing reading). spec:
    ///     Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    public readonly int Buff00Y;

    /// <summary>Buff slot 1 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff01Id;

    public readonly int Buff01X;
    public readonly int Buff01Y;

    /// <summary>Buff slot 2 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff02Id;

    public readonly int Buff02X;
    public readonly int Buff02Y;

    /// <summary>Buff slot 3 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff03Id;

    public readonly int Buff03X;
    public readonly int Buff03Y;

    /// <summary>Buff slot 4 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff04Id;

    public readonly int Buff04X;
    public readonly int Buff04Y;

    /// <summary>Buff slot 5 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff05Id;

    public readonly int Buff05X;
    public readonly int Buff05Y;

    /// <summary>Buff slot 6 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff06Id;

    public readonly int Buff06X;
    public readonly int Buff06Y;

    /// <summary>Buff slot 7 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff07Id;

    public readonly int Buff07X;
    public readonly int Buff07Y;

    /// <summary>Buff slot 8 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff08Id;

    public readonly int Buff08X;
    public readonly int Buff08Y;

    /// <summary>Buff slot 9 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff09Id;

    public readonly int Buff09X;
    public readonly int Buff09Y;

    /// <summary>Buff slot 10 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff10Id;

    public readonly int Buff10X;
    public readonly int Buff10Y;

    /// <summary>Buff slot 11 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff11Id;

    public readonly int Buff11X;
    public readonly int Buff11Y;

    /// <summary>Buff slot 12 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff12Id;

    public readonly int Buff12X;
    public readonly int Buff12Y;

    /// <summary>Buff slot 13 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff13Id;

    public readonly int Buff13X;
    public readonly int Buff13Y;

    /// <summary>Buff slot 14 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff14Id;

    public readonly int Buff14X;
    public readonly int Buff14Y;

    /// <summary>Buff slot 15 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff15Id;

    public readonly int Buff15X;
    public readonly int Buff15Y;

    /// <summary>Buff slot 16 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff16Id;

    public readonly int Buff16X;
    public readonly int Buff16Y;

    /// <summary>Buff slot 17 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff17Id;

    public readonly int Buff17X;
    public readonly int Buff17Y;

    /// <summary>Buff slot 18 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff18Id;

    public readonly int Buff18X;
    public readonly int Buff18Y;

    /// <summary>Buff slot 19 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff19Id;

    public readonly int Buff19X;
    public readonly int Buff19Y;

    /// <summary>Buff slot 20 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff20Id;

    public readonly int Buff20X;
    public readonly int Buff20Y;

    /// <summary>Buff slot 21 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff21Id;

    public readonly int Buff21X;
    public readonly int Buff21Y;

    /// <summary>Buff slot 22 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff22Id;

    public readonly int Buff22X;
    public readonly int Buff22Y;

    /// <summary>Buff slot 23 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff23Id;

    public readonly int Buff23X;
    public readonly int Buff23Y;

    /// <summary>Buff slot 24 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff24Id;

    public readonly int Buff24X;
    public readonly int Buff24Y;

    /// <summary>Buff slot 25 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff25Id;

    public readonly int Buff25X;
    public readonly int Buff25Y;

    /// <summary>Buff slot 26 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff26Id;

    public readonly int Buff26X;
    public readonly int Buff26Y;

    /// <summary>Buff slot 27 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff27Id;

    public readonly int Buff27X;
    public readonly int Buff27Y;

    /// <summary>Buff slot 28 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff28Id;

    public readonly int Buff28X;
    public readonly int Buff28Y;

    /// <summary>Buff slot 29 catalog id. spec: Docs/RE/packets/4-102_buff_state.yaml.</summary>
    public readonly uint Buff29Id;

    public readonly int Buff29X;
    public readonly int Buff29Y;

    /// <summary>
    ///     0x000 — 116-byte opaque player stat block. Sub-offsets are best-effort (see spec notes);
    ///     modelled opaque to reserve the correct span without inventing field boundaries.
    ///     spec: Docs/RE/packets/4-102_buff_state.yaml (StatBlock: bytes[116]).
    /// </summary>
    [InlineArray(116)]
    public struct StatBlockBuffer
    {
        private byte _element0;
    }
}