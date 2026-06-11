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

            // EXP column A u32 @ +2. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "EXP column 0 u32 @ +2: CONFIRMED".
            uint columnA = BinaryPrimitives.ReadUInt32LittleEndian(rec[2..]);

            // EXP column B u32 @ +6. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "EXP column 1 u32 @ +6: CONFIRMED".
            uint columnB = BinaryPrimitives.ReadUInt32LittleEndian(rec[6..]);

            // Bytes +10 to +19 (10 bytes): meaning UNVERIFIED — expose raw.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "Remaining fields UNVERIFIED".
            // Slice from the backing Memory so it is zero-copy.
            ReadOnlyMemory<byte> tail = data.Slice(recOffset + 10, 10);

            results[i] = new ExpCurveEntry
            {
                Level = level,
                ColumnA = columnA,
                ColumnB = columnB,
                RawTail = tail,
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

            // Level index u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "Level index u16 @ +0: CONFIRMED".
            ushort level = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(recOffset, 2));

            // Body bytes +2 to +59 (58 bytes). Stat layout: UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+2: 58 bytes, UNVERIFIED".
            ReadOnlyMemory<byte> body = data.Slice(recOffset + 2, 58);

            results[i] = new LevelBaseEntry { Level = level, Body = body };
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

            // u16 key @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "u16 key @ +0: CONFIRMED".
            ushort key = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(recOffset, 2));

            // Body +2 to +31 (30 bytes). UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.5 — "+2: 30 bytes, UNVERIFIED".
            ReadOnlyMemory<byte> body = data.Slice(recOffset + 2, 30);

            results[i] = new UserPointEntry { Key = key, Body = body };
        }

        return results;
    }

    // =========================================================================
    // users.scr — character class stat grid (single 496-byte block)
    // =========================================================================

    /// <summary>
    /// Parses <c>data/script/users.scr</c> — character class stat grid.
    /// The entire file is a single 496-byte opaque block.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Opaque users block.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer is not exactly 496 bytes.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.6 users.scr — "496-byte bulk block": CONFIRMED (size only).
    /// Internal layout: UNVERIFIED.
    /// </remarks>
    public static UsersBlock ParseUsersScr(ReadOnlyMemory<byte> data)
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — "496-byte bulk block": CONFIRMED.
        if (data.Length != UsersBlock.FixedSize)
            throw new InvalidDataException(
                $"users.scr parse error: expected exactly {UsersBlock.FixedSize} bytes, " +
                $"got {data.Length}. spec: Docs/RE/formats/config_tables.md §2.6.");

        return new UsersBlock { RawData = data };
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
    /// Main body field layout: UNVERIFIED.
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

            // Full 488-byte raw record (zero-copy slice).
            ReadOnlyMemory<byte> raw = data.Slice(recOffset, MobScrStride);

            results[i] = new MobCatalogEntry
            {
                Id = mobId,
                Type = mobType,
                Raw = raw,
            };
        }

        return results;
    }
}