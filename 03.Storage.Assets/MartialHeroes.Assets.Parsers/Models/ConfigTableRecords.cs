namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One record from <c>data/script/exp.scr</c> — EXP required per level.
/// Stride: 20 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.3 exp.scr — "stride: 20 bytes": CONFIRMED.
/// No file header; record count = file_size / 20.
/// spec: Docs/RE/formats/config_tables.md §2.1 — "No file header, no record-count prefix": CONFIRMED.
/// </remarks>
public sealed class ExpCurveEntry
{
    /// <summary>Level index, 1-based. Map key. spec: §2.3 +0 u16 Level index: CONFIRMED.</summary>
    public required ushort Level { get; init; }

    /// <summary>
    /// Constant 64 (0x0040) in all 300 records. Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — "+2 u16 Constant 64: CONFIRMED (value); semantic UNVERIFIED".
    /// </summary>
    public required ushort Const64 { get; init; }

    /// <summary>
    /// Primary EXP required to reach the next level.
    /// L1=10; L50=112,284,408; plateaus at 1,999,557,415 from ~L143.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — "+4 u32 Primary EXP required: CONFIRMED".
    /// </summary>
    public required uint PrimaryExp { get; init; }

    /// <summary>
    /// Reserved / high-word extension. Zero in all 300 records. CONFIRMED.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — "+8 u32 reserved (always zero): CONFIRMED".
    /// </summary>
    public required uint Reserved { get; init; }

    /// <summary>
    /// Secondary EXP curve. Zero for L1–L73; grows to ~4.17B at L240; non-monotone at high levels.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — "+12 u32 Secondary EXP curve: CONFIRMED (present); semantic UNVERIFIED".
    /// </summary>
    public required uint SecondaryExp { get; init; }

    /// <summary>
    /// Tertiary EXP curve. Zero until ~L186; L200=8; L300=263,880.
    /// spec: Docs/RE/formats/config_tables.md §2.3 — "+16 u32 Tertiary EXP curve: CONFIRMED (present); semantic UNVERIFIED".
    /// </summary>
    public required uint TertiaryExp { get; init; }
}

/// <summary>
/// One record from <c>data/script/userlevel.scr</c> — per-level stat-scaling coefficients.
/// Stride: 60 bytes. 300 records.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — "stride: 60 bytes, 300 records": CONFIRMED.
/// Wave-8 SPEC CORRECTION: +4/+6/+8 are three u16 counters (not two u32); layout corrected below.
/// spec: Docs/RE/formats/config_tables.md §2.4 — "SPEC CORRECTION from prior version: +4/+8 each a 4-byte u32 step field is wrong; they are 2-byte u16 counters".
/// </remarks>
public sealed class LevelBaseEntry
{
    /// <summary>Level index 1-based (map key). spec: §2.4 +0 u16: CONFIRMED.</summary>
    public required ushort Level { get; init; }

    // +2 u16 always zero (alignment pad). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.4 — "+2 u16 always zero: CONFIRMED".

    /// <summary>
    /// Tier step counter A u16 @ +4. L1–L11=0; L12–L23=1; L24–L144=2; L145–L300=0.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — "+4 u16 Tier step counter A: CONFIRMED (value); name UNVERIFIED".
    /// </summary>
    public required ushort TierStepA { get; init; }

    /// <summary>
    /// Tier step counter B u16 @ +6. Mirrors TierStepA in all 300 records.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — "+6 u16 Tier step counter B (mirrors +4): CONFIRMED".
    /// </summary>
    public required ushort TierStepB { get; init; }

    /// <summary>
    /// Divisor index C u16 @ +8. L1–L11=0; L12=2; L24=3; L36–L144=4; L145–L300=0.
    /// Feeds the (10/A)×B formula grid with users.scr.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — "+8 u16 Divisor index C: CONFIRMED (value); name UNVERIFIED".
    /// </summary>
    public required ushort DivisorC { get; init; }

    // +10 u16 always zero (alignment pad). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.4 — "+10 u16 always zero: CONFIRMED".

