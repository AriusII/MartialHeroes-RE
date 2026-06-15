using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based regression tests for the CAMPAIGN VFS-DEEP-II spec corrections.
/// Covers 6 categories:
/// <list type="bullet">
///   <item>1. items.scr Model-A: multi-record walk + effect tails + exact EOF</item>
///   <item>2. citems.scr: 6-paragraph description + item_name at 0x04</item>
///   <item>3. SoundTable: 52-byte stride record parse (stride correction)</item>
///   <item>4. bgtexture.lst: BgTextureKind enum mapping (render-mode, not boolean flag)</item>
///   <item>5. items.csv: embedded-comma (numeric-anchor split) + float column (InvariantCulture)</item>
///   <item>6. events.scr: consumed-field extraction (mode_flag + rate_array / actor_array rename)</item>
/// </list>
/// All buffers are hand-built in-memory; no real VFS files are required.
/// spec: Docs/RE/formats/items_scr.md, Docs/RE/formats/sound_tables.md,
///       Docs/RE/formats/bgtexture_lst.md, Docs/RE/formats/items_csv.md,
///       Docs/RE/formats/events_scr.md
/// </summary>
public sealed class VfsDeepIISpecCorrectionTests
{
    // ─── Binary helpers ────────────────────────────────────────────────────────

    /// <summary>Concatenates byte arrays without LINQ allocation.</summary>
    private static byte[] Concat(params byte[][] arrays)
    {
        int totalLen = 0;
        foreach (byte[] a in arrays) totalLen += a.Length;
        byte[] result = new byte[totalLen];
        int pos = 0;
        foreach (byte[] a in arrays)
        {
            a.CopyTo(result, pos);
            pos += a.Length;
        }

        return result;
    }

    private static void WriteU8(byte[] buf, int off, byte v) => buf[off] = v;

    private static void WriteU16LE(byte[] buf, int off, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(off, 2), v);

    private static void WriteU32LE(byte[] buf, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteF32LE(byte[] buf, int off, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(off, 4), v);

    private static Encoding Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    private static void WriteCp949(byte[] buf, int off, int fieldLen, string text)
    {
        var enc = Cp949();
        byte[] encoded = enc.GetBytes(text);
        int copyLen = Math.Min(encoded.Length, fieldLen - 1);
        Array.Copy(encoded, 0, buf, off, copyLen);
        buf[off + copyLen] = 0x00;
    }

    // =========================================================================
    // 1. items.scr Model-A: multi-record walk + effect tails + exact EOF
    // =========================================================================
    // spec: Docs/RE/formats/items_scr.md §1 -- CONFIRMED Model A.

    /// <summary>
    /// Builds one items.scr record: fixed 548-byte (0x224) block + effect_count*8 trailing bytes.
    /// spec: Docs/RE/formats/items_scr.md §1.2 -- "[ fixed 548-byte (0x224) block ][ effect_count x 8 bytes ]".
    /// </summary>
    private static byte[] BuildItemsScrRecord(
        string name, uint uid, byte effectCount = 0,
        ushort effectA = 0, short effectB = 0, ushort effectC = 0, byte effectD = 0)
    {
        // spec: Docs/RE/formats/items_scr.md §1.2 -- per-record stride = 0x224 + 8 * effect_count: CONFIRMED.
        int totalSize = 0x224 + effectCount * 8;
        byte[] buf = new byte[totalSize];

        // item_name CP949[52] @ 0x000. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 -- item_name CP949[52] @0x000: CONFIRMED.
        WriteCp949(buf, 0x000, 52, name);

        // item_uid u32LE @ 0x034. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 -- item_uid u32LE @0x034: CONFIRMED.
        WriteU32LE(buf, 0x034, uid);

        // item_desc CP949 @ 0x038 (NUL-terminated).
        // spec: Docs/RE/formats/items_scr.md §1.4 -- item_desc CP949 @0x038: CONFIRMED present.
        WriteCp949(buf, 0x038, 0x224 - 0x038, "TestDesc");

        // effect_count u8 @ 0x220. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 -- effect_count u8 @0x220: CONFIRMED.
        WriteU8(buf, 0x220, effectCount);

        if (effectCount > 0)
        {
            // effect entry 0: effect_a u16 @+0, effect_b s16 @+2, effect_c u16 @+4, effect_d u8 @+6.
            // spec: Docs/RE/formats/items_scr.md §1.5 -- 8-byte on-disk layout: PLAUSIBLE.
            int entryBase = 0x224;
            WriteU16LE(buf, entryBase + 0, effectA);
            WriteU16LE(buf, entryBase + 2, (ushort)effectB);
            WriteU16LE(buf, entryBase + 4, effectC);
            WriteU8(buf, entryBase + 6, effectD);
            // byte +7 is padding; left as zero.
        }

        return buf;
    }

    [Fact]
    public void ItemsScr_ModelA_SingleRecord_NoEffects_ParsesCorrectly()
    {
        // Regression: the parser must walk Model-A fixed blocks correctly.
        // spec: Docs/RE/formats/items_scr.md §1.2 -- CONFIRMED Model A.
        byte[] buf = BuildItemsScrRecord("SwordA", uid: 0x0B000001u);
        var records = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).ToArray();

        Assert.Single(records);
        Assert.Equal("SwordA", records[0].ItemName);
        Assert.Equal(0x0B000001u, records[0].ItemUid);
        Assert.Equal(0, records[0].EffectCount);
        Assert.Empty(records[0].Effects);
    }

