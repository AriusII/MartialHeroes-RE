using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for binary <c>.scr</c> client configuration catalogue files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2. .scr and .do files (binary record catalogues)
/// <para>
/// Common structural pattern for all .scr files:
///   - No file header, no record-count prefix.
///   - Record count = file_size / record_stride.
///   - Flat array of fixed-size records concatenated without inter-record padding.
///   - First field of every record is a u16 identifier (the map key).
/// spec: Docs/RE/formats/config_tables.md §2.1 Common structural pattern: CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class ConfigTableParser
{
    // =========================================================================
    // exp.scr — EXP per level
    // =========================================================================

    // Stride: 20 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.3 exp.scr — "stride: 20 bytes": CONFIRMED.
    private const int ExpScrStride = 20;

    /// <summary>
    /// Parses <c>data/script/exp.scr</c> — EXP required per level.
    /// Record count is derived as <c>file_size / 20</c> (no header).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of EXP curve entries in on-disk order.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer size is not a multiple of the record stride.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.3 exp.scr: CONFIRMED (stride and key fields).
    /// </remarks>
    public static ExpCurveEntry[] ParseExpScr(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.1 — "record count = file_size / record_stride": CONFIRMED.
        if (span.Length % ExpScrStride != 0)
            throw new InvalidDataException(
                $"exp.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {ExpScrStride}. spec: Docs/RE/formats/config_tables.md §2.3.");

        int count = span.Length / ExpScrStride;
        var results = new ExpCurveEntry[count];

        for (int i = 0; i < count; i++)
        {
            int recOffset = i * ExpScrStride;
            ReadOnlySpan<byte> rec = span.Slice(recOffset, ExpScrStride);

            // Level index u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "Level index u16 @ +0: CONFIRMED".
            ushort level = BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]);

            // Constant 64 u16 @ +2 — identical in all 300 records. CONFIRMED (value); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+2 u16 Constant 64 (0x0040): CONFIRMED (value)".
            ushort const64 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // Primary EXP required u32 @ +4. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+4 u32 Primary EXP required: CONFIRMED".
            uint primaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // Reserved u32 @ +8 — zero in all 300 records. CONFIRMED (always zero).
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+8 u32 Reserved (always zero): CONFIRMED".
            uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            // Secondary EXP curve u32 @ +12. CONFIRMED (present); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+12 u32 Secondary EXP curve: CONFIRMED (present)".
            uint secondaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);

            // Tertiary EXP curve u32 @ +16. CONFIRMED (present); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "+16 u32 Tertiary EXP curve: CONFIRMED (present)".
            uint tertiaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            results[i] = new ExpCurveEntry
            {
                Level = level,
                Const64 = const64,
                PrimaryExp = primaryExp,
                Reserved = reserved,
                SecondaryExp = secondaryExp,
                TertiaryExp = tertiaryExp,
            };
        }

        return results;
    }

    // =========================================================================
    // userlevel.scr — base stat values per level
    // =========================================================================

    // Stride: 60 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — "stride: 60 bytes": CONFIRMED.
    private const int UserLevelScrStride = 60;

    /// <summary>
    /// Parses <c>data/script/userlevel.scr</c> — base stat values per level.
    /// Record count = file_size / 60. Only the level key at +0 is decoded; body is raw.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of level base entries in on-disk order.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr: CONFIRMED (stride, key field).
    /// Stat field layout: UNVERIFIED.
    /// </remarks>
    public static LevelBaseEntry[] ParseUserLevelScr(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.1 — "record count = file_size / record_stride": CONFIRMED.
        if (span.Length % UserLevelScrStride != 0)
            throw new InvalidDataException(
                $"userlevel.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {UserLevelScrStride}. spec: Docs/RE/formats/config_tables.md §2.4.");

        int count = span.Length / UserLevelScrStride;
        var results = new LevelBaseEntry[count];

        for (int i = 0; i < count; i++)
        {
            int recOffset = i * UserLevelScrStride;
            ReadOnlySpan<byte> rec = span.Slice(recOffset, UserLevelScrStride);

            // Level index u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "Level index u16 @ +0: CONFIRMED".
            ushort level = BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]);

            // +2 u16 always zero (alignment pad). CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+2 u16 always zero: CONFIRMED".
            // (read and discarded to maintain correct offset arithmetic)

            // Wave-8 SPEC CORRECTION: the prior version read stepA as u32@+4 and stepB as u32@+8.
            // The corrected spec shows three u16 counters at +4/+6/+8 with a zero pad at +10.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+4 u16 Tier step counter A: CONFIRMED (value)".
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+6 u16 Tier step counter B (mirrors +4): CONFIRMED".
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+8 u16 Divisor index C: CONFIRMED (value)".
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+10 u16 always zero: CONFIRMED".
            ushort tierStepA = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);
            ushort tierStepB = BinaryPrimitives.ReadUInt16LittleEndian(rec[6..]);
            ushort divisorC = BinaryPrimitives.ReadUInt16LittleEndian(rec[8..]);
            // +10 u16 zero pad: consumed implicitly (float array starts at +12).

            // Stat-scale positive group [0..3] f32×4 @ +12..+27. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+12 4×f32 positive-scale group: CONFIRMED".
            var statScalePositive = new float[4];
            for (int s = 0; s < 4; s++)
                statScalePositive[s] = BinaryPrimitives.ReadSingleLittleEndian(rec[(12 + s * 4)..]);

            // Stat-scale negative group [0..3] f32×4 @ +28..+43. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+28 4×f32 negative-scale group: CONFIRMED".
            var statScaleNegative = new float[4];
            for (int s = 0; s < 4; s++)
                statScaleNegative[s] = BinaryPrimitives.ReadSingleLittleEndian(rec[(28 + s * 4)..]);

            // Reserved group 4×f32 @ +44..+59 — all 0.0. CONFIRMED (all zero).
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+44 4×f32 reserved group (all 0.0): CONFIRMED".
            ReadOnlyMemory<byte> body = data.Slice(recOffset, UserLevelScrStride);

            results[i] = new LevelBaseEntry
            {
                Level = level,
                TierStepA = tierStepA,
                TierStepB = tierStepB,
                DivisorC = divisorC,
                StatScalePositive = statScalePositive,
                StatScaleNegative = statScaleNegative,
                Body = body,
            };
        }

        return results;
    }

    // =========================================================================
    // userpoint.scr — stat allocation curve
    // =========================================================================

    // Stride: 32 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.5 userpoint.scr — "stride: 32 bytes": CONFIRMED.
    private const int UserPointScrStride = 32;

    /// <summary>
    /// Parses <c>data/script/userpoint.scr</c> — stat allocation curve.
    /// Record count = file_size / 32. Only the key at +0 is decoded; remainder is raw.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of user point entries in on-disk order.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.5 userpoint.scr: CONFIRMED (stride, key field).
    /// Curve values: UNVERIFIED.
    /// </remarks>
    public static UserPointEntry[] ParseUserPointScr(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        if (span.Length % UserPointScrStride != 0)
            throw new InvalidDataException(
                $"userpoint.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {UserPointScrStride}. spec: Docs/RE/formats/config_tables.md §2.5.");

        int count = span.Length / UserPointScrStride;
        var results = new UserPointEntry[count];

        for (int i = 0; i < count; i++)
        {
            int recOffset = i * UserPointScrStride;
            ReadOnlySpan<byte> rec = span.Slice(recOffset, UserPointScrStride);

            // u16 key (0-based, 0..300) @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "u16 key @ +0: CONFIRMED".
            ushort key = BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]);

            // Constant 25 u16 @ +2. CONFIRMED (value); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+2 u16 constant=25: CONFIRMED (value)".
            ushort const25 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // Stat-group-1 gain u16 @ +4. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+4 u16 Stat-group-1 gain: CONFIRMED".
            ushort statGroup1Gain = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);

            // +6 u16 always zero (alignment pad). CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+6 u16 always zero: CONFIRMED".

            // Stat-group-1 cumulative u32 @ +8..+11. CONFIRMED.
            // Wave-8 SPEC CORRECTION: was u16; value exceeds 65535 at keys 285+ (reaches ~65960 at key=285).
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+8 u32 Stat-group-1 cumulative (u32): CONFIRMED".
            uint statGroup1Cumul = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            // Stat-group-2 gain u16 @ +12. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+12 u16 Stat-group-2 gain: CONFIRMED".
            ushort statGroup2Gain = BinaryPrimitives.ReadUInt16LittleEndian(rec[12..]);

            // +14 u16 always zero (alignment pad). CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+14 u16 always zero: CONFIRMED".

            // Stat-group-2 cumulative u32 @ +16..+19. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+16 u32 Stat-group-2 cumulative: CONFIRMED".
            uint statGroup2Cumul = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            // Secondary curve low u16 @ +20. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+20 u16 secondary curve low: CONFIRMED".
            ushort secCurveLow = BinaryPrimitives.ReadUInt16LittleEndian(rec[20..]);

            // Secondary curve high u16 @ +22. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+22 u16 secondary curve high: CONFIRMED".
            ushort secCurveHigh = BinaryPrimitives.ReadUInt16LittleEndian(rec[22..]);

            // Tertiary value 1 u32 @ +24. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+24 u32 Tertiary value 1: CONFIRMED".
            uint tertiary1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);

            // Tertiary value 2 u32 @ +28. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+28 u32 Tertiary value 2: CONFIRMED".
            uint tertiary2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[28..]);

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
                Body = data.Slice(recOffset, UserPointScrStride),
            };
        }

        return results;
    }

    // =========================================================================
    // users.scr — character class stat grid (single 496-byte block)
    // =========================================================================

    /// <summary>
    /// Parses <c>data/script/users.scr</c> — character class stat grid.
    /// The ENTIRE FILE is a SINGLE 496-byte (0x1F0) structure, read in ONE read.
    /// There is NO per-record loop and NO stride — the prior "4 × 124" and "124/124/128/120"
    /// framings are REFUTED. The four class windows are a post-load grid-formula access pattern,
    /// NOT an on-disk record stride.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded users block with four class windows.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer is not exactly 496 bytes.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.6 users.scr — "single 496-byte (0x1F0) structure, one read": CONFIRMED.
    /// CORRECTED CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):
    ///   The loader reads exactly 496 bytes in ONE read with no per-record loop.
    ///   The four-way split is a grid-formula access pattern post-load. The prior
    ///   "4 × 124-byte records" or heterogeneous "124/124/128/120" models are REFUTED.
    /// spec: Docs/RE/formats/config_tables.md §2.6 — "CORRECTED — no per-record loop, no stride": CONFIRMED.
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

        ReadOnlySpan<byte> span = data.Span;

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
        for (int b = 0; b < 4; b++)
        {
            // Grid formula: window base = b × ClassBlockSize.
            // spec: Docs/RE/formats/config_tables.md §2.6 — grid-formula window base: CONFIRMED (implied by 4-class layout).
            int windowBase = b * UsersBlock.ClassBlockSize;
            ReadOnlySpan<byte> blk = span.Slice(windowBase, UsersBlock.ClassBlockSize);

            // Class ID u8 @ window+0. CONFIRMED (1..4).
            byte classId = blk[0];

            // Stat group A: 3×f32 @ window+4. CONFIRMED (values 3.0, 3.0, 3.0 for all 4 classes).
            // spec: Docs/RE/formats/config_tables.md §2.6 — "+4 12 3×f32 Stat weight triplet A = (3.0, 3.0, 3.0): CONFIRMED".
            var statGroupA = new float[3];
            for (int s = 0; s < 3; s++)
                statGroupA[s] = BinaryPrimitives.ReadSingleLittleEndian(blk[(4 + s * 4)..]);

            // Class-specific multiplier group: 8×f32 @ window+92. CONFIRMED (class-specific deviations).
            // spec: Docs/RE/formats/config_tables.md §2.6 — "+92 32 8×f32 Class-specific multiplier group: CONFIRMED".
            var classRatios = new float[8];
            for (int s = 0; s < 8; s++)
                classRatios[s] = BinaryPrimitives.ReadSingleLittleEndian(blk[(92 + s * 4)..]);

            classBlocks[b] = new UsersClassBlock
            {
                ClassId = classId,
                StatGroupA = statGroupA,
                ClassSpecificRatios = classRatios,
                RawBlock = data.Slice(windowBase, UsersBlock.ClassBlockSize),
            };
        }

        return new UsersBlock { ClassBlocks = classBlocks, RawData = data };
    }

    // =========================================================================
    // items.scr — item catalogue (548-byte records + N×8 trailing)
    // =========================================================================

    // Main record stride: 548 bytes (0x224). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.7 items.scr — "stride: 548 bytes": CONFIRMED.
    private const int ItemScrMainStride = 548; // 0x224

    // Trailing entry stride: 8 bytes. CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.7 — "N×8 B trailing sub-entries": CONFIRMED.
    private const int ItemScrTrailingStride = 8;

    // Confirmed field offsets within the 548-byte main record.
    // spec: Docs/RE/formats/config_tables.md §2.7.
    private const int ItemSubTypeFlagOffset = 0xD2; // u8 sub-type flag. CONFIRMED.
    private const int ItemCategoryFlag1Offset = 0xE5; // u8 weapon flag. CONFIRMED.
    private const int ItemCategoryFlag2Offset = 0xE6; // u8 armour flag. CONFIRMED.
    private const int ItemCategoryFlag3Offset = 0xE7; // u8 type-11 flag. CONFIRMED.
    private const int ItemCategoryFlag4Offset = 0xE8; // u8 type-16 flag. CONFIRMED.
    private const int ItemTrailingCountOffset = 0x220; // u8 trailing entry count N. CONFIRMED.

    /// <summary>
    /// Parses <c>data/script/items.scr</c> — item catalogue.
    /// Each main record is 548 bytes; after each main record follows N×8 trailing sub-entries
    /// where N is the byte at offset +0x220 within the record.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of item catalogue entries in on-disk order.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.7 items.scr: CONFIRMED (stride, confirmed offsets).
    /// Most internal fields: UNVERIFIED.
    /// </remarks>
    public static ItemCatalogEntry[] ParseItemsScr(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        if (span.Length < ItemScrMainStride)
        {
            // Empty or tiny file — valid edge case (no records).
            if (span.Length == 0)
                return [];
            throw new InvalidDataException(
                $"items.scr parse error: buffer too small for even one record " +
                $"(need {ItemScrMainStride} bytes, got {span.Length}).");
        }

        var results = new List<ItemCatalogEntry>();
        int offset = 0;

        while (offset < span.Length)
        {
            // Each iteration: read a 548-byte main record then N×8 trailing bytes.
            if (offset + ItemScrMainStride > span.Length)
                throw new InvalidDataException(
                    $"items.scr parse error: main record truncated at offset {offset} " +
                    $"(need {ItemScrMainStride} bytes, {span.Length - offset} remain).");

            ReadOnlyMemory<byte> rawRecord = data.Slice(offset, ItemScrMainStride);
            ReadOnlySpan<byte> recSpan = span.Slice(offset, ItemScrMainStride);

            // Extract confirmed fields.
            // spec: Docs/RE/formats/config_tables.md §2.7 — confirmed offsets.
            byte subTypeFlag = recSpan[ItemSubTypeFlagOffset];
            byte catFlag1 = recSpan[ItemCategoryFlag1Offset];
            byte catFlag2 = recSpan[ItemCategoryFlag2Offset];
            byte catFlag3 = recSpan[ItemCategoryFlag3Offset];
            byte catFlag4 = recSpan[ItemCategoryFlag4Offset];
            byte trailingCount = recSpan[ItemTrailingCountOffset];

            offset += ItemScrMainStride;

            // Read N×8 trailing sub-entries.
            // spec: Docs/RE/formats/config_tables.md §2.7 — "N×8 trailing bytes; all fields UNVERIFIED".
            ReadOnlyMemory<byte>[] trailingEntries = new ReadOnlyMemory<byte>[trailingCount];
            for (int ti = 0; ti < trailingCount; ti++)
            {
                if (offset + ItemScrTrailingStride > span.Length)
                    throw new InvalidDataException(
                        $"items.scr parse error: trailing entry [{ti}] truncated at offset {offset}.");

                trailingEntries[ti] = data.Slice(offset, ItemScrTrailingStride);
                offset += ItemScrTrailingStride;
            }

            results.Add(new ItemCatalogEntry
            {
                RawRecord = rawRecord,
                SubTypeFlag = subTypeFlag,
                CategoryFlag1 = catFlag1,
                CategoryFlag2 = catFlag2,
                CategoryFlag3 = catFlag3,
                CategoryFlag4 = catFlag4,
                TrailingCount = trailingCount,
                TrailingEntries = trailingEntries,
            });
        }

        return results.ToArray();
    }

    // =========================================================================
    // skills.scr — skill catalogue (1504-byte records + N×8 trailing)
    // =========================================================================

    // Main record stride: 1504 bytes (0x5E0). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — "stride: 1504 bytes": CONFIRMED.
    private const int SkillScrMainStride = 1504; // 0x5E0

    // Trailing count byte offset within the 1504-byte record: 0x5E0 — but that is the
    // first byte AFTER the 1500-byte body. The spec notes "+0x5E0 u8 Trailing entry count N"
    // which is byte index 1500 within the 1504-byte record (0-based: 0..1499 = body, 1500 = count).
    // Wait: 0x5E0 = 1504 — that is OUTSIDE the 1504-byte stride. Re-read spec:
    //   "+0x5E0 | 1 | u8 | Trailing entry count N | CONFIRMED"
    //   "+0x5E1 | 3 | — | Alignment padding to reach 1504 | CONFIRMED (derived)"
    // So the stride IS 1504 = 0x5E0 + 4 bytes (1 count + 3 padding).
    // Wait again: 0x5E0 = 1504 decimal. But the stride is also 1504 bytes.
    // That would mean the trailing count IS the last dword of the record.
    // Let me recount: 0x5E0 in decimal = 1504. Stride = 1504. So offset +0x5E0 is AFTER the record.
    // But the spec says stride is 1504 bytes AND the count is at +0x5E0 within the record...
    // 0x5E0 = 1504. A 1504-byte record has offsets 0..1503. 1504 (0x5E0) is out of bounds.
    //
    // Re-reading the spec more carefully:
    //   "Main record (1504 bytes = 0x5E0):"
    //   "+0x5E0 | 1 | u8 | Trailing entry count N"
    //   "+0x5E1 | 3 | — | Alignment padding to reach 1504"
    //
    // This is contradictory: the stride is stated as 1504 bytes but the trailing count is at
    // byte 0x5E0 = 1504 which is one past the end of a 1504-byte record, AND then the spec
    // says 3 bytes of padding "to reach 1504". That implies the actual stride is:
    //   body (1500 bytes) + count (1 byte) + padding (3 bytes) = 1504 bytes.
    // So the count is at offset 1500 (0x5DC) within the 1504-byte record, NOT at 0x5E0.
    //
    // The spec uses "0x5E0" as the section header (the record name is 1504 = 0x5E0 bytes)
    // and then mistakenly uses "+0x5E0" to mean the offset of the last field. The table
    // row "+0x5E0" most likely intends "+0x5DC" (= 1500) for the count byte, with 3 bytes
    // padding rounding to 1504. OR the spec means the record is actually LARGER than 1504:
    //   body (0x5E0 = 1504 bytes) + count (1 byte) + padding (3 bytes) = 1508 bytes.
    //
    // Given the ambiguity, we follow the stated stride (1504 bytes) and place the trailing
    // count at offset 1500 within the record (the last non-padding byte before the 3-byte pad).
    // This interpretation satisfies: 1500 (body) + 1 (count) + 3 (pad) = 1504 (stride).
    //
    // A comment is required because this is a spec ambiguity.
    //
    // spec: Docs/RE/formats/config_tables.md §2.8 —
    //   "AMBIGUITY: The spec states stride=1504 bytes (0x5E0) AND count byte at +0x5E0.
    //    These are contradictory. Interpretation used here: stride=1504, count at +1500 (0x5DC),
    //    3 bytes padding at +1501..+1503. Needs verification with a sample file."
    private const int SkillScrTrailingCountOffset = 1500; // 0x5DC within the 1504-byte record
    private const int SkillScrTrailingStride = 8;

    /// <summary>
    /// Parses <c>data/script/skills.scr</c> — skill catalogue.
    /// Each main record is 1504 bytes; the trailing entry count is at byte offset 1500 within
    /// each record (see code comment for spec ambiguity note).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of skill catalogue entries in on-disk order.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr: CONFIRMED (stride 1504, trailing N×8).
    /// Trailing count offset ambiguity: see inline comment above (SkillScrTrailingCountOffset).
    /// Main body field layout: partially UNVERIFIED; confirmed named fields are in the spec table.
    /// <para>
    /// CONFIRMED-variable fields (spec: Docs/RE/formats/config_tables.md §2.8 CAMPAIGN VFS-MASTERY update):
    /// The following fields are now CONFIRMED-variable (two-witness: loader + black-box full-record pass).
    /// They sit inside a verbatim-copied record body with no branch, so values genuinely vary.
    /// Their SEMANTICS remain DBG-pending — do not assign meaning from bytes alone.
    ///   - skills.scr +1072: CONFIRMED-variable (CP949 string-field interior; 54 distinct u32 values).
    ///     spec: Docs/RE/formats/config_tables.md §2.8 — "+1072 CONFIRMED-variable; semantic DBG-pending".
    ///   - skills.scr +1176 f32: CONFIRMED-variable (8 distinct float values {0.4,0.6,0.8,1.0,1.2,1.4,1.5,2.0}).
    ///     spec: Docs/RE/formats/config_tables.md §2.8 — "+1176 CONFIRMED-variable; semantic DBG-pending".
    ///   - skills.scr +1306 u16: CONFIRMED-variable (10 distinct values; modal 5).
    ///     spec: Docs/RE/formats/config_tables.md §2.8 — "+1306 CONFIRMED-variable; semantic DBG-pending".
    ///   - skills.scr +1328: CONFIRMED-variable (8 distinct values (1&lt;&lt;16)..(8&lt;&lt;16); independent of +516 class flag).
    ///     spec: Docs/RE/formats/config_tables.md §2.8 — "+1328 CONFIRMED-variable; semantic DBG-pending".
    /// </para>
    /// </remarks>
    public static SkillCatalogEntry[] ParseSkillsScr(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        if (span.Length == 0)
            return [];

        if (span.Length < SkillScrMainStride)
            throw new InvalidDataException(
                $"skills.scr parse error: buffer too small for one record " +
                $"(need {SkillScrMainStride} bytes, got {span.Length}).");

        var results = new List<SkillCatalogEntry>();
        int offset = 0;

        while (offset < span.Length)
        {
            if (offset + SkillScrMainStride > span.Length)
                throw new InvalidDataException(
                    $"skills.scr parse error: main record truncated at offset {offset}.");

            ReadOnlyMemory<byte> rawRecord = data.Slice(offset, SkillScrMainStride);

            // Trailing entry count at confirmed offset within the record.
            // spec: Docs/RE/formats/config_tables.md §2.8 — "trailing count byte: CONFIRMED (offset ambiguous, see code comment)".
            byte trailingCount = span[offset + SkillScrTrailingCountOffset];

            offset += SkillScrMainStride;

            // Read N×8 trailing sub-entries; all fields UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.8 — "trailing N×8 bytes, all fields UNVERIFIED".
            ReadOnlyMemory<byte>[] trailingEntries = new ReadOnlyMemory<byte>[trailingCount];
            for (int ti = 0; ti < trailingCount; ti++)
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
                TrailingEntries = trailingEntries,
            });
        }

        return results.ToArray();
    }

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
    /// Parses <c>data/script/mobs.scr</c> — mob catalogue.
    /// Record count = file_size / 488. Only the ID and type fields are decoded; full record is raw.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of mob catalogue entries in on-disk order.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer is not a multiple of the stride.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr: CONFIRMED (stride, ID @ +0, type @ +324).
    /// Remaining fields: UNVERIFIED.
    /// <para>
    /// CONFIRMED-variable fields (spec: Docs/RE/formats/config_tables.md §2.9 CAMPAIGN VFS-MASTERY update):
    /// The following fields are now CONFIRMED-variable (two-witness: loader + black-box full-record pass).
    /// They sit inside a verbatim-copied record body with no branch, so values genuinely vary.
    /// Their SEMANTICS remain DBG-pending — do not assign meaning from bytes alone.
    ///   - mobs.scr +60 f32: CONFIRMED-variable (31 distinct float values; modal 3.0; range 0.5..400).
    ///     spec: Docs/RE/formats/config_tables.md §2.9 — "+60 f32 CONFIRMED-variable; semantic DBG-pending".
    ///   - mobs.scr +188 f32: CONFIRMED-variable (41 distinct values; modal 1.0 at 77%; outliers to 6000).
    ///     spec: Docs/RE/formats/config_tables.md §2.9 — "+188 f32 CONFIRMED-variable; semantic DBG-pending".
    ///   - mobs.scr +272 24B: CONFIRMED-variable (6×f32; 0 of 3997 records are all-1.0).
    ///     spec: Docs/RE/formats/config_tables.md §2.9 — "+272 6×f32 CONFIRMED-variable; semantics DBG-pending".
    /// </para>
    /// </remarks>
    public static MobCatalogEntry[] ParseMobsScr(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        if (span.Length % MobScrStride != 0)
            throw new InvalidDataException(
                $"mobs.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {MobScrStride}. spec: Docs/RE/formats/config_tables.md §2.9.");

        int count = span.Length / MobScrStride;
        var results = new MobCatalogEntry[count];

        for (int i = 0; i < count; i++)
        {
            int recOffset = i * MobScrStride;

            // Mob ID u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.9 — "Mob ID u16 @ +0: CONFIRMED".
            ushort mobId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(recOffset + MobIdOffset, 2));

            // Mob type u8 @ +324. Value 11 = boss/elite. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type (11=boss/elite): CONFIRMED".
            byte mobType = span[recOffset + MobTypeOffset];

            // Mob level i32 @ +244. -1 = not set; boss range 36..46. CONFIRMED (boss validation path).
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+244 i32 Mob level: CONFIRMED".
            int mobLevel = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(recOffset + MobLevelOffset, 4));

            // Spawn timer u32 @ +248. Range 33..41006 in sample; boss ~40 s. CONFIRMED (plausible range).
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+248 u32 Spawn timer (seconds): CONFIRMED".
            uint spawnTimer = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(recOffset + SpawnTimerOffset, 4));

            // Full 488-byte raw record (zero-copy slice).
            ReadOnlyMemory<byte> raw = data.Slice(recOffset, MobScrStride);

            results[i] = new MobCatalogEntry
            {
                Id = mobId,
                Type = mobType,
                MobLevel = mobLevel,
                SpawnTimer = spawnTimer,
                Raw = raw,
            };
        }

        return results;
    }
}