    /// <summary>
    /// Stat-scale positive group [0..3] f32×4 @ +12. L1–L35=1.0; L36–L300=3.0.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — "+12 4×f32 positive-scale group: CONFIRMED".
    /// </summary>
    public required float[] StatScalePositive { get; init; }

    /// <summary>
    /// Stat-scale negative group [0..3] f32×4 @ +28. L1–L35=−1.0; L36–L300=−2.0.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — "+28 4×f32 negative-scale group: CONFIRMED".
    /// </summary>
    public required float[] StatScaleNegative { get; init; }

    // +44..+59: reserved 4×f32 (all 0.0). CONFIRMED (all zero).
    // spec: Docs/RE/formats/config_tables.md §2.4 — "+44 4×f32 Reserved group (all 0.0): CONFIRMED".

    /// <summary>
    /// Full 60-byte raw record. Exposes the reserved group (+44..+59) for future analysis.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — stride 60 bytes: CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    // ── Backward-compat aliases (kept so Mapping/Godot consumers don't break) ──
    // The Wave-8 spec correction renamed StepA→TierStepA, StepB→TierStepB and split u32→u16.
    // Aliases allow existing code to compile unchanged while new code uses the typed names.

    /// <summary>Alias for <see cref="TierStepA"/> — use <see cref="TierStepA"/> in new code.</summary>
    public ushort StepA => TierStepA;

    /// <summary>Alias for <see cref="TierStepB"/> — use <see cref="TierStepB"/> in new code.</summary>
    public ushort StepB => TierStepB;
}

/// <summary>
/// One record from <c>data/script/userpoint.scr</c> — stat-point allocation budget curve.
/// Stride: 32 bytes. 301 records, keys 0..300.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.5 userpoint.scr — "stride: 32 bytes, 301 records": CONFIRMED.
/// Wave-8 SPEC CORRECTION: +8 cumulative is u32 (not u16 + flag byte); +16 cumulative is also u32.
/// spec: Docs/RE/formats/config_tables.md §2.5 — "SPEC CORRECTION: +8 is u32 (cumulative exceeds 65535 at high keys)".
/// </remarks>
public sealed class UserPointEntry
{
    /// <summary>Allocation step index 0-based (0..300). Map key. spec: §2.5 +0 u16: CONFIRMED.</summary>
    public required ushort Key { get; init; }

    /// <summary>
    /// Constant 25 in all 301 records. Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+2 u16 constant=25: CONFIRMED (value); semantic UNVERIFIED".
    /// </summary>
    public required ushort Const25 { get; init; }

    /// <summary>
    /// Stat-group-1 gain at this step. key=0→5; key=1..284→3; key=285..300→1000.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+4 u16 Stat-group-1 gain: CONFIRMED".
    /// </summary>
    public required ushort StatGroup1Gain { get; init; }

    // +6 u16 always zero (alignment pad). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.5 — "+6 u16 always zero: CONFIRMED".

    /// <summary>
    /// Stat-group-1 cumulative total (running sum of <see cref="StatGroup1Gain"/>).
    /// u32 — verified: value exceeds 65,535 at key≥285 (reaches ~65,960 at key=285).
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+8 u32 Stat-group-1 cumulative (u32, NOT u16): CONFIRMED".
    /// </summary>
    public required uint StatGroup1Cumulative { get; init; }

    /// <summary>
    /// Stat-group-2 gain at this step. key=0→7; key=9→2; key=300→300.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+12 u16 Stat-group-2 gain: CONFIRMED".
    /// </summary>
    public required ushort StatGroup2Gain { get; init; }

    // +14 u16 always zero (alignment pad). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.5 — "+14 u16 always zero: CONFIRMED".

    /// <summary>
    /// Stat-group-2 cumulative total (running sum of <see cref="StatGroup2Gain"/>). u32.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+16 u32 Stat-group-2 cumulative: CONFIRMED".
    /// </summary>
    public required uint StatGroup2Cumulative { get; init; }

    /// <summary>
    /// Secondary curve — low word u16 @ +20. key=0..5→0; key=6→282; ~+3/step; plateaus ~key150.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+20 u16 secondary curve low: CONFIRMED".
    /// </summary>
    public required ushort SecondaryCurveLow { get; init; }

