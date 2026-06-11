using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="ConfigTableParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/config_tables.md §2
/// </summary>
public sealed class ConfigTableParserTests
{
    // -----------------------------------------------------------------------
    // Binary helpers
    // -----------------------------------------------------------------------

    private static void WriteU16LE(byte[] buf, int offset, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), v);

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    // =========================================================================
    // exp.scr tests — stride 20 bytes
    // =========================================================================

    /// <summary>
    /// Builds a synthetic exp.scr buffer with <paramref name="count"/> records.
    /// spec: Docs/RE/formats/config_tables.md §2.3 exp.scr — stride 20 bytes: CONFIRMED.
    /// </summary>
    private static byte[] BuildExpScr(
        (ushort level, uint colA, uint colB)[] records)
    {
        // stride = 20 bytes per record; no header.
        // spec: Docs/RE/formats/config_tables.md §2.1 — "No file header, no record-count prefix": CONFIRMED.
        byte[] buf = new byte[records.Length * 20];
        for (int i = 0; i < records.Length; i++)
        {
            int off = i * 20;
            // Level u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "Level index u16 @ +0: CONFIRMED".
            WriteU16LE(buf, off + 0, records[i].level);
            // EXP column A u32 @ +2. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "EXP column 0 u32 @ +2: CONFIRMED".
            WriteU32LE(buf, off + 2, records[i].colA);
            // EXP column B u32 @ +6. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.3 — "EXP column 1 u32 @ +6: CONFIRMED".
            WriteU32LE(buf, off + 6, records[i].colB);
            // Bytes +10..+19: leave as zero (UNVERIFIED tail).
            // spec: Docs/RE/formats/config_tables.md §2.3 — "Remaining fields UNVERIFIED".
        }
        return buf;
    }

    [Fact]
    public void ExpScr_Parse_RecordCount_FromStride()
    {
        // spec: Docs/RE/formats/config_tables.md §2.1 — "record count = file_size / stride": CONFIRMED.
        byte[] data = BuildExpScr([(1, 100u, 200u), (2, 300u, 400u), (3, 500u, 600u)]);
        ExpCurveEntry[] entries = ConfigTableParser.ParseExpScr(new ReadOnlyMemory<byte>(data));
        Assert.Equal(3, entries.Length);
    }

    [Fact]
    public void ExpScr_Parse_LevelField()
    {
        // spec: Docs/RE/formats/config_tables.md §2.3 — "Level index u16 @ +0: CONFIRMED".
        byte[] data = BuildExpScr([(42, 1000u, 2000u)]);
        ExpCurveEntry[] entries = ConfigTableParser.ParseExpScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((ushort)42, entries[0].Level);
    }

    [Fact]
    public void ExpScr_Parse_ExpColumns()
    {
        // spec: Docs/RE/formats/config_tables.md §2.3 — column A @ +2, column B @ +6: CONFIRMED.
        byte[] data = BuildExpScr([(1, 99999u, 88888u)]);
        ExpCurveEntry[] entries = ConfigTableParser.ParseExpScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(99999u, entries[0].ColumnA);
        Assert.Equal(88888u, entries[0].ColumnB);
    }

    [Fact]
    public void ExpScr_Parse_RawTail_Is10Bytes()
    {
        // Bytes +10..+19 of each record: UNVERIFIED tail exposed raw.
        // spec: Docs/RE/formats/config_tables.md §2.3 — "Remaining fields UNVERIFIED".
        byte[] data = BuildExpScr([(7, 0u, 0u)]);
        // Set tail bytes to recognisable values.
        data[10] = 0xDE;
        data[19] = 0xAD;

        ExpCurveEntry[] entries = ConfigTableParser.ParseExpScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(10, entries[0].RawTail.Length);
        Assert.Equal((byte)0xDE, entries[0].RawTail.Span[0]);
        Assert.Equal((byte)0xAD, entries[0].RawTail.Span[9]);
    }

    [Fact]
    public void ExpScr_Parse_NonMultipleStride_ThrowsInvalidData()
    {
        // If file_size % stride != 0, throw.
        // spec: Docs/RE/formats/config_tables.md §2.1 — stride-based count.
        byte[] badData = new byte[25]; // 25 % 20 != 0
        Assert.Throws<InvalidDataException>(() =>
            ConfigTableParser.ParseExpScr(new ReadOnlyMemory<byte>(badData)));
    }

    // =========================================================================
    // userlevel.scr tests — stride 60 bytes
    // =========================================================================

    private static byte[] BuildUserLevelScr((ushort level, byte[] body58)[] records)
    {
        // stride = 60 bytes per record; no header.
        // spec: Docs/RE/formats/config_tables.md §2.4 — stride 60 bytes: CONFIRMED.
        byte[] buf = new byte[records.Length * 60];
        for (int i = 0; i < records.Length; i++)
        {
            int off = i * 60;
            // Level u16 @ +0. CONFIRMED.
            WriteU16LE(buf, off, records[i].level);
            // Body 58 bytes @ +2. UNVERIFIED.
            if (records[i].body58.Length != 58)
                throw new ArgumentException("body must be 58 bytes");
            records[i].body58.CopyTo(buf, off + 2);
        }
        return buf;
    }

    [Fact]
    public void UserLevelScr_Parse_LevelKey()
    {
        // spec: Docs/RE/formats/config_tables.md §2.4 — "Level index u16 @ +0: CONFIRMED".
        var body = new byte[58];
        byte[] data = BuildUserLevelScr([(15, body)]);
        LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(new ReadOnlyMemory<byte>(data));

        Assert.Single(entries);
        Assert.Equal((ushort)15, entries[0].Level);
    }

    [Fact]
    public void UserLevelScr_Parse_Body_Is58Bytes()
    {
        // spec: Docs/RE/formats/config_tables.md §2.4 — "+2: 58 bytes UNVERIFIED".
        var body = new byte[58];
        body[0] = 0xEF;
        body[57] = 0x12;
        byte[] data = BuildUserLevelScr([(1, body)]);
        LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(58, entries[0].Body.Length);
        Assert.Equal((byte)0xEF, entries[0].Body.Span[0]);
        Assert.Equal((byte)0x12, entries[0].Body.Span[57]);
    }

    // =========================================================================
    // mobs.scr tests — stride 488 bytes
    // =========================================================================

    /// <summary>
    /// Builds a synthetic mobs.scr buffer.
    /// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr — stride 488 bytes: CONFIRMED.
    /// </summary>
    private static byte[] BuildMobsScr((ushort id, byte type)[] mobs)
    {
        // stride = 488 bytes per record; no header.
        // spec: Docs/RE/formats/config_tables.md §2.1 — "No file header": CONFIRMED.
        byte[] buf = new byte[mobs.Length * 488];
        for (int i = 0; i < mobs.Length; i++)
        {
            int off = i * 488;
            // Mob ID u16 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.9 — "Mob ID u16 @ +0: CONFIRMED".
            WriteU16LE(buf, off + 0, mobs[i].id);
            // Mob type u8 @ +324. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type (11=boss): CONFIRMED".
            buf[off + 324] = mobs[i].type;
            // All other bytes left as zero (UNVERIFIED fields).
        }
        return buf;
    }

    [Fact]
    public void MobsScr_Parse_RecordCount()
    {
        // spec: Docs/RE/formats/config_tables.md §2.1 — "record count = file_size / stride": CONFIRMED.
        byte[] data = BuildMobsScr([(1, 0), (2, 11), (3, 0)]);
        MobCatalogEntry[] entries = ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(data));
        Assert.Equal(3, entries.Length);
    }

    [Fact]
    public void MobsScr_Parse_MobId()
    {
        // spec: Docs/RE/formats/config_tables.md §2.9 — "Mob ID u16 @ +0: CONFIRMED".
        byte[] data = BuildMobsScr([(1234, 0)]);
        MobCatalogEntry[] entries = ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(data));
        Assert.Equal((ushort)1234, entries[0].Id);
    }

    [Fact]
    public void MobsScr_Parse_MobType_Boss()
    {
        // spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type (11=boss/elite): CONFIRMED".
        byte[] data = BuildMobsScr([(99, 11)]);
        MobCatalogEntry[] entries = ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((byte)11, entries[0].Type);
    }

    [Fact]
    public void MobsScr_Parse_MobType_NonBoss()
    {
        byte[] data = BuildMobsScr([(5, 0)]);
        MobCatalogEntry[] entries = ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((byte)0, entries[0].Type);
    }

    [Fact]
    public void MobsScr_Parse_RawRecord_Is488Bytes()
    {
        // Full raw record exposed; internal fields UNVERIFIED.
        byte[] data = BuildMobsScr([(7, 11)]);
        MobCatalogEntry[] entries = ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(488, entries[0].Raw.Length);
    }

    [Fact]
    public void MobsScr_Parse_NonMultipleStride_ThrowsInvalidData()
    {
        byte[] badData = new byte[490]; // 490 % 488 = 2
        Assert.Throws<InvalidDataException>(() =>
            ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(badData)));
    }

    [Fact]
    public void MobsScr_Parse_TypeField_AtOffset324()
    {
        // Verify the type byte is read from exactly offset +324 within each record.
        // spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type: CONFIRMED".
        byte[] data = new byte[488];
        WriteU16LE(data, 0, 42);
        data[324] = 11; // boss

        MobCatalogEntry[] entries = ConfigTableParser.ParseMobsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((ushort)42, entries[0].Id);
        Assert.Equal((byte)11, entries[0].Type);
    }

    // =========================================================================
    // items.scr tests — stride 548 + N×8
    // =========================================================================

    private static byte[] BuildItemsScr((byte trailingCount, byte categoryFlag1)[] items)
    {
        // Each main record is 548 bytes; followed by trailingCount × 8 bytes.
        // spec: Docs/RE/formats/config_tables.md §2.7 — stride 548 bytes + N×8: CONFIRMED.
        using var ms = new System.IO.MemoryStream();
        foreach (var (trailingCount, catFlag1) in items)
        {
            var rec = new byte[548];
            // category flag 1 (weapon) at +0xE5. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.7 — "+0xE5 u8 Category flag 1 (1=weapon): CONFIRMED".
            rec[0xE5] = catFlag1;
            // trailing count at +0x220. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §2.7 — "+0x220 u8 Trailing entry count N: CONFIRMED".
            rec[0x220] = trailingCount;
            ms.Write(rec);
            // trailing entries: trailingCount × 8 bytes (zeroed; fields UNVERIFIED).
            ms.Write(new byte[trailingCount * 8]);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ItemsScr_Parse_RecordCount()
    {
        // spec: Docs/RE/formats/config_tables.md §2.7 — stride 548 + variable trailing: CONFIRMED.
        byte[] data = BuildItemsScr([(0, 0), (0, 0)]);
        ItemCatalogEntry[] entries = ConfigTableParser.ParseItemsScr(new ReadOnlyMemory<byte>(data));
        Assert.Equal(2, entries.Length);
    }

    [Fact]
    public void ItemsScr_Parse_CategoryFlag1_Weapon()
    {
        // spec: Docs/RE/formats/config_tables.md §2.7 — "+0xE5 u8 Category flag 1 (1=weapon): CONFIRMED".
        byte[] data = BuildItemsScr([(0, 1)]); // catFlag1 = 1 = weapon
        ItemCatalogEntry[] entries = ConfigTableParser.ParseItemsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((byte)1, entries[0].CategoryFlag1);
    }

    [Fact]
    public void ItemsScr_Parse_TrailingEntries()
    {
        // spec: Docs/RE/formats/config_tables.md §2.7 — "N×8 trailing sub-entries": CONFIRMED.
        byte[] data = BuildItemsScr([(3, 0)]); // 3 trailing × 8 = 24 extra bytes
        ItemCatalogEntry[] entries = ConfigTableParser.ParseItemsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((byte)3, entries[0].TrailingCount);
        Assert.Equal(3, entries[0].TrailingEntries.Length);
        Assert.Equal(8, entries[0].TrailingEntries[0].Length);
    }

    [Fact]
    public void ItemsScr_Parse_RawRecord_Is548Bytes()
    {
        byte[] data = BuildItemsScr([(0, 0)]);
        ItemCatalogEntry[] entries = ConfigTableParser.ParseItemsScr(new ReadOnlyMemory<byte>(data));
        Assert.Equal(548, entries[0].RawRecord.Length);
    }

    [Fact]
    public void ItemsScr_Parse_EmptyBuffer_ReturnsEmpty()
    {
        ItemCatalogEntry[] entries = ConfigTableParser.ParseItemsScr(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(entries);
    }

    // =========================================================================
    // users.scr tests — fixed 496-byte block
    // =========================================================================

    [Fact]
    public void UsersScr_Parse_FixedSize496()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — "496-byte bulk block": CONFIRMED.
        byte[] data = new byte[UsersBlock.FixedSize];
        data[0] = 0xCA;
        data[UsersBlock.FixedSize - 1] = 0xFE;

        UsersBlock block = ConfigTableParser.ParseUsersScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(UsersBlock.FixedSize, block.RawData.Length);
        Assert.Equal((byte)0xCA, block.RawData.Span[0]);
        Assert.Equal((byte)0xFE, block.RawData.Span[UsersBlock.FixedSize - 1]);
    }

    [Fact]
    public void UsersScr_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooSmall = new byte[UsersBlock.FixedSize - 1];
        Assert.Throws<InvalidDataException>(() =>
            ConfigTableParser.ParseUsersScr(new ReadOnlyMemory<byte>(tooSmall)));
    }

    // =========================================================================
    // skills.scr tests — stride 1504 + N×8
    // =========================================================================

    private static byte[] BuildSkillsScr(byte trailingCount)
    {
        // Single record: 1504 bytes main + trailingCount × 8 trailing.
        // spec: Docs/RE/formats/config_tables.md §2.8 — "stride 1504 bytes + N×8": CONFIRMED.
        using var ms = new System.IO.MemoryStream();
        var rec = new byte[1504];
        // Trailing count at byte 1500 (0x5DC) — see spec ambiguity note in ConfigTableParser.
        rec[1500] = trailingCount;
        ms.Write(rec);
        ms.Write(new byte[trailingCount * 8]);
        return ms.ToArray();
    }

    [Fact]
    public void SkillsScr_Parse_SingleRecord_NoTrailing()
    {
        byte[] data = BuildSkillsScr(0);
        SkillCatalogEntry[] entries = ConfigTableParser.ParseSkillsScr(new ReadOnlyMemory<byte>(data));

        Assert.Single(entries);
        Assert.Equal((byte)0, entries[0].TrailingCount);
        Assert.Empty(entries[0].TrailingEntries);
    }

    [Fact]
    public void SkillsScr_Parse_TrailingEntries()
    {
        // spec: Docs/RE/formats/config_tables.md §2.8 — "trailing N×8 bytes": CONFIRMED.
        byte[] data = BuildSkillsScr(2);
        SkillCatalogEntry[] entries = ConfigTableParser.ParseSkillsScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((byte)2, entries[0].TrailingCount);
        Assert.Equal(2, entries[0].TrailingEntries.Length);
        Assert.Equal(8, entries[0].TrailingEntries[0].Length);
    }

    [Fact]
    public void SkillsScr_Parse_RawRecord_Is1504Bytes()
    {
        byte[] data = BuildSkillsScr(0);
        SkillCatalogEntry[] entries = ConfigTableParser.ParseSkillsScr(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1504, entries[0].RawRecord.Length);
    }

    [Fact]
    public void SkillsScr_Parse_EmptyBuffer_ReturnsEmpty()
    {
        SkillCatalogEntry[] entries = ConfigTableParser.ParseSkillsScr(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(entries);
    }
}
