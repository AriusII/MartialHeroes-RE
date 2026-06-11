namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One record from <c>data/script/exp.scr</c> — EXP required per level.
/// Stride: 20 bytes. Only fields at confirmed offsets are typed; remaining 10 bytes are exposed raw.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.3 exp.scr — "stride: 20 bytes": CONFIRMED.
/// No file header; record count = file_size / 20.
/// spec: Docs/RE/formats/config_tables.md §2.1 — "No file header, no record-count prefix": CONFIRMED.
/// </remarks>
public sealed class ExpCurveEntry
{
    /// <summary>
    /// Level index, 1-based. Map key.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — Level index u16 @ +0: CONFIRMED.
    /// </summary>
    public required ushort Level { get; init; }

    /// <summary>
    /// EXP column A (feed separate runtime ladder; which column is "to next level" is UNVERIFIED).
    /// spec: Docs/RE/formats/config_tables.md §2.3 — EXP column 0 u32 @ +2: CONFIRMED.
    /// </summary>
    public required uint ColumnA { get; init; }

    /// <summary>
    /// EXP column B.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — EXP column 1 u32 @ +6: CONFIRMED.
    /// </summary>
    public required uint ColumnB { get; init; }

    /// <summary>
    /// Remaining 10 bytes of the 20-byte record (offsets +10 to +19). Internal meaning UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — "Remaining fields UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> RawTail { get; init; }
}

/// <summary>
/// One record from <c>data/script/userlevel.scr</c> — base stat values per level.
/// Stride: 60 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — "stride: 60 bytes": CONFIRMED.
/// </remarks>
public sealed class LevelBaseEntry
{
    /// <summary>
    /// Level index (map key).
    /// spec: Docs/RE/formats/config_tables.md §2.4 — Level index u16 @ +0: CONFIRMED.
    /// </summary>
    public required ushort Level { get; init; }

    /// <summary>
    /// Body bytes at offsets +2 to +59 (58 bytes). Stat field names, types, order: UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — "+2: 58 bytes, stat base values, UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }
}

/// <summary>
/// One record from <c>data/script/userpoint.scr</c> — stat allocation curve.
/// Stride: 32 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.5 userpoint.scr — "stride: 32 bytes": CONFIRMED.
/// </remarks>
public sealed class UserPointEntry
{
    /// <summary>
    /// Point index (map key).
    /// spec: Docs/RE/formats/config_tables.md §2.5 — u16 key @ +0: CONFIRMED.
    /// </summary>
    public required ushort Key { get; init; }

    /// <summary>
    /// Remaining 30 bytes. Curve values; internal layout UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+2: 30 bytes, curve values, UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }
}

/// <summary>
/// Entire <c>data/script/users.scr</c> read as a single 496-byte opaque block.
/// Internal layout and the (10/A)*B stat-ratio formula offsets are UNVERIFIED.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.6 users.scr — "496-byte bulk block": CONFIRMED (size only).
/// </remarks>
public sealed class UsersBlock
{
    /// <summary>
    /// Fixed size of the users.scr file.
    /// spec: Docs/RE/formats/config_tables.md §2.6 — "496-byte bulk block": CONFIRMED.
    /// </summary>
    public const int FixedSize = 496;

    /// <summary>
    /// Raw opaque data. Internal layout UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.6 — "internal layout UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> RawData { get; init; }
}

/// <summary>
/// One record from <c>data/script/items.scr</c> — item catalogue.
/// Main record stride: 548 bytes (0x224). May be followed by N × 8 trailing sub-entries.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.7 items.scr — "stride: 548 bytes + N×8 trailing": CONFIRMED.
/// </remarks>
public sealed class ItemCatalogEntry
{
    // The item ID width (u16 or u32) is UNVERIFIED.
    // spec: Docs/RE/formats/config_tables.md §2.7 — "item ID at +0: exact size UNVERIFIED".
    // We expose the first 2 bytes as u16 (confirmed position, unconfirmed width).

    /// <summary>
    /// Raw main record body (548 bytes). The full 548-byte record is exposed because the majority
    /// of fields have UNVERIFIED layouts. Only the confirmed fields below are additionally exposed.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "All other offsets: UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> RawRecord { get; init; }

    /// <summary>
    /// Sub-type flag at record offset +0xD2.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "+0xD2 u8 Sub-type flag": CONFIRMED.
    /// </summary>
    public required byte SubTypeFlag { get; init; }

    /// <summary>
    /// Category flag 1 at offset +0xE5. Value 1 = weapon.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "+0xE5 u8 Category flag 1 (1=weapon)": CONFIRMED.
    /// </summary>
    public required byte CategoryFlag1 { get; init; }

    /// <summary>
    /// Category flag 2 at offset +0xE6. Value 1 = armour.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "+0xE6 u8 Category flag 2 (1=armour)": CONFIRMED.
    /// </summary>
    public required byte CategoryFlag2 { get; init; }

    /// <summary>
    /// Category flag 3 at offset +0xE7. Value 1 = type-11.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "+0xE7 u8 Category flag 3 (1=type-11)": CONFIRMED.
    /// </summary>
    public required byte CategoryFlag3 { get; init; }

    /// <summary>
    /// Category flag 4 at offset +0xE8. Value 1 = type-16.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "+0xE8 u8 Category flag 4 (1=type-16)": CONFIRMED.
    /// </summary>
    public required byte CategoryFlag4 { get; init; }

    /// <summary>
    /// Count of trailing 8-byte sub-entries at offset +0x220.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "+0x220 u8 Trailing entry count N": CONFIRMED.
    /// </summary>
    public required byte TrailingCount { get; init; }

    /// <summary>
    /// Trailing upgrade/effect sub-entries, each 8 bytes. All fields UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.7 — "trailing N×8 bytes, all fields UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte>[] TrailingEntries { get; init; }
}

/// <summary>
/// One record from <c>data/script/skills.scr</c> — skill catalogue.
/// Main record stride: 1504 bytes (0x5E0). May be followed by N × 8 trailing sub-entries.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — "stride: 1504 bytes + N×8 trailing": CONFIRMED.
/// </remarks>
public sealed class SkillCatalogEntry
{
    /// <summary>
    /// Raw main record body (1504 bytes). The body field layout is UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.8 — "+0 to +0x5DF: main skill data UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> RawRecord { get; init; }

    /// <summary>
    /// Count of trailing 8-byte sub-entries at record offset +0x5E0.
    /// spec: Docs/RE/formats/config_tables.md §2.8 — "+0x5E0 u8 Trailing entry count N": CONFIRMED.
    /// </summary>
    public required byte TrailingCount { get; init; }

    /// <summary>
    /// Trailing sub-entries, each 8 bytes. All fields UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.8 — "trailing N×8 bytes, all fields UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte>[] TrailingEntries { get; init; }
}

/// <summary>
/// One record from <c>data/script/mobs.scr</c> — mob catalogue.
/// Stride: 488 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr — "stride: 488 bytes": CONFIRMED.
/// </remarks>
public sealed class MobCatalogEntry
{
    /// <summary>
    /// Mob ID (map key).
    /// spec: Docs/RE/formats/config_tables.md §2.9 — Mob ID u16 @ +0: CONFIRMED.
    /// </summary>
    public required ushort Id { get; init; }

    /// <summary>
    /// Mob type byte. Value 11 = boss / elite.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type (11=boss/elite)": CONFIRMED.
    /// </summary>
    public required byte Type { get; init; }

    /// <summary>
    /// Complete raw 488-byte record.
    /// Fields between confirmed offsets are UNVERIFIED; raw record exposed for future analysis.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "internal layout: majority UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}