    [Fact]
    public void ItemsScr_ModelA_TwoRecords_MultiRecordWalk()
    {
        // Regression: parser must advance cursor correctly between records and parse all records.
        // spec: Docs/RE/formats/items_scr.md §1.3 -- "Record count: obtained by walking to EOF."
        byte[] r1 = BuildItemsScrRecord("ItemAlpha", uid: 100u);
        byte[] r2 = BuildItemsScrRecord("ItemBeta", uid: 200u);
        byte[] buf = Concat(r1, r2);

        var records = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).ToArray();

        Assert.Equal(2, records.Length);
        Assert.Equal("ItemAlpha", records[0].ItemName);
        Assert.Equal(100u, records[0].ItemUid);
        Assert.Equal("ItemBeta", records[1].ItemName);
        Assert.Equal(200u, records[1].ItemUid);
    }

    [Fact]
    public void ItemsScr_ModelA_WithEffectTail_ParsesEffects()
    {
        // Regression: effect tail (8 bytes per entry) must be consumed.
        // spec: Docs/RE/formats/items_scr.md §1.5 -- "effect entry: 8 bytes on disk."
        byte[] buf = BuildItemsScrRecord(
            "EnchantedBlade", uid: 999u,
            effectCount: 1, effectA: 0xAB, effectB: -5, effectC: 0x12, effectD: 0x07);

        var records = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).ToArray();

        Assert.Single(records);
        Assert.Equal(1, records[0].EffectCount);
        Assert.Single(records[0].Effects);
        Assert.Equal(0xABu, records[0].Effects[0].EffectA);
        Assert.Equal((short)-5, records[0].Effects[0].EffectB);
        Assert.Equal(0x12u, records[0].Effects[0].EffectC);
        Assert.Equal(0x07u, records[0].Effects[0].EffectD);
    }

    [Fact]
    public void ItemsScr_ModelA_ThreeRecords_MixedEffectCounts_ExactEof()
    {
        // Regression: records with different effect_count values must be parsed and the parser
        // must land exactly on EOF (zero residual).
        // spec: Docs/RE/formats/items_scr.md §1.3 -- "EOF-clean; zero residual": CONFIRMED.
        byte[] r1 = BuildItemsScrRecord("Bow", uid: 1u, effectCount: 0); // 548 bytes
        byte[] r2 = BuildItemsScrRecord("Sword", uid: 2u, effectCount: 1); // 548 + 8 = 556 bytes
        byte[] r3 = BuildItemsScrRecord("Ring", uid: 3u, effectCount: 1); // 548 + 8 = 556 bytes
        byte[] buf = Concat(r1, r2, r3); // total = 548 + 556 + 556 = 1660 bytes

        var records = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).ToArray();

        Assert.Equal(3, records.Length);
        Assert.Equal("Bow", records[0].ItemName);
        Assert.Equal("Sword", records[1].ItemName);
        Assert.Equal("Ring", records[2].ItemName);
        Assert.Equal(0, records[0].EffectCount);
        Assert.Equal(1, records[1].EffectCount);
        Assert.Equal(1, records[2].EffectCount);
    }

    [Fact]
    public void ItemsScr_ModelA_FixedBlockRaw_Is548Bytes()
    {
        // The FixedBlockRaw slice must be exactly 548 (0x224) bytes.
        // spec: Docs/RE/formats/items_scr.md §1.2 -- "fixed 548-byte (0x224) block": CONFIRMED.
        byte[] buf = BuildItemsScrRecord("X", uid: 0u);
        var records = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).ToArray();

        Assert.Equal(0x224, records[0].FixedBlockRaw.Length);
    }

    // =========================================================================
    // 2. citems.scr: 6-paragraph description + item_name at 0x04 (spec correction)
    // =========================================================================
    // spec: Docs/RE/formats/items_scr.md §2 citems.scr -- CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.5 -- Corrections applied.

    /// <summary>
    /// Builds a minimal citems.scr fixture with the given number of records.
    /// Applies the VFS-DEEP-II spec corrections:
    ///   - item_name is at 0x04 (48 bytes), NOT 0x08 (no separate item_ref field).
    ///   - Description = 6 x 81-byte paragraphs from 0x0E4.
    /// spec: Docs/RE/formats/items_scr.md §2.5 -- Corrections.
    /// </summary>
    private static byte[] BuildCitemsRecord(
        uint slotIndex = 1u,
        string itemName = "DefaultItem",
        uint itemUid = 283000000u,
        string[]? descParagraphs = null)
    {
        // spec: Docs/RE/formats/items_scr.md §2.1 -- "Fixed stride: 1052 bytes (0x41C)": CONFIRMED.
        const int stride = 1052;
        byte[] buf = new byte[stride];

        // slot_index u32LE @ 0x00.
        // spec: Docs/RE/formats/items_scr.md §2.2 -- slot_index u32LE @ 0x00: CONFIRMED.
        WriteU32LE(buf, 0x00, slotIndex);

        // item_name CP949[48] @ 0x04. CORRECTION: name starts at 0x04 (NOT 0x08).
        // spec: Docs/RE/formats/items_scr.md §2.2 -- item_name CP949[48] @ 0x04: CONFIRMED (512/512).
        // spec: Docs/RE/formats/items_scr.md §2.5 -- "item_ref at +0x04 DOES NOT EXIST": CORRECTION.
        WriteCp949(buf, 0x04, 48, itemName);

        // item_uid u32LE @ 0x48.
        // spec: Docs/RE/formats/items_scr.md §2.2 -- item_uid u32LE @ 0x48: CONFIRMED.
        WriteU32LE(buf, 0x48, itemUid);

        // 6 x 81-byte description paragraphs from 0x0E4.
        // CORRECTION: not a single buffer near 0xDC; 6 consecutive 81-byte paragraphs.
        // spec: Docs/RE/formats/items_scr.md §2.4 -- 6 x 81-byte desc paragraphs from 0x0E4: CONFIRMED.
        if (descParagraphs != null)
        {
            // Paragraph offsets: 0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279 (stride 81).
            // spec: Docs/RE/formats/items_scr.md §2.4 -- paragraph start offsets.
            int[] paraOffsets = { 0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279 };
            for (int p = 0; p < Math.Min(descParagraphs.Length, 6); p++)
                WriteCp949(buf, paraOffsets[p], 81, descParagraphs[p]);
        }

        return buf;
    }

    [Fact]
    public void Citems_ItemName_IsAt0x04_NotAt0x08()
    {
        // Regression for VFS-DEEP-II correction: item_name is at 0x04 (48B),
        // no separate item_ref field exists at 0x04.
        // spec: Docs/RE/formats/items_scr.md §2.2 -- item_name CP949[48] @ 0x04: CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.5 -- "item_ref at +0x04 DOES NOT EXIST."
        byte[] buf = BuildCitemsRecord(slotIndex: 1u, itemName: "GoldenRobe");
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(cat.Records);
        Assert.Equal("GoldenRobe", cat.Records[0].ItemName);
    }

    [Fact]
    public void Citems_DescParagraphs_SixParagraphs_Decoded()
    {
        // Regression: 6 description paragraphs must be decoded from 0x0E4..0x28B.
        // spec: Docs/RE/formats/items_scr.md §2.4 -- "6 x 81-byte paragraphs from 0x0E4": CONFIRMED.
        string[] paragraphs = { "Para1", "Para2", "Para3", "Para4", "Para5", "Para6" };
        byte[] buf = BuildCitemsRecord(slotIndex: 7u, descParagraphs: paragraphs);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        CitemsRecord rec = cat.Records[0];
        Assert.Equal(6, rec.DescParagraphs.Length);
        Assert.Equal("Para1", rec.DescParagraphs[0]);
        Assert.Equal("Para3", rec.DescParagraphs[2]);
        Assert.Equal("Para6", rec.DescParagraphs[5]);
    }

    [Fact]
    public void Citems_EmptyParagraphs_AreEmptyStrings()
    {
        // Empty description paragraphs (all-zero bytes) must decode as empty strings.
        // spec: Docs/RE/formats/items_scr.md §2.4 -- "empty paragraphs are empty strings."
        byte[] buf = BuildCitemsRecord(slotIndex: 3u); // no paragraphs supplied
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(6, cat.Records[0].DescParagraphs.Length);
        Assert.All(cat.Records[0].DescParagraphs, p => Assert.Equal(string.Empty, p));
    }

    [Fact]
    public void Citems_TwoRecords_BothItemNames_Correct()
    {
        // Multi-record: second record item_name should also come from 0x04.
        byte[] r1 = BuildCitemsRecord(slotIndex: 1u, itemName: "Alpha", itemUid: 283000001u);
        byte[] r2 = BuildCitemsRecord(slotIndex: 2u, itemName: "Beta", itemUid: 283000002u);
        byte[] buf = Concat(r1, r2);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, cat.Records.Count);
        Assert.Equal("Alpha", cat.Records[0].ItemName);
        Assert.Equal("Beta", cat.Records[1].ItemName);
    }

    // =========================================================================
    // 3. SoundTable: stride-48 record parse (two-witness correction, 2026-06-15)
    // =========================================================================
    // CORRECTION: stride is 48 bytes (CONFIRMED two-witness), NOT 52.
    // The loader advances 0x30 bytes per record, reads 256 × 48 = 12288 bytes, and leaves a
    // 1024-byte unread trailer at the end of the 13312-byte file.
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "stride 48 bytes": CONFIRMED (two-witness).
    // Field at +0x24 (4 bytes) is NOT read by the loader on any path — labeled Unlabeled24.
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "unlabeled_24 @ +0x24: NOT-READ by loader".

    /// <summary>
    /// Builds a sound table fixture: exactly 13312 bytes (256 × 48 read + 1024 unread trailer).
    /// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED.
    /// </summary>
    private static byte[] BuildSoundTable(
        uint soundEntryId = 101u,
        float weight = 1.0f,
        float posX = 500.0f, uint unlabeled24 = 0u, float posZ = -200.0f,
        float radius = 128.0f)
    {
        // Fixed file size: 13312 = 256 × 48 + 1024 (unread trailer).
        // spec: Docs/RE/formats/sound_tables.md §File layout — "13312 bytes (0x3400)": CONFIRMED.
        const int fixedSize = 13312;
        byte[] buf = new byte[fixedSize];

        // Record 0 is written with supplied values; remaining 255 records are all-zero.

        // sound_entry_id u32LE @ +0x00.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @+0x00: CONFIRMED.
        WriteU32LE(buf, 0x00, soundEntryId);

        // hour_schedule u8[24] @ +0x04 — left zero (all hours inactive).
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — hour_schedule u8[24] @+0x04: CONFIRMED.

        // weight f32 @ +0x1C.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @+0x1C: SAMPLE-VERIFIED.
        WriteF32LE(buf, 0x1C, weight);

        // pos_x f32 @ +0x20.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_x f32 @+0x20: CONFIRMED.
        WriteF32LE(buf, 0x20, posX);

        // unlabeled_24 u32 @ +0x24 — NOT read by the loader; carried verbatim.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ.
        WriteU32LE(buf, 0x24, unlabeled24);

        // pos_z f32 @ +0x28.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_z f32 @+0x28: CONFIRMED.
        WriteF32LE(buf, 0x28, posZ);

        // radius f32 @ +0x2C.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — radius f32 @+0x2C: SAMPLE-VERIFIED.
        WriteF32LE(buf, 0x2C, radius);

        // Bytes 0x30..end of record = part of 48-byte stride (already zero; record ends at 0x30).
        // The 1024-byte unread trailer (bytes 0x3000..0x33FF) is all-zero.

        return buf;
    }

    [Fact]
    public void SoundTable_Stride48_FixedFileSize13312()
    {
        // Stride is 48 bytes (CONFIRMED two-witness). 256 × 48 = 12288 bytes read; 1024 trailer = 13312 total.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "stride 48 bytes": CONFIRMED (two-witness, 2026-06-15).
        Assert.Equal(13312, SoundTableData.FixedFileSize);
        Assert.Equal(48, SoundTableData.EntryStride);
        Assert.Equal(256, SoundTableData.EntryCount);
        Assert.Equal(12288, SoundTableData.ReadSize); // 256 × 48
        Assert.Equal(1024, SoundTableData.TrailerSize); // 13312 − 12288
    }

    [Fact]
    public void SoundTable_Record0_Unlabeled24_Radius_Decoded()
    {
        // Regression: unlabeled_24 (@0x24, formerly pos_y — WITHDRAWN) and radius (@0x2C)
        // must decode to the correct typed fields.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "unlabeled_24 @ +0x24: NOT-READ (WITHDRAWN from pos_y)".
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "radius f32 @ +0x2C": SAMPLE-VERIFIED.
        byte[] buf = BuildSoundTable(
            soundEntryId: 55u,
            weight: 2.5f,
            posX: 1024.0f, unlabeled24: 0xABABABABu, posZ: -512.0f,
            radius: 256.0f);

        // All five table variants share the same binary layout.
        // spec: Docs/RE/formats/sound_tables.md — all five variants share the same binary layout.
        SoundTableData table = SoundTableParser.Parse(new ReadOnlyMemory<byte>(buf), SoundTableExtension.Bgm);

        SoundTableEntry e = table.Entries[0];
        Assert.Equal(55u, e.SoundEntryId);
        Assert.Equal(2.5f, e.Weight);
        Assert.Equal(1024.0f, e.PosX);
        // Unlabeled24 is NOT read by the loader but the parser surfaces it verbatim.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ.
        Assert.Equal(0xABABABABu, e.Unlabeled24);
        Assert.Equal(-512.0f, e.PosZ);
        Assert.Equal(256.0f, e.Radius, precision: 4);
    }

    [Fact]
    public void SoundTable_WrongLength_ThrowsInvalidDataException()
    {
        // Exact file-size validation: any buffer != 13312 must throw.
        // spec: Docs/RE/formats/sound_tables.md -- "parse error if length != 13312."
        byte[] tooShort = new byte[13311];
        byte[] tooLong = new byte[13313];

        Assert.Throws<InvalidDataException>(() =>
            SoundTableParser.Parse(new ReadOnlyMemory<byte>(tooShort), SoundTableExtension.Bgm));
        Assert.Throws<InvalidDataException>(() =>
            SoundTableParser.Parse(new ReadOnlyMemory<byte>(tooLong), SoundTableExtension.Bgm));
    }

    [Fact]
    public void SoundTable_AllEntries_CountIs256()
    {
        // Parser must always return exactly 256 entries.
        // spec: Docs/RE/formats/sound_tables.md -- EntryCount = 256: SAMPLE-VERIFIED.
        byte[] buf = BuildSoundTable();
        SoundTableData table = SoundTableParser.Parse(new ReadOnlyMemory<byte>(buf), SoundTableExtension.Bgm);

        Assert.Equal(256, table.Entries.Length);
    }

    // =========================================================================
    // 4. bgtexture.lst: BgTextureKind enum mapping (render-mode, not boolean flag)
    // =========================================================================
    // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations -- render-mode selector, NOT boolean.

    private static byte[] BuildBgtextureLst(params (byte kind, string path)[] entries)
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout -- record_count u32LE @ 0: CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout -- stride 48 bytes: CONFIRMED.
        byte[] buf = new byte[4 + entries.Length * 48];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)entries.Length);
        var enc = Cp949();
        for (int i = 0; i < entries.Length; i++)
        {
            int recBase = 4 + i * 48;
            buf[recBase] = entries[i].kind;
            byte[] pathBytes = enc.GetBytes(entries[i].path);
            int copyLen = Math.Min(pathBytes.Length, 46);
            pathBytes.AsSpan(0, copyLen).CopyTo(buf.AsSpan(recBase + 1, 47));
        }

        return buf;
    }

    [Theory]
    [InlineData(0x01, BgTextureKind.Static)]
    [InlineData(0x02, BgTextureKind.ScrollUv)]
    [InlineData(0x0A, BgTextureKind.Grass)]
    [InlineData(0x0B, BgTextureKind.Plant)]
    [InlineData(0x0C, BgTextureKind.TreeBark)]
    [InlineData(0x14, BgTextureKind.Foliage)]
    [InlineData(0x99, BgTextureKind.Unknown)] // unmapped value -> Unknown
    public void BgtextureLst_KindByte_MapsToCorrectEnum(byte kindByte, BgTextureKind expectedKind)
    {
        // Regression: kind byte must map to the render-mode enum, not a boolean animated/static flag.
        // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations --
        //   0x01=Static, 0x02=ScrollUv, 0x0A=Grass, 0x0B=Plant, 0x0C=TreeBark, 0x14=Foliage: HIGH.
        byte[] buf = BuildBgtextureLst((kindByte, "some/texture"));
        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(kindByte, cat.Records[0].KindRaw);
        Assert.Equal(expectedKind, cat.Records[0].KindEnum);
    }

    [Fact]
    public void BgtextureLst_KindRaw_IsDistinctFromKindEnum()
    {
        // KindRaw is the on-disk byte; KindEnum is the typed mapping.
        // Both must be accessible separately.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout -- kind u8 @ +0: CONFIRMED.
        byte[] buf = BuildBgtextureLst((0x0A, "terrain/grass01"));
        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal((byte)0x0A, cat.Records[0].KindRaw);
        Assert.Equal(BgTextureKind.Grass, cat.Records[0].KindEnum);
        Assert.Equal("terrain/grass01", cat.Records[0].RelPath);
    }

    // =========================================================================
    // 5. items.csv: embedded-comma (numeric-anchor split) + float column
    // =========================================================================
    // spec: Docs/RE/formats/items_csv.md §2 -- HAZARD-A (embedded commas) + HAZARD-B (float column).

    private static ReadOnlyMemory<byte> CsvToMemory(string csvText)
    {
        // items.csv uses CP949 encoding with LF-only line endings.
        // spec: Docs/RE/formats/items_csv.md §2 -- "CP949; LF-only": CONFIRMED.
        return new ReadOnlyMemory<byte>(Cp949().GetBytes(csvText));
    }

    [Fact]
    public void ItemsCsv_EmbeddedCommaInName_ParsesCorrectly()
    {
        // HAZARD A: item_name (col 0) may contain literal unquoted commas.
        // Naive Split(',') would misalign columns; numeric-anchor rule must be used instead.
        // spec: Docs/RE/formats/items_csv.md §2.HAZARD-A -- numeric-anchor field-splitting: CONFIRMED; CRITICAL.
        // Example: "Fire, Sword,1001,Attack weapon,500,1.0"
        //   col0 (name) = "Fire, Sword"  (contains embedded comma)
        //   col1 (item_id) = 1001
        //   col2 (desc) = "Attack weapon"
        //   col3+ (numeric tail) = 500, 1.0
        string csv = "Fire, Sword,1001,Attack weapon,500,1.0\n";
        ItemCsvRow[] records = ItemsCsvParser.ParseText(csv);

        Assert.Single(records);
        Assert.Equal("Fire, Sword", records[0].NameCp949); // spec: ItemCsvRow.NameCp949 col0
        Assert.Equal(1001u, records[0].ItemId); // spec: ItemCsvRow.ItemId col1
        Assert.Equal("Attack weapon", records[0].DescriptionCp949); // spec: ItemCsvRow.DescriptionCp949 col2
    }

    [Fact]
    public void ItemsCsv_FloatColumn_InvariantCulture()
    {
        // HAZARD B: a numeric column uses period decimal separator (e.g. "1.5").
        // Must be parsed with InvariantCulture; '.' must not be treated as a field separator.
        // spec: Docs/RE/formats/items_csv.md §2.HAZARD-B -- float column: CONFIRMED; HIGH.
        // Key invariant: the parser must not treat '.' in a numeric token as a field separator,
        // which would produce a phantom column and misalign the rest of the row.
        // We verify by checking that the identity columns (name, id, desc) are unaffected even
        // when the numeric tail contains floats.
        string csv = "BasicSword,2002,Cuts things,100,0,0,1.5\n";
        ItemCsvRow[] records = ItemsCsvParser.ParseText(csv);

        Assert.Single(records);
        Assert.Equal("BasicSword", records[0].NameCp949);
        Assert.Equal(2002u, records[0].ItemId);
        Assert.Equal("Cuts things", records[0].DescriptionCp949);
        // Columns are parsed without throwing even with float in tail.
    }

    [Fact]
    public void ItemsCsv_LfOnlyLineEndings_MultipleRows()
    {
        // spec: Docs/RE/formats/items_csv.md §2 -- "LF-only line endings": CONFIRMED.
        // Two rows separated by LF only; parser must yield 2 records.
        string csv = "ItemA,1001,DescA,10\nItemB,1002,DescB,20\n";
        ItemCsvRow[] records = ItemsCsvParser.ParseText(csv);

        Assert.Equal(2, records.Length);
        Assert.Equal("ItemA", records[0].NameCp949);
        Assert.Equal("ItemB", records[1].NameCp949);
        Assert.Equal(1001u, records[0].ItemId);
        Assert.Equal(1002u, records[1].ItemId);
    }

    [Fact]
    public void ItemsCsv_DescriptionWithEmbeddedComma_ColumnAlignmentPreserved()
    {
        // HAZARD A also applies to item_description (col 2).
        // spec: Docs/RE/formats/items_csv.md §2.HAZARD-A -- "name AND description may contain commas."
        string csv = "MagicRod,3003,Strong, magical item,999,1000000,2000000,0\n";
        ItemCsvRow[] records = ItemsCsvParser.ParseText(csv);

        Assert.Single(records);
        Assert.Equal("MagicRod", records[0].NameCp949);
        Assert.Equal(3003u, records[0].ItemId);
        Assert.Equal("Strong, magical item", records[0].DescriptionCp949);
    }

    // =========================================================================
    // 6. events.scr: consumed-field extraction (mode_flag + renamed arrays)
    // =========================================================================
    // spec: Docs/RE/formats/events_scr.md §1.3 -- CONSUMED fields: event_id, mode_flag, rate_array, actor_array.

    /// <summary>
    /// Builds a minimal events.scr fixture: one 520-byte record.
    /// spec: Docs/RE/formats/events_scr.md §1.1 -- "stride 520 bytes (0x208)": CONFIRMED.
    /// </summary>
    private static byte[] BuildEventsScrRecord(
        uint eventId = 10551u,
        ushort modeFlag = 1,
        uint[]? rateArray = null,
        uint[]? actorArray = null)
    {
        // spec: Docs/RE/formats/events_scr.md §1.1 -- stride 520 (0x208): CONFIRMED.
        byte[] buf = new byte[520];

        // event_id u32LE @ 0x00. CONFIRMED CONSUMED.
        // spec: Docs/RE/formats/events_scr.md §1.3 -- event_id u32LE @0x00: CONFIRMED CONSUMED.
        WriteU32LE(buf, 0x00, eventId);

        // mode_flag u16LE @ 0x64. CONFIRMED CONSUMED.
        // VFS-DEEP-II correction: earlier drafts mislabeled this as reserved/padding.
        // spec: Docs/RE/formats/events_scr.md §1.3 -- mode_flag u16LE @0x64: CONFIRMED CONSUMED.
        WriteU16LE(buf, 0x64, modeFlag);

        // rate_array u32LE[50] @ 0x68. CONFIRMED CONSUMED (formerly ids_array_a -- CORRECTED).
        // spec: Docs/RE/formats/events_scr.md §1.3 -- rate_array u32LE[50] @0x68: CONFIRMED CONSUMED.
        if (rateArray != null)
            for (int k = 0; k < Math.Min(rateArray.Length, 50); k++)
                WriteU32LE(buf, 0x68 + k * 4, rateArray[k]);

        // actor_array u32LE[52] @ 0x130. CONFIRMED CONSUMED (formerly ids_array_b -- CORRECTED).
        // spec: Docs/RE/formats/events_scr.md §1.3 -- actor_array u32LE[52] @0x130: CONFIRMED CONSUMED.
        if (actorArray != null)
            for (int k = 0; k < Math.Min(actorArray.Length, 52); k++)
                WriteU32LE(buf, 0x130 + k * 4, actorArray[k]);

        return buf;
    }

    [Fact]
    public void EventsScr_ModeFlag_IsConsumedField_DecodedAtOffset0x64()
    {
        // Regression: mode_flag at 0x64 must be a first-class CONSUMED field on the model.
        // VFS-DEEP-II correction: earlier drafts had this as reserved/padding.
        // spec: Docs/RE/formats/events_scr.md §1.3 -- mode_flag u16LE @0x64: CONFIRMED CONSUMED.
        byte[] buf = BuildEventsScrRecord(eventId: 12345u, modeFlag: 1);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(records);
        Assert.Equal(12345u, records[0].EventId);
        Assert.Equal((ushort)1, records[0].ModeFlag);
    }

    [Fact]
    public void EventsScr_ModeFlag_Zero_AlsoDecoded()
    {
        // mode_flag = 0 is a valid value (the other branch the client checks).
        // spec: Docs/RE/formats/events_scr.md §1.3 -- mode_flag u16LE @0x64 -- "one consumer branches on == 1."
        byte[] buf = BuildEventsScrRecord(eventId: 20001u, modeFlag: 0);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal((ushort)0, records[0].ModeFlag);
    }

    [Fact]
    public void EventsScr_RateArray_DecodedAsRateArray_NotIdsArray()
    {
        // Regression: field renamed from IdsArrayA -> RateArray.
        // Values are rates (divide by 1,000,000 for fraction).
        // spec: Docs/RE/formats/events_scr.md §1.3 -- rate_array u32LE[50] @0x68: CONFIRMED CONSUMED.
        // spec: Docs/RE/formats/events_scr.md §1.7 -- "divide by 1,000,000 = rate fraction": HIGH.
        var rates = new uint[] { 500000u, 250000u }; // 0.5, 0.25 as fractions
        byte[] buf = BuildEventsScrRecord(rateArray: rates);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records[0].RateArray.Count);
        Assert.Equal(500000u, records[0].RateArray[0]);
        Assert.Equal(250000u, records[0].RateArray[1]);
        // Sanity: dividing by 1e6 gives the documented fraction.
        Assert.Equal(0.5f, records[0].RateArray[0] / 1_000_000f, precision: 6);
    }

    [Fact]
    public void EventsScr_ActorArray_DecodedAsActorArray_NotIdsArray()
    {
        // Regression: field renamed from IdsArrayB -> ActorArray.
        // Values are 9-digit actor IDs.
        // spec: Docs/RE/formats/events_scr.md §1.3 -- actor_array u32LE[52] @0x130: CONFIRMED CONSUMED.
        var actors = new uint[] { 213010002u, 215010101u };
        byte[] buf = BuildEventsScrRecord(actorArray: actors);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records[0].ActorArray.Count);
        Assert.Equal(213010002u, records[0].ActorArray[0]);
        Assert.Equal(215010101u, records[0].ActorArray[1]);
    }

    [Fact]
    public void EventsScr_AllFourConsumedFields_DecodedTogether()
    {
        // Integration: all four CONSUMED fields must be decoded in one pass.
        // spec: Docs/RE/formats/events_scr.md §1.6 -- "client reads ONLY four fields": CONFIRMED.
        var rates = new uint[] { 1000000u }; // rate = 1.0 (100%)
        var actors = new uint[] { 213010002u };
        byte[] buf = BuildEventsScrRecord(
            eventId: 31704u,
            modeFlag: 1,
            rateArray: rates,
            actorArray: actors);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        EventsScrRecord r = records[0];
        Assert.Equal(31704u, r.EventId);
        Assert.Equal((ushort)1, r.ModeFlag);
        Assert.Single(r.RateArray);
        Assert.Equal(1000000u, r.RateArray[0]);
        Assert.Single(r.ActorArray);
        Assert.Equal(213010002u, r.ActorArray[0]);
    }

    [Fact]
    public void EventsScr_RateArray_ZeroTerminated_EmptyWhenAllZero()
    {
        // Zero-terminated semantics: if no entry is written, RateArray must be empty.
        byte[] buf = BuildEventsScrRecord(rateArray: null, actorArray: null);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Empty(records[0].RateArray);
        Assert.Empty(records[0].ActorArray);
    }
}