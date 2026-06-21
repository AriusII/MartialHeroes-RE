using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parsers for binary <c>.scr</c> client configuration catalogue files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/config_tables.md §2. .scr and .do files (binary record catalogues)
///     <para>
///         Common structural pattern for all .scr files:
///         - No file header, no record-count prefix.
///         - Record count = file_size / record_stride.
///         - Flat array of fixed-size records concatenated without inter-record padding.
///         - First field of every record is a u16 identifier (the map key).
///         spec: Docs/RE/formats/config_tables.md §2.1 Common structural pattern: CONFIRMED.
///     </para>
///     <para>
///         ZERO rendering/engine dependencies.
///     </para>
/// </remarks>
public static class ConfigTableParser
{
    // =========================================================================
    // exp.scr — EXP per level
    // =========================================================================

    // Stride: 20 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.3 exp.scr — "stride: 20 bytes": CONFIRMED.
    private const int ExpScrStride = 20;

    // =========================================================================
    // userlevel.scr — base stat values per level
    // =========================================================================

    // Stride: 60 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — "stride: 60 bytes": CONFIRMED.
    private const int UserLevelScrStride = 60;

    // =========================================================================
    // userpoint.scr — stat allocation curve
    // =========================================================================

    // Stride: 32 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.5 userpoint.scr — "stride: 32 bytes": CONFIRMED.
    private const int UserPointScrStride = 32;

    // =========================================================================
    // items.scr — RETIRED (CAMPAIGN 11 Phase 3a)
    // =========================================================================
    // ParseItemsScr / ItemCatalogEntry were a duplicate of the canonical ItemsScrParser
    // (which decodes name/uid/desc/model_ref/anim_ref/discriminator/effects into ItemsScrRecord).
    // No production consumer in layers 04/05 used ItemCatalogEntry.
    // Use ItemsScrParser.Parse for all items.scr decoding.
    // spec: Docs/RE/formats/items_scr.md §4 — engineer guidance: canonical parser is ItemsScrParser.

    // =========================================================================
    // skills.scr — skill catalogue (1504-byte records + N×8 trailing)
    // =========================================================================

    // Main record stride: 1504 bytes (0x5E0). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — "stride: 1504 bytes": CONFIRMED.
    private const int SkillScrMainStride = 1504; // 0x5E0

    // Trailing count byte offset within the 1504-byte record.
    // spec: Docs/RE/formats/config_tables.md §2.8 —
    //   AMBIGUITY: the spec table cites "+0x5E0" for the trailing count byte, but 0x5E0 = 1504
    //   which is the same as the stated stride (1504 bytes), i.e. one past the end of the record.
    //   The most coherent reading is: body (1500 bytes) + count (1 byte) + pad (3 bytes) = 1504,
    //   so the count sits at offset 1500 (0x5DC). DBG-PENDING: confirm against a real skills.scr sample.
    private const int SkillScrTrailingCountOffset = 1500; // 0x5DC — DBG-pending sample verification
    private const int SkillScrTrailingStride = 8;

    // =========================================================================
    // mobs.scr — mob catalogue (488-byte records)
    // =========================================================================

    // Stride: 488 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr — "stride: 488 bytes": CONFIRMED.
    private const int MobScrStride = 488;

    // Confirmed field offsets within the 488-byte record.
    // spec: Docs/RE/formats/config_tables.md §2.9.
    private const int MobIdOffset = 0; // u16 mob ID. CONFIRMED.
    private const int MobTypeOffset = 324; // u8 mob type (11 = boss). CONFIRMED.
    private const int MobLevelOffset = 244; // i32 mob level; -1=not set. CONFIRMED (boss validation path).
    private const int SpawnTimerOffset = 248; // u32 spawn timer in seconds. CONFIRMED (plausible range).