    /// <summary>
    /// Secondary curve — high word u16 @ +22. key=0..5→0; key=6→20; grows slowly; plateaus ~56 by key150.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+22 u16 secondary curve high: CONFIRMED".
    /// </summary>
    public required ushort SecondaryCurveHigh { get; init; }

    /// <summary>
    /// Tertiary value 1 u32 @ +24. key=0..295→0; key=296→235,000; key=300→255,000.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+24 u32 Tertiary value 1: CONFIRMED".
    /// </summary>
    public required uint TertiaryValue1 { get; init; }

    /// <summary>
    /// Tertiary value 2 u32 @ +28. Same pattern as TertiaryValue1.
    /// spec: Docs/RE/formats/config_tables.md §2.5 — "+28 u32 Tertiary value 2: CONFIRMED".
    /// </summary>
    public required uint TertiaryValue2 { get; init; }

    /// <summary>Full 32-byte raw record. spec: §2.5 stride 32 B: CONFIRMED.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }
}

/// <summary>
/// One 124-byte per-class block from <c>data/script/users.scr</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.6 — "4 × 124-byte class blocks": CONFIRMED.
/// </remarks>
public sealed class UsersClassBlock
{
    /// <summary>
    /// Class ID (1..4). Block 0→1, block 1→2, block 2→3, block 3→4.
    /// spec: §2.6 — ClassId u8 @ block+0: CONFIRMED.
    /// </summary>
    public required byte ClassId { get; init; }

    /// <summary>
    /// Stat group A: 3×f32 @ +4. Values 3.0, 3.0, 3.0 for all classes.
    /// spec: §2.6 — +4 3×f32 Stat group A (all 3.0): CONFIRMED.
    /// </summary>
    public required float[] StatGroupA { get; init; }

    /// <summary>
    /// Class-specific ratio group: 8×f32 @ +92. Mostly 1.0 with class-specific deviations.
    /// spec: §2.6 — +92 8×f32 class-specific ratio group: CONFIRMED.
    /// </summary>
    public required float[] ClassSpecificRatios { get; init; }

    /// <summary>Full 124-byte raw block. spec: §2.6 block stride 124 B: CONFIRMED.</summary>
    public required ReadOnlyMemory<byte> RawBlock { get; init; }
}

/// <summary>
/// Decoded <c>data/script/users.scr</c> — character class stat grid.
/// 496 bytes = 4 × 124-byte class blocks.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.6 users.scr — "496-byte bulk block (4 × 124-byte class blocks)": CONFIRMED.
/// </remarks>
public sealed class UsersBlock
{
    /// <summary>Fixed file size in bytes. spec: §2.6 — "496 bytes": CONFIRMED.</summary>
    public const int FixedSize = 496; // 4 × 124

    /// <summary>Per-class block size. spec: §2.6 — "124 bytes per block": CONFIRMED.</summary>
    public const int ClassBlockSize = 124;

    /// <summary>
    /// Four class blocks in file order. Class IDs 1-4.
    /// spec: §2.6 — "4 sequential 124-byte class blocks": CONFIRMED.
    /// </summary>
    public required UsersClassBlock[] ClassBlocks { get; init; }

    /// <summary>Full 496-byte raw data.</summary>
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
    /// Mob level. -1 = not set; 0 = trivial; boss range 36..46 for ID range 14000-14009.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "+244 i32 Mob level: CONFIRMED (boss validation path)".
    /// </summary>
    public required int MobLevel { get; init; }

    /// <summary>
    /// Spawn timer in seconds. Range 33..41006 in sample; boss default ~40 s.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "+248 u32 Spawn timer (seconds): CONFIRMED (plausible range)".
    /// </summary>
    public required uint SpawnTimer { get; init; }

    /// <summary>
    /// Complete raw 488-byte record.
    /// Fields between confirmed offsets are UNVERIFIED; raw record exposed for future analysis.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "internal layout: majority UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .do file models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/textcommand.do</c>. Stride: 52 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §3.1: CONFIRMED (all 28 records decoded).
/// </remarks>
public sealed class TextCommandRecord
{
    /// <summary>Command ID u32 @ +0 (BST key). spec: §3.1 CONFIRMED.</summary>
    public required uint CommandId { get; init; }

    /// <summary>Command name CP949 char[36] @ +4. spec: §3.1 CONFIRMED.</summary>
    public required string CommandName { get; init; }

    /// <summary>Argument flag u8 @ +44. 0=no arg; 1=player-name arg. spec: §3.1 CONFIRMED (pattern).</summary>
    public required byte ArgumentFlag { get; init; }

    /// <summary>Sub-command ID u32 @ +48. Non-zero for emote/action. spec: §3.1 CONFIRMED (pattern).</summary>
    public required uint SubCommandId { get; init; }

    /// <summary>Full 52-byte raw record.</summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}

/// <summary>
/// One record from <c>data/script/emoticon.do</c>. Stride: 40 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §3.2: CONFIRMED (all 21 records).
/// </remarks>
public sealed class EmoticonRecord
{
    /// <summary>Emote ID u32 @ +0 (primary BST key). spec: §3.2 CONFIRMED.</summary>
    public required uint EmoteId { get; init; }

    /// <summary>Category flag u8 @ +4. spec: §3.2 CONFIRMED (pattern); semantic UNVERIFIED.</summary>
    public required byte CategoryFlag { get; init; }

    /// <summary>Secondary key u32 @ +8. spec: §3.2 CONFIRMED.</summary>
    public required uint SecondaryKey { get; init; }

    /// <summary>Action link u32 @ +12. spec: §3.2 CONFIRMED (pattern); name UNVERIFIED.</summary>
    public required uint ActionLink { get; init; }

    /// <summary>Full 40-byte raw record (includes all fields at +16..+36 which are UNVERIFIED).</summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}

/// <summary>
/// One record from <c>data/script/msginfo.do</c>. Stride: 128 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §3.3: CONFIRMED (all 14 sample records decoded, CP949 confirmed).
/// </remarks>
public sealed class MsgInfoRecord
{
    /// <summary>Message ID u32 @ +0 (BST key). spec: §3.3 CONFIRMED.</summary>
    public required uint MessageId { get; init; }

    /// <summary>Dialog flag u32 @ +4. spec: §3.3 CONFIRMED (pattern); semantic UNVERIFIED.</summary>
    public required uint DialogFlag { get; init; }

    /// <summary>Text line 1 CP949 char[60] @ +8. spec: §3.3 CONFIRMED.</summary>
    public required string TextLine1 { get; init; }

    /// <summary>Text line 2 CP949 char[60] @ +68. spec: §3.3 CONFIRMED.</summary>
    public required string TextLine2 { get; init; }

    /// <summary>Full 128-byte raw record.</summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}

/// <summary>
/// One record from <c>data/item/items_extra.do</c>. Stride: 48 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §3.4: CONFIRMED (loader attachment code confirmed key fields).
/// </remarks>
public sealed class ItemsExtraRecord
{
    /// <summary>Item ID u32 @ +0. Top byte encodes category. 0x7FFFFFFF=sentinel. spec: §3.4 CONFIRMED.</summary>
    public required uint ItemId { get; init; }

    /// <summary>True if this is a sentinel record (ItemId == 0x7FFFFFFF). spec: §3.4 CONFIRMED.</summary>
    public required bool IsSentinel { get; init; }

    /// <summary>Animation speed scale f32 @ +4. 1.0=normal. spec: §3.4 CONFIRMED.</summary>
    public required float AnimScale { get; init; }

    /// <summary>Attachment field A i32 @ +8. Range 0..3. spec: §3.4 CONFIRMED (range); name UNVERIFIED.</summary>
    public required int AttachFieldA { get; init; }

    /// <summary>Attachment field B i32 @ +12. Range 8..48. spec: §3.4 CONFIRMED (range); name UNVERIFIED.</summary>
    public required int AttachFieldB { get; init; }

    /// <summary>Weapon bone attachment X i32 @ +16. Local space. spec: §3.4 CONFIRMED.</summary>
    public required int AttachX { get; init; }

    /// <summary>Weapon bone attachment Y i32 @ +20. spec: §3.4 CONFIRMED.</summary>
    public required int AttachY { get; init; }

    /// <summary>Weapon bone attachment Z i32 @ +24. spec: §3.4 CONFIRMED.</summary>
    public required int AttachZ { get; init; }

    /// <summary>Rotation around X axis in degrees i32 @ +28. spec: §3.4 CONFIRMED.</summary>
    public required int RotXDeg { get; init; }

    /// <summary>Rotation around Y axis in degrees i32 @ +32. spec: §3.4 CONFIRMED.</summary>
    public required int RotYDeg { get; init; }

    /// <summary>Rotation around Z axis in degrees i32 @ +36. spec: §3.4 CONFIRMED.</summary>
    public required int RotZDeg { get; init; }

    /// <summary>Fourth rotation or secondary anim param i32 @ +40. Range -185..+300. spec: §3.4 CONFIRMED (range).</summary>
    public required int Field40 { get; init; }

    /// <summary>Rarity tier u32 @ +44. Values 0..5. spec: §3.4 CONFIRMED (range); semantic INFERRED.</summary>
    public required uint RarityTier { get; init; }

    /// <summary>Full 48-byte raw record.</summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  data/script/items.csv — Item catalogue
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One data row from <c>data/script/items.csv</c>.
/// 139 columns, CP949 encoding, no header row. RFC 4180 quoting.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §4 items.csv: CONFIRMED.
/// Only columns with CONFIRMED or HIGH confidence are exposed as typed properties.
/// All 139 raw string columns are accessible via <see cref="RawColumns"/>.
/// <c>\\</c> inside string fields encodes an in-game newline (preserved verbatim).
/// spec: Docs/RE/formats/config_tables.md §4.1 — "in-game newline escape: \\": CONFIRMED.
/// </remarks>
public sealed class ItemCsvRow
{
    // ── Identity (cols 0–6) ─────────────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 0–6: CONFIRMED.

    /// <summary>Item display name (CP949). col0. CONFIRMED.</summary>
    public required string NameCp949 { get; init; }

    /// <summary>Item ID. col1. CONFIRMED.</summary>
    public required uint ItemId { get; init; }

    /// <summary>Item description (CP949); <c>\\</c>=in-game newline. col2. CONFIRMED.</summary>
    public required string DescriptionCp949 { get; init; }

    /// <summary>Linked/related item ID. col3. HIGH.</summary>
    public required uint LinkedItemId { get; init; }

    /// <summary>Base reference ID. col4. HIGH.</summary>
    public required uint BaseRefId { get; init; }

    /// <summary>Secondary reference ID. col5. HIGH.</summary>
    public required uint SecondaryRefId { get; init; }

    /// <summary>Item subtype / category code. col6. CONFIRMED.</summary>
    public required uint ItemSubtype { get; init; }

    // ── Flags and meta (cols 7–18) ──────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 7–18.

    /// <summary>Bonus flag A. col7. HIGH.</summary>
    public required byte BonusFlagA { get; init; }

    /// <summary>Bonus flag B. col8. HIGH.</summary>
    public required byte BonusFlagB { get; init; }

    /// <summary>Enhancement slot count. col10. HIGH.</summary>
    public required byte EnhancementSize { get; init; }

    /// <summary>NPC sell price (gold). col16. CONFIRMED.</summary>
    public required uint SellPrice { get; init; }

    /// <summary>1 = purchaseable from NPC. col17. HIGH.</summary>
    public required byte NpcPurchaseable { get; init; }

    /// <summary>1 = item is enabled/active. col18. CONFIRMED.</summary>
    public required byte Enabled { get; init; }

    // ── Stacking, tier, durability (cols 19–23) ─────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 19–23.

    /// <summary>Maximum stack size. col19. CONFIRMED.</summary>
    public required ushort MaxStack { get; init; }

    /// <summary>Item tier / quality rank. col22. CONFIRMED.</summary>
    public required ushort ItemTierRank { get; init; }

    /// <summary>Maximum durability. col23. HIGH.</summary>
    public required ushort MaxDurability { get; init; }