    /// <summary>
    ///     Parses <c>data/script/exp.scr</c> — EXP required per level.
    ///     Record count is derived as <c>file_size / 20</c> (no header).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of EXP curve entries in on-disk order.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown if the buffer size is not a multiple of the record stride.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.3 exp.scr: CONFIRMED (stride and key fields).
    /// </remarks>
    public static ExpCurveEntry[] ParseExpScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.1 — "record count = file_size / record_stride": CONFIRMED.
        if (span.Length % ExpScrStride != 0)
            throw new InvalidDataException(
                $"exp.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {ExpScrStride}. spec: Docs/RE/formats/config_tables.md §2.3.");

        var count = span.Length / ExpScrStride;
        var results = new ExpCurveEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * ExpScrStride;
            var rec = span.Slice(recOffset, ExpScrStride);

            // Level index u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "Level index u16 @ +0: CONFIRMED".
            var level = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            // Constant 64 u16 @ +2 — identical in all 300 records. CONFIRMED (value); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+2 u16 Constant 64 (0x0040): CONFIRMED (value)".
            var const64 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // Primary EXP required u32 @ +4. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+4 u32 Primary EXP required: CONFIRMED".
            var primaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // Reserved u32 @ +8 — zero in all 300 records. CONFIRMED (always zero).
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+8 u32 Reserved (always zero): CONFIRMED".
            var reserved = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            // Secondary EXP curve u32 @ +12. CONFIRMED (present); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+12 u32 Secondary EXP curve: CONFIRMED (present)".
            var secondaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);

            // Tertiary EXP curve u32 @ +16. CONFIRMED (present); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+16 u32 Tertiary EXP curve: CONFIRMED (present)".
            var tertiaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            results[i] = new ExpCurveEntry
            {
                Level = level,
                Const64 = const64,
                PrimaryExp = primaryExp,
                Reserved = reserved,
                SecondaryExp = secondaryExp,
                TertiaryExp = tertiaryExp
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/script/userlevel.scr</c> — base stat values per level.
    ///     Record count = file_size / 60. Only the level key at +0 is decoded; body is raw.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of level base entries in on-disk order.</returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr: CONFIRMED (stride, key field).
    ///     Stat field layout: UNVERIFIED.
    /// </remarks>
    public static LevelBaseEntry[] ParseUserLevelScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.1 — "record count = file_size / record_stride": CONFIRMED.
        if (span.Length % UserLevelScrStride != 0)
            throw new InvalidDataException(
                $"userlevel.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {UserLevelScrStride}. spec: Docs/RE/formats/config_tables.md §2.4.");

        var count = span.Length / UserLevelScrStride;
        var results = new LevelBaseEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * UserLevelScrStride;
            var rec = span.Slice(recOffset, UserLevelScrStride);

            // Level index u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "Level index u16 @ +0: CONFIRMED".
            var level = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            // +2 u16 always zero (alignment pad). CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+2 u16 always zero: CONFIRMED".
            // (read and discarded to maintain correct offset arithmetic)

            // Wave-8 SPEC CORRECTION: the prior version read stepA as u32@+4 and stepB as u32@+8.
            // The corrected spec shows three u16 counters at +4/+6/+8 with a zero pad at +10.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+4 u16 Tier step counter A: CONFIRMED (value)".
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+6 u16 Tier step counter B (mirrors +4): CONFIRMED".
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+8 u16 Divisor index C: CONFIRMED (value)".
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+10 u16 always zero: CONFIRMED".
            var tierStepA = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);
            var tierStepB = BinaryPrimitives.ReadUInt16LittleEndian(rec[6..]);
            var divisorC = BinaryPrimitives.ReadUInt16LittleEndian(rec[8..]);
            // +10 u16 zero pad: consumed implicitly (float array starts at +12).

            // Stat-scale positive group [0..3] f32×4 @ +12..+27. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+12 4×f32 positive-scale group: CONFIRMED".
            var statScalePositive = new float[4];
            for (var s = 0; s < 4; s++)
                statScalePositive[s] = BinaryPrimitives.ReadSingleLittleEndian(rec[(12 + s * 4)..]);

            // Stat-scale negative group [0..3] f32×4 @ +28..+43. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+28 4×f32 negative-scale group: CONFIRMED".
            var statScaleNegative = new float[4];
            for (var s = 0; s < 4; s++)
                statScaleNegative[s] = BinaryPrimitives.ReadSingleLittleEndian(rec[(28 + s * 4)..]);

            // Reserved group 4×f32 @ +44..+59 — all 0.0. CONFIRMED (all zero).
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+44 4×f32 reserved group (all 0.0): CONFIRMED".
            var body = data.Slice(recOffset, UserLevelScrStride);

            results[i] = new LevelBaseEntry
            {
                Level = level,
                TierStepA = tierStepA,
                TierStepB = tierStepB,
                DivisorC = divisorC,
                StatScalePositive = statScalePositive,
                StatScaleNegative = statScaleNegative,
                Body = body
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/script/userpoint.scr</c> — stat allocation curve.
    ///     Record count = file_size / 32. Only the key at +0 is decoded; remainder is raw.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of user point entries in on-disk order.</returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.5 userpoint.scr: CONFIRMED (stride, key field).
    ///     Curve values: UNVERIFIED.
    /// </remarks>
    public static UserPointEntry[] ParseUserPointScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % UserPointScrStride != 0)
            throw new InvalidDataException(
                $"userpoint.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {UserPointScrStride}. spec: Docs/RE/formats/config_tables.md §2.5.");

        var count = span.Length / UserPointScrStride;
        var results = new UserPointEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * UserPointScrStride;
            var rec = span.Slice(recOffset, UserPointScrStride);

            // u16 key (0-based, 0..300) @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "u16 key @ +0: CONFIRMED".
            var key = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            // Constant 25 u16 @ +2. CONFIRMED (value); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+2 u16 constant=25: CONFIRMED (value)".
            var const25 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // Stat-group-1 gain u16 @ +4. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+4 u16 Stat-group-1 gain: CONFIRMED".
            var statGroup1Gain = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);

            // +6 u16 always zero (alignment pad). CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+6 u16 always zero: CONFIRMED".

            // Stat-group-1 cumulative u32 @ +8..+11. CONFIRMED.
            // Wave-8 SPEC CORRECTION: was u16; value exceeds 65535 at keys 285+ (reaches ~65960 at key=285).
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+8 u32 Stat-group-1 cumulative (u32): CONFIRMED".
            var statGroup1Cumul = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            // Stat-group-2 gain u16 @ +12. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+12 u16 Stat-group-2 gain: CONFIRMED".
            var statGroup2Gain = BinaryPrimitives.ReadUInt16LittleEndian(rec[12..]);

            // +14 u16 always zero (alignment pad). CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+14 u16 always zero: CONFIRMED".

            // Stat-group-2 cumulative u32 @ +16..+19. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+16 u32 Stat-group-2 cumulative: CONFIRMED".
            var statGroup2Cumul = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            // Secondary curve low u16 @ +20. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+20 u16 secondary curve low: CONFIRMED".
            var secCurveLow = BinaryPrimitives.ReadUInt16LittleEndian(rec[20..]);

            // Secondary curve high u16 @ +22. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+22 u16 secondary curve high: CONFIRMED".
            var secCurveHigh = BinaryPrimitives.ReadUInt16LittleEndian(rec[22..]);

            // Tertiary value 1 u32 @ +24. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+24 u32 Tertiary value 1: CONFIRMED".
            var tertiary1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);

            // Tertiary value 2 u32 @ +28. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+28 u32 Tertiary value 2: CONFIRMED".
            var tertiary2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[28..]);

            results[i] = new UserPointEntry
            {
                Key = key,
                Const25 = const25,
                StatGroup1Gain = statGroup1Gain,
                StatGroup1Cumulative = statGroup1Cumul,
                StatGroup2Gain = statGroup2Gain,
                StatGroup2Cumulative = statGroup2Cumul,
                SecondaryCurveLow = secCurveLow,
                SecondaryCurveHigh = secCurveHigh,
                TertiaryValue1 = tertiary1,
                TertiaryValue2 = tertiary2,
                Body = data.Slice(recOffset, UserPointScrStride)
            };
        }

        return results;
    }

    // =========================================================================
    // users.scr — character class stat grid (single 496-byte block)
    // =========================================================================

    /// <summary>
    ///     Parses <c>data/script/users.scr</c> — character class stat grid.
    ///     The ENTIRE FILE is a SINGLE 496-byte (0x1F0) structure, read in ONE read.
    ///     There is NO per-record loop and NO stride — the prior "4 × 124" and "124/124/128/120"
    ///     framings are REFUTED. The four class windows are a post-load grid-formula access pattern,
    ///     NOT an on-disk record stride.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded users block with four class windows.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown if the buffer is not exactly 496 bytes.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.6 users.scr — "single 496-byte (0x1F0) structure, one read": CONFIRMED.
    ///     CORRECTED CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):
    ///     The loader reads exactly 496 bytes in ONE read with no per-record loop.
    ///     The four-way split is a grid-formula access pattern post-load. The prior
    ///     "4 × 124-byte records" or heterogeneous "124/124/128/120" models are REFUTED.
    ///     spec: Docs/RE/formats/config_tables.md §2.6 — "CORRECTED — no per-record loop, no stride": CONFIRMED.
    /// </remarks>
    public static UsersBlock ParseUsersScr(ReadOnlyMemory<byte> data)
    {
        // The whole file is one 496-byte block — no loop, no stride.
        // spec: Docs/RE/formats/config_tables.md §2.6 — "whole file is a SINGLE fixed-size structure of 496 bytes (0x1F0)": CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §2.6 — structure size CONFIRMED (two-witness: loader reads exactly this many bytes in one read, on-disk size matches).
        if (data.Length != UsersBlock.FixedSize)
            throw new InvalidDataException(
                $"users.scr parse error: expected exactly {UsersBlock.FixedSize} bytes " +
                $"(single structure, no stride, no record loop), " +
                $"got {data.Length}. spec: Docs/RE/formats/config_tables.md §2.6.");

        var span = data.Span;

        // Extract the four class windows using grid-formula offsets.
        // Each window is 124 bytes; the grid formula supplies the window base.
        // spec: Docs/RE/formats/config_tables.md §2.6 — "post-load grid-formula access pattern, not a record stride": CONFIRMED.
        // Per-window layout (offsets relative to window base):
        //   +0: u8 class ID (1..4). CONFIRMED.
        //   +1: constant 0x13 (19). CONFIRMED (value); semantic UNVERIFIED.
        //   +2: constant 0x43 (67). CONFIRMED (value); semantic UNVERIFIED.
        //   +3: always zero (header pad). CONFIRMED.
        //   +4..+15: stat weight triplet A = (3.0, 3.0, 3.0). 3×f32. CONFIRMED.
        //   +16..+35: zero group. 5×f32. CONFIRMED (all zero).
        //   +36..+47: (7.0, 24.0, 0.0) B-input triplet #1. 3×f32. CONFIRMED.
        //   +48..+59: (7.0, 24.0, 0.0) B-input triplet #2. CONFIRMED.
        //   +60..+71: (7.0, 24.0, 0.0) B-input triplet #3. CONFIRMED.
        //   +72..+91: zero group. 5×f32. CONFIRMED (all zero).
        //   +92..+123: class-specific multiplier group. 8×f32. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §2.6 per-class window layout table: CONFIRMED.
        var classBlocks = new UsersClassBlock[4];
        for (var b = 0; b < 4; b++)
        {
            // Grid formula: window base = b × ClassBlockSize.
            // spec: Docs/RE/formats/config_tables.md §2.6 — grid-formula window base: CONFIRMED (implied by 4-class layout).
            var windowBase = b * UsersBlock.ClassBlockSize;
            var blk = span.Slice(windowBase, UsersBlock.ClassBlockSize);

            // Class ID u8 @ window+0. CONFIRMED (1..4).
            var classId = blk[0];

            // Stat group A: 3×f32 @ window+4. CONFIRMED (values 3.0, 3.0, 3.0 for all 4 classes).
            // spec: Docs/RE/formats/config_tables.md §2.6 — "+4 12 3×f32 Stat weight triplet A = (3.0, 3.0, 3.0): CONFIRMED".
            var statGroupA = new float[3];
            for (var s = 0; s < 3; s++)
                statGroupA[s] = BinaryPrimitives.ReadSingleLittleEndian(blk[(4 + s * 4)..]);

            // Class-specific multiplier group: 8×f32 @ window+92. CONFIRMED (class-specific deviations).
            // spec: Docs/RE/formats/config_tables.md §2.6 — "+92 32 8×f32 Class-specific multiplier group: CONFIRMED".
            var classRatios = new float[8];
            for (var s = 0; s < 8; s++)
                classRatios[s] = BinaryPrimitives.ReadSingleLittleEndian(blk[(92 + s * 4)..]);

            classBlocks[b] = new UsersClassBlock
            {
                ClassId = classId,
                StatGroupA = statGroupA,
                ClassSpecificRatios = classRatios,
                RawBlock = data.Slice(windowBase, UsersBlock.ClassBlockSize)
            };
        }

        return new UsersBlock { ClassBlocks = classBlocks, RawData = data };
    }

    /// <summary>
    ///     Parses <c>data/script/skills.scr</c> — skill catalogue.
    ///     Each main record is 1504 bytes; the trailing entry count is at byte offset 1500 within
    ///     each record (see code comment for spec ambiguity note).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of skill catalogue entries in on-disk order.</returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.8 skills.scr: CONFIRMED (stride 1504, trailing N×8).
    ///     Trailing count offset ambiguity: see inline comment above (SkillScrTrailingCountOffset).
    ///     Main body field layout: partially UNVERIFIED; confirmed named fields are in the spec table.
    ///     <para>
    ///         CONFIRMED-variable fields (spec: Docs/RE/formats/config_tables.md §2.8 CAMPAIGN VFS-MASTERY update):
    ///         The following fields are now CONFIRMED-variable (two-witness: loader + black-box full-record pass).
    ///         They sit inside a verbatim-copied record body with no branch, so values genuinely vary.
    ///         Their SEMANTICS remain DBG-pending — do not assign meaning from bytes alone.
    ///         - skills.scr +1072: CONFIRMED-variable (CP949 string-field interior; 54 distinct u32 values).
    ///         spec: Docs/RE/formats/config_tables.md §2.8 — "+1072 CONFIRMED-variable; semantic DBG-pending".
    ///         - skills.scr +1176 f32: CONFIRMED-variable (8 distinct float values {0.4,0.6,0.8,1.0,1.2,1.4,1.5,2.0}).
    ///         spec: Docs/RE/formats/config_tables.md §2.8 — "+1176 CONFIRMED-variable; semantic DBG-pending".
    ///         - skills.scr +1306 u16: CONFIRMED-variable (10 distinct values; modal 5).
    ///         spec: Docs/RE/formats/config_tables.md §2.8 — "+1306 CONFIRMED-variable; semantic DBG-pending".
    ///         - skills.scr +1328: CONFIRMED-variable (8 distinct values (1&lt;&lt;16)..(8&lt;&lt;16); independent of +516
    ///         class flag).
    ///         spec: Docs/RE/formats/config_tables.md §2.8 — "+1328 CONFIRMED-variable; semantic DBG-pending".
    ///     </para>
    /// </remarks>
    public static SkillCatalogEntry[] ParseSkillsScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length == 0)
            return [];

        if (span.Length < SkillScrMainStride)
            throw new InvalidDataException(
                $"skills.scr parse error: buffer too small for one record " +
                $"(need {SkillScrMainStride} bytes, got {span.Length}).");

        var results = new List<SkillCatalogEntry>();
        var offset = 0;

        while (offset < span.Length)
        {
            if (offset + SkillScrMainStride > span.Length)
                throw new InvalidDataException(
                    $"skills.scr parse error: main record truncated at offset {offset}.");

            var rawRecord = data.Slice(offset, SkillScrMainStride);

            // Trailing entry count at confirmed offset within the record.
            // spec: Docs/RE/formats/config_tables.md §2.8 — "trailing count byte: CONFIRMED (offset ambiguous, see code comment)".
            var trailingCount = span[offset + SkillScrTrailingCountOffset];

            offset += SkillScrMainStride;

            // Read N×8 trailing sub-entries; all fields UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.8 — "trailing N×8 bytes, all fields UNVERIFIED".
            var trailingEntries = new ReadOnlyMemory<byte>[trailingCount];
            for (var ti = 0; ti < trailingCount; ti++)
            {
                if (offset + SkillScrTrailingStride > span.Length)
                    throw new InvalidDataException(
                        $"skills.scr parse error: trailing entry [{ti}] truncated at offset {offset}.");

                trailingEntries[ti] = data.Slice(offset, SkillScrTrailingStride);
                offset += SkillScrTrailingStride;
            }

            results.Add(new SkillCatalogEntry
            {
                RawRecord = rawRecord,
                TrailingCount = trailingCount,
                TrailingEntries = trailingEntries
            });
        }

        return results.ToArray();
    }

    /// <summary>
    ///     Parses <c>data/script/mobs.scr</c> — mob catalogue.
    ///     Record count = file_size / 488. Only the ID and type fields are decoded; full record is raw.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of mob catalogue entries in on-disk order.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown if the buffer is not a multiple of the stride.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr: CONFIRMED (stride, ID @ +0, type @ +324).
    ///     Remaining fields: UNVERIFIED.
    ///     <para>
    ///         CONFIRMED-variable fields (spec: Docs/RE/formats/config_tables.md §2.9 CAMPAIGN VFS-MASTERY update):
    ///         The following fields are now CONFIRMED-variable (two-witness: loader + black-box full-record pass).
    ///         They sit inside a verbatim-copied record body with no branch, so values genuinely vary.
    ///         Their SEMANTICS remain DBG-pending — do not assign meaning from bytes alone.
    ///         - mobs.scr +60 f32: CONFIRMED-variable (31 distinct float values; modal 3.0; range 0.5..400).
    ///         spec: Docs/RE/formats/config_tables.md §2.9 — "+60 f32 CONFIRMED-variable; semantic DBG-pending".
    ///         - mobs.scr +188 f32: CONFIRMED-variable (41 distinct values; modal 1.0 at 77%; outliers to 6000).
    ///         spec: Docs/RE/formats/config_tables.md §2.9 — "+188 f32 CONFIRMED-variable; semantic DBG-pending".
    ///         - mobs.scr +272 24B: CONFIRMED-variable (6×f32; 0 of 3997 records are all-1.0).
    ///         spec: Docs/RE/formats/config_tables.md §2.9 — "+272 6×f32 CONFIRMED-variable; semantics DBG-pending".
    ///     </para>
    /// </remarks>
    public static MobCatalogEntry[] ParseMobsScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % MobScrStride != 0)
            throw new InvalidDataException(
                $"mobs.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {MobScrStride}. spec: Docs/RE/formats/config_tables.md §2.9.");

        var count = span.Length / MobScrStride;
        var results = new MobCatalogEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * MobScrStride;

            // Mob ID u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.9 — "Mob ID u16 @ +0: CONFIRMED".
            var mobId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(recOffset + MobIdOffset, 2));

            // Mob type u8 @ +324. Value 11 = boss/elite. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type (11=boss/elite): CONFIRMED".
            var mobType = span[recOffset + MobTypeOffset];

            // Mob level i32 @ +244. -1 = not set; boss range 36..46. CONFIRMED (boss validation path).
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+244 i32 Mob level: CONFIRMED".
            var mobLevel = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(recOffset + MobLevelOffset, 4));

            // Spawn timer u32 @ +248. Range 33..41006 in sample; boss ~40 s. CONFIRMED (plausible range).
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+248 u32 Spawn timer (seconds): CONFIRMED".
            var spawnTimer = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(recOffset + SpawnTimerOffset, 4));

            // Full 488-byte raw record (zero-copy slice).
            var raw = data.Slice(recOffset, MobScrStride);

            results[i] = new MobCatalogEntry
            {
                Id = mobId,
                Type = mobType,
                MobLevel = mobLevel,
                SpawnTimer = spawnTimer,
                Raw = raw
            };
        }

        return results;
    }
}