    // ── Required stats (cols 24–28) ─────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 24–28: CONFIRMED.

    /// <summary>Required Strength. col24. CONFIRMED.</summary>
    public required ushort ReqStr { get; init; }

    /// <summary>Required Constitution. col25. CONFIRMED.</summary>
    public required ushort ReqCon { get; init; }

    /// <summary>Required Agility. col26. CONFIRMED.</summary>
    public required ushort ReqAgi { get; init; }

    /// <summary>Required Intelligence. col27. CONFIRMED.</summary>
    public required ushort ReqInt { get; init; }

    /// <summary>Required Chi. col28. CONFIRMED.</summary>
    public required ushort ReqChi { get; init; }

    // ── Class restriction flags (cols 29–32) ────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 29–32: CONFIRMED.

    /// <summary>Class Yi can equip (1=yes). col29. CONFIRMED.</summary>
    public required byte ClassYi { get; init; }

    /// <summary>Class Ye can equip (1=yes). col30. CONFIRMED.</summary>
    public required byte ClassYe { get; init; }

    /// <summary>Class In can equip (1=yes). col31. CONFIRMED.</summary>
    public required byte ClassIn { get; init; }

    /// <summary>Class Ji can equip (1=yes). col32. CONFIRMED.</summary>
    public required byte ClassJi { get; init; }

    // ── Enchant and socket block (cols 47–48) ───────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 47–48: CONFIRMED.

    /// <summary>Current enchant level. col47. CONFIRMED.</summary>
    public required byte EnchantLevel { get; init; }

    /// <summary>Gem / socket power. col48. CONFIRMED.</summary>
    public required byte GemPower { get; init; }

    // ── Bonus stat block A (cols 64–68) ─────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 64–68: CONFIRMED.

    /// <summary>Bonus attack. col64. CONFIRMED.</summary>
    public required uint BonusAtk { get; init; }

    /// <summary>Bonus HP. col65. CONFIRMED.</summary>
    public required uint BonusHp { get; init; }

    /// <summary>Bonus extended attack. col68. CONFIRMED.</summary>
    public required uint BonusExtAtk { get; init; }

    // ── Float rate block (cols 75, 78) ──────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 75, 78: CONFIRMED.

    /// <summary>Attack speed multiplier. col75. CONFIRMED.</summary>
    public required float AttackSpeed { get; init; }

    /// <summary>Dodge rate. col78. CONFIRMED.</summary>
    public required float DodgeRate { get; init; }

    // ── Bonus stat block B (cols 84–96) ─────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 84–96: CONFIRMED.

    /// <summary>Bonus Chi. col84. CONFIRMED.</summary>
    public required uint BonusChi { get; init; }

    /// <summary>Weapon stat A. col85. CONFIRMED.</summary>
    public required uint WeaponStatA { get; init; }

    /// <summary>Weapon stat B. col86. CONFIRMED.</summary>
    public required uint WeaponStatB { get; init; }

    /// <summary>Minimum attack value. col87. CONFIRMED.</summary>
    public required uint MinAttack { get; init; }

    /// <summary>Maximum attack value. col90. CONFIRMED.</summary>
    public required uint MaxAttack { get; init; }

    /// <summary>Bonus defense A. col93. CONFIRMED.</summary>
    public required uint BonusDefenseA { get; init; }

    /// <summary>Physical defense. col94. CONFIRMED.</summary>
    public required uint PhysDefense { get; init; }

    /// <summary>Armor defense. col96. CONFIRMED.</summary>
    public required uint ArmorDefense { get; init; }

    // ── Consumable block (cols 112–131) ─────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 112–131: CONFIRMED.

    /// <summary>Duration in minutes (consumable). col112. CONFIRMED.</summary>
    public required uint DurationMinutes { get; init; }

    /// <summary>Expire mode. col113. CONFIRMED.</summary>
    public required byte ExpireMode { get; init; }

    /// <summary>Consumable effect value. col119. CONFIRMED.</summary>
    public required uint ConsumableValue { get; init; }

    /// <summary>1 = item is consumable. col120. CONFIRMED.</summary>
    public required byte IsConsumable { get; init; }

    /// <summary>Gem category. col127. CONFIRMED.</summary>
    public required byte GemCategory { get; init; }

    /// <summary>1 = equippable. col128. CONFIRMED.</summary>
    public required byte EquippableFlag { get; init; }

    /// <summary>1 = has attached effect. col129. CONFIRMED.</summary>
    public required byte HasEffect { get; init; }

    /// <summary>Effect type code. col130. CONFIRMED.</summary>
    public required byte EffectType { get; init; }

    /// <summary>Effect strength. col131. CONFIRMED.</summary>
    public required ushort EffectStrength { get; init; }

    // ── Model / visual IDs (cols 117–118) ───────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 columns 117–118: CONFIRMED.

    /// <summary>Model set ID (references mesh/texture set). col117. CONFIRMED.</summary>
    public required ushort ModelSetId { get; init; }

    /// <summary>Model type / slot code. col118. CONFIRMED.</summary>
    public required byte ModelType { get; init; }

    // ── Raw access ───────────────────────────────────────────────────────────

    /// <summary>
    /// All 139 columns as raw decoded strings (0-indexed), for consumers that need
    /// columns not yet decoded to typed properties.
    /// spec: Docs/RE/formats/config_tables.md §4.1 — "139 columns (0-based 0..138)": CONFIRMED.
    /// </summary>
    public required string[] RawColumns { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  data/script/npc.scr  NPC description-text table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/npc.scr</c>. Stride: 404 bytes (0x194).
/// 2510 records; sequential u32 id at +0 mirrored at +4.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.17.3 npc.scr: SAMPLE-VERIFIED.
/// No header; record count = file_size / 404. All strings CP949, null-terminated.
/// </remarks>
public sealed class NpcScrRecord
{
    /// <summary>
    /// NPC class/descriptor id (map key). Sequential 1..2510.
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — id u32 @ 0x000: SAMPLE-VERIFIED.
    /// </summary>
    public required uint Id { get; init; }

    /// <summary>
    /// Id mirror — equals Id in all records (possible second lookup key).
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — id_mirror u32 @ 0x004: SAMPLE-VERIFIED.
    /// </summary>
    public required uint IdMirror { get; init; }

    /// <summary>
    /// Description paragraph 0 (CP949, first archetype paragraph).
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — paragraph_0 char[] @ 0x014: SAMPLE-VERIFIED.
    /// </summary>
    public required string Paragraph0 { get; init; }

    /// <summary>
    /// Description paragraph 1 (CP949, second paragraph).
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — paragraph_1 char[] @ 0x050: SAMPLE-VERIFIED.
    /// </summary>
    public required string Paragraph1 { get; init; }

    /// <summary>
    /// Description paragraph 2 (CP949, third paragraph).
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — paragraph_2 char[] @ 0x090: SAMPLE-VERIFIED.
    /// </summary>
    public required string Paragraph2 { get; init; }

    /// <summary>Full 404-byte raw record for future field analysis.</summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  data/script/quests.scr  Quest template catalogue
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One occupied slot from <c>data/script/quests.scr</c>. Stride: 3720 bytes (0xE88).
/// 488 total slots; 122 occupied (leading u16 quest_id != 0).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.17.1 quests.scr: SAMPLE-VERIFIED.
/// Sparse flat array: slot is empty when quest_id == 0. Runtime keys a map on non-zero ids (range 1..617).
/// </remarks>
public sealed class QuestScrRecord
{
    /// <summary>
    /// Quest id (map key). 1..617; 0 = empty slot (never returned by the parser).
    /// spec: Docs/RE/formats/config_tables.md §2.17.1 — quest_id u16 @ 0x000: SAMPLE-VERIFIED.
    /// </summary>
    public required ushort QuestId { get; init; }

    /// <summary>
    /// Quest name (CP949, null-terminated within ~62-byte name buffer ending by ~0x3F).
    /// spec: Docs/RE/formats/config_tables.md §2.17.1 — quest_name char[] @ 0x002: SAMPLE-VERIFIED.
    /// </summary>
    public required string QuestName { get; init; }

    /// <summary>Full 3720-byte raw slot for future field analysis.</summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}