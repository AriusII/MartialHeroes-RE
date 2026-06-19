using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

// spec: Docs/RE/formats/items_scr.md §2.4 (citems: 10 paragraphs, 0x0E4 + i*81, #-sentinel, GetParagraph <10)
// spec: Docs/RE/formats/items_scr.md §1.4.2 (+0x80 → g%d.skn, +0x84 → pool id)
// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations (kind==1 Static / !=1 ScrollAnimated)
//       §Header (count [1,2000), 48-byte stride, size = 4 + count*48)
// spec: Docs/RE/formats/actormotion.md (cols 15..23 = motion_ids_a; cols 24..32 = motion_ids_b; col2 = skin_class)
// spec: Docs/RE/formats/xdb_tables.md §5 (creature_item: 48-byte stride, item_id @+4)

/// <summary>
/// Synthetic-byte regression tests for Assets.Parsers.
/// Every expected value is derived from a committed spec; cited inline.
/// </summary>
public sealed class ParserRegressionTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // CitemsParser
    // spec: Docs/RE/formats/items_scr.md §2 citems.scr
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CitemsParser_SingleRecord_DescParaCount_UpTo10()
    {
        // spec: Docs/RE/formats/items_scr.md §2.4 — desc_para_count = 10 structural capacity, CONFIRMED
        // Build a minimal 1052-byte record (RecordStride = 0x41C = 1052).
        // spec: Docs/RE/formats/items_scr.md §2.1 — fixed stride 1052 bytes (0x41C): CONFIRMED
        byte[] record = new byte[1052];

        // item_id u32LE @ 0x00
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x00), 1001);

        // item_name CP949[48] @ 0x04 — ASCII "TestItem"
        byte[] nameBytes = Encoding.ASCII.GetBytes("TestItem");
        nameBytes.CopyTo(record, 0x04);

        // Place 5 real paragraphs at offsets 0x0E4 + i*81 (i=0..4)
        // spec: Docs/RE/formats/items_scr.md §2.4 — desc_para[i] start = 0x0E4 + i * 81: CONFIRMED
        const int descBase = 0x0E4;
        const int paraWidth = 81; // spec: §2.4 — 81 bytes per paragraph: CONFIRMED
        for (int i = 0; i < 5; i++)
        {
            int off = descBase + i * paraWidth;
            record[off] = (byte)'P'; // non-sentinel first byte
            record[off + 1] = (byte)('0' + i); // distinguish each paragraph
        }

        // Paragraph 5 (index 5) starts with '#' — early-terminate sentinel
        // spec: Docs/RE/formats/items_scr.md §2.4 — stop at the first '#'-sentinel paragraph (first byte '#')
        int sentinelOff = descBase + 5 * paraWidth;
        record[sentinelOff] = (byte)'#';

        var catalog = CitemsParser.Parse(record);
        Assert.Single(catalog.Records);

        var rec = catalog.Records[0];
        Assert.Equal(1001u, rec.ItemId);

        // Paragraphs 0..4 must be populated (5 paragraphs before the sentinel)
        Assert.Equal(5, rec.DescParagraphs.Length);

        // GetParagraph(10) is out-of-bounds — returns null
        // spec: Docs/RE/formats/items_scr.md §2.4 — consumer paragraph accessor bounds index < 10
        Assert.Null(rec.GetParagraph(10));
        Assert.Null(rec.GetParagraph(11));

        // GetParagraph(0..4) return non-null
        for (int i = 0; i < 5; i++)
            Assert.NotNull(rec.GetParagraph(i));

        // GetParagraph(5..9) returns null (sentinel stopped at index 5)
        for (int i = 5; i < 10; i++)
            Assert.Null(rec.GetParagraph(i));
    }

    [Fact]
    public void CitemsParser_AllTenParagraphs_NullGetParagraph10()
    {
        // spec: Docs/RE/formats/items_scr.md §2.4 — structural capacity = 10; GetParagraph(10) always null
        byte[] record = new byte[1052];
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x00), 5);

        // All 10 paragraphs non-sentinel
        const int descBase = 0x0E4;
        const int paraWidth = 81;
        for (int i = 0; i < 10; i++)
        {
            int off = descBase + i * paraWidth;
            record[off] = (byte)'A'; // non-sentinel
        }

        var catalog = CitemsParser.Parse(record);
        var rec = catalog.Records[0];

        // All 10 must be populated
        Assert.Equal(10, rec.DescParagraphs.Length);

        // Index 9 is valid (last); index 10 is OOB
        Assert.NotNull(rec.GetParagraph(9));
        Assert.Null(rec.GetParagraph(10)); // spec: §2.4 — bounds index < 10
    }

    [Fact]
    public void CitemsParser_FirstParagraphIsSentinel_ZeroDescParagraphs()
    {
        // spec: Docs/RE/formats/items_scr.md §2.4 — '#'-sentinel at index 0 → 0 paragraphs emitted
        byte[] record = new byte[1052];
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x00), 7);

        const int descBase = 0x0E4;
        record[descBase] = (byte)'#'; // sentinel at first paragraph

        var catalog = CitemsParser.Parse(record);
        var rec = catalog.Records[0];

        Assert.Empty(rec.DescParagraphs);
        Assert.Null(rec.GetParagraph(0)); // no paragraph 0 — early-terminated
    }

    [Fact]
    public void CitemsParser_InvalidStride_Throws()
    {
        // spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052, must be exact"
        byte[] bad = new byte[1051]; // not divisible by 1052
        Assert.Throws<InvalidDataException>(() => CitemsParser.Parse(bad));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ItemsScrParser
    // spec: Docs/RE/formats/items_scr.md §1 — Model A
    // spec: Docs/RE/formats/items_scr.md §1.4.2 (+0x80 model_ref_key, +0x84 anim_ref_key)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ItemsScrParser_ModelRefKey_AnimRefKey_PathDerivation()
    {
        // spec: Docs/RE/formats/items_scr.md §1.4.2 —
        //   +0x080 = model_ref_key (printf selector → data/char/skin/g{N}.skn)
        //   +0x084 = anim_ref_key (pool id → BindPosePoolId)
        // Build a minimal 548-byte fixed block with effect_count=0.
        // spec: Docs/RE/formats/items_scr.md §1.2 — fixed 548-byte (0x224) block: CONFIRMED
        byte[] record = new byte[548]; // 0x224

        // item_name CP949[52] @ 0x000 — "Sword"
        byte[] name = Encoding.ASCII.GetBytes("Sword");
        name.CopyTo(record, 0x000);

        // item_uid u32LE @ 0x034
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x034), 9999u);

        // model_ref_key u32LE @ 0x080 = 7
        // spec: Docs/RE/formats/items_scr.md §1.4.2 — +0x080 = model_ref_key (printf selector): CONFIRMED
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x080), 7u);

        // anim_ref_key u32LE @ 0x084 = 42
        // spec: Docs/RE/formats/items_scr.md §1.4.2 — +0x084 = anim_ref_key (pool id): CONFIRMED
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x084), 42u);

        // effect_count u8 @ 0x220 = 0 (no trailing entries)
        record[0x220] = 0;

        var records = ItemsScrParser.Parse(record).ToList();
        Assert.Single(records);

        var r = records[0];
        Assert.Equal(7u, r.ModelRefKey);
        // spec: Docs/RE/formats/items_scr.md §1.4.2 — SknVfsPath = "data/char/skin/g{ModelRefKey}.skn"
        Assert.Equal("data/char/skin/g7.skn", r.SknVfsPath);

        Assert.Equal(42u, r.AnimRefKey);
        // spec: Docs/RE/formats/items_scr.md §1.4.2 — BindPosePoolId = AnimRefKey
        Assert.Equal(42u, r.BindPosePoolId);
    }

    [Fact]
    public void ItemsScrParser_EffectCount_Zero_NoTrailingEntries()
    {
        // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED
        // effect_count=0 → stride = 548, no trailing entries
        byte[] record = new byte[548];
        record[0x220] = 0;

        var records = ItemsScrParser.Parse(record).ToList();
        Assert.Single(records);
        Assert.Equal(0, records[0].EffectCount);
        Assert.Empty(records[0].Effects);
    }

    [Fact]
    public void ItemsScrParser_TwoRecords_StrideConsistency()
    {
        // Two back-to-back fixed blocks with effect_count=0 → parser yields 2 records
        byte[] data = new byte[548 * 2];
        // First record: name "A"
        data[0] = (byte)'A';
        data[0x220] = 0; // effect_count=0

        // Second record at offset 548: name "B"
        data[548] = (byte)'B';
        data[548 + 0x220] = 0;

        var records = ItemsScrParser.Parse(data).ToList();
        Assert.Equal(2, records.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BgtextureLstParser
    // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations, §Header, §Record / body layout
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BgtextureLstParser_KindByte1_IsStaticRenderBucket()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations —
        //   "kind byte gates a single binary branch: ==0x01 STATIC render-object": CODE-CONFIRMED CYCLE 1
        var buf = BuildBgtextureLst(new (byte kind, string relPath)[]
        {
            (0x01, "terrain/stone"),
        });

        var catalog = BgtextureLstParser.Parse(buf);
        Assert.Equal(1, catalog.Count);
        Assert.Equal(BgTextureRenderBucket.Static, catalog.Records[0].RenderBucket);
        Assert.Equal(BgTextureKind.Static, catalog.Records[0].KindEnum);
        Assert.Equal("terrain/stone", catalog.Records[0].RelPath);
    }

    [Fact]
    public void BgtextureLstParser_KindByte2_IsScrollAnimated()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations —
        //   "kind!=0x01 wires the NON-STATIC (scroll/animated) render-object type": CODE-CONFIRMED CYCLE 1
        var buf = BuildBgtextureLst(new (byte, string)[]
        {
            (0x02, "water/lake"),
        });

        var catalog = BgtextureLstParser.Parse(buf);
        Assert.Equal(BgTextureRenderBucket.ScrollAnimated, catalog.Records[0].RenderBucket);
        Assert.Equal(BgTextureKind.ScrollUv, catalog.Records[0].KindEnum);
    }

    [Fact]
    public void BgtextureLstParser_TwoRecords_PoolIndexIsOnDiskPosition()
    {
        // Pool index is the 0-based record position.
        // spec: Docs/RE/formats/bgtexture_lst.md — "records are addressed by position": CONFIRMED
        var buf = BuildBgtextureLst(new (byte, string)[]
        {
            (0x01, "terrain/a"),
            (0x02, "water/b"),
        });

        var catalog = BgtextureLstParser.Parse(buf);
        Assert.Equal(2, catalog.Count);
        Assert.Equal(0, catalog.Records[0].Index);
        Assert.Equal(1, catalog.Records[1].Index);
    }

    [Fact]
    public void BgtextureLstParser_SizeFormula_4PlusCountTimes48()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula: CONFIRMED
        // 3 records → total = 4 + 3*48 = 148 bytes
        var buf = BuildBgtextureLst(new (byte, string)[]
        {
            (0x01, "a"),
            (0x01, "b"),
            (0x01, "c"),
        });

        Assert.Equal(4 + 3 * 48, buf.Length);
    }

    [Fact]
    public void BgtextureLstParser_CountZero_Throws()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout —
        //   "rejects a count of 0": CODE-CONFIRMED
        byte[] buf = new byte[4]; // count=0
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 0);
        Assert.Throws<InvalidDataException>(() => BgtextureLstParser.Parse(buf));
    }

    [Fact]
    public void BgtextureLstParser_Count2000_Throws()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout —
        //   "rejects a count >= 2000 (0x7D0)": CODE-CONFIRMED
        byte[] buf = new byte[4 + 2000 * 48];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 2000u);
        Assert.Throws<InvalidDataException>(() => BgtextureLstParser.Parse(buf));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ActormotionParser
    // spec: Docs/RE/formats/actormotion.md §Per-record layout
    //   cols 15..23 = motion_ids_a (MotionClipIds); cols 24..32 = motion_ids_b (SfxEventIds)
    //   col2 = int_a / SkinClassId
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActormotionParser_MotionClipIds_AreColumns15to23()
    {
        // spec: Docs/RE/formats/actormotion.md §Per-record layout —
        //   motion_ids_a[0..8] → cols 15..23 @ 0x40: CONFIRMED
        string tabRow = BuildActormotionRow(
            col2_skinClass: 3,
            motionIdsA: [101, 102, 103, 104, 105, 106, 107, 108, 109],
            motionIdsB: [201, 202, 203, 204, 205, 206, 207, 208, 209]);

        string text = $"1\n{tabRow}\n";
        var catalogue = ActormotionParser.ParseText(text);

        Assert.Equal(1, catalogue.Count);
        var entry = catalogue.AllEntries.First();

        // MotionClipIds (DirArray1 / motion_ids_a) must be cols 15..23
        Assert.Equal([101, 102, 103, 104, 105, 106, 107, 108, 109], entry.MotionClipIds);

        // SfxEventIds (DirArray2 / motion_ids_b) must be cols 24..32
        Assert.Equal([201, 202, 203, 204, 205, 206, 207, 208, 209], entry.SfxEventIds);
    }

    [Fact]
    public void ActormotionParser_SkinClassId_IsColumn2()
    {
        // spec: Docs/RE/formats/actormotion.md §Per-record layout —
        //   int_a @ 0x04, col2 = skin_class: SAMPLE-VERIFIED
        string tabRow = BuildActormotionRow(col2_skinClass: 7);
        string text = $"1\n{tabRow}\n";
        var catalogue = ActormotionParser.ParseText(text);
        var entry = catalogue.AllEntries.First();

        Assert.Equal(7, entry.SkinClassId); // SkinClassId == IntA == col2
    }

    [Fact]
    public void ActormotionParser_SfxEventIds_IsDistinctFromMotionClipIds()
    {
        // spec: Docs/RE/formats/actormotion.md — motion_ids_b = SFX/FX event ids, NOT secondary motion
        string tabRow = BuildActormotionRow(
            motionIdsA: [10, 11, 12, 13, 14, 15, 16, 17, 18],
            motionIdsB: [20, 21, 22, 23, 24, 25, 26, 27, 28]);
        string text = $"1\n{tabRow}\n";
        var catalogue = ActormotionParser.ParseText(text);
        var entry = catalogue.AllEntries.First();

        // SfxEventIds and MotionClipIds must differ at every slot
        for (int i = 0; i < 9; i++)
            Assert.NotEqual(entry.MotionClipIds[i], entry.SfxEventIds[i]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // XdbParser — creature_item.xdb
    // spec: Docs/RE/formats/xdb_tables.md §5 — stride 48 bytes; item_id u32LE @+4
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void XdbParser_CreatureItem_Stride48_ItemIdAtOffset4()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — stride 48 bytes, 921 records: CONFIRMED
        // spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED
        byte[] record = new byte[48];

        // creature_key u32LE @ +0
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0), 12345u);

        // item_id u32LE @ +4 = 3001 (a known held-item visual id in the spec context)
        // spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), 3001u);

        // tick_interval u32LE @ +44 = 100 (constant in all 921 records per spec)
        // spec: Docs/RE/formats/xdb_tables.md §5 — tick_interval u32LE @ +44: CONFIRMED (constant 100)
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), 100u);

        var results = XdbParser.ParseCreatureItemXdb(record);
        Assert.Single(results);

        var r = results[0];
        Assert.Equal(12345u, r.CreatureKey);
        Assert.Equal(3001u, r.ItemId); // spec: §5 — item_id = held-item visual id: CONFIRMED
        Assert.Equal(100u, r.Probability); // spec: §5 — tick_interval constant 100: CONFIRMED
    }

    [Fact]
    public void XdbParser_CreatureItem_StrideMustBe48_NonMultiple_Throws()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — stride 48 bytes, exact: CONFIRMED
        byte[] bad = new byte[47]; // not a multiple of 48
        Assert.Throws<InvalidDataException>(() => XdbParser.ParseCreatureItemXdb(bad));
    }

    [Fact]
    public void XdbParser_CreatureItem_MultipleRecords_AllParsed()
    {
        // Verify 3 records × 48 bytes = 144 bytes parses to 3 entries
        byte[] data = new byte[3 * 48];
        for (int i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 48 + 4), (uint)(3001 + i));
        }

        var results = XdbParser.ParseCreatureItemXdb(data);
        Assert.Equal(3, results.Length);
        Assert.Equal(3001u, results[0].ItemId);
        Assert.Equal(3002u, results[1].ItemId);
        Assert.Equal(3003u, results[2].ItemId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a valid bgtexture.lst byte buffer from a list of (kind, relPath) tuples.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Header — u32LE count; §Record — 48 bytes each.
    /// </summary>
    private static byte[] BuildBgtextureLst((byte kind, string relPath)[] records)
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — count u32LE @ 0: CONFIRMED
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED
        int count = records.Length;
        byte[] buf = new byte[4 + count * 48];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)count);

        for (int i = 0; i < count; i++)
        {
            int offset = 4 + i * 48;
            buf[offset] = records[i].kind; // kind u8 @ +0
            byte[] pathBytes = Encoding.ASCII.GetBytes(records[i].relPath);
            int copyLen = Math.Min(pathBytes.Length, 47); // relpath char[47] @ +1
            pathBytes.AsSpan(0, copyLen).CopyTo(buf.AsSpan(offset + 1));
        }

        return buf;
    }

    /// <summary>
    /// Builds a 33-column tab-separated actormotion.txt row (no trailing newline).
    /// spec: Docs/RE/formats/actormotion.md §Per-record layout — 33 tab-delimited columns.
    /// Columns 0/1 = 0 (category/intra-offset), col2 = skinClass, cols 3..14 = floats/ints = 0,
    /// cols 15..23 = motionIdsA, cols 24..32 = motionIdsB.
    /// </summary>
    private static string BuildActormotionRow(
        int col2_skinClass = 0,
        int[]? motionIdsA = null,
        int[]? motionIdsB = null)
    {
        // spec: Docs/RE/formats/actormotion.md §Per-record layout — 33 columns per record
        int[] a = motionIdsA ?? [0, 0, 0, 0, 0, 0, 0, 0, 0];
        int[] b = motionIdsB ?? [0, 0, 0, 0, 0, 0, 0, 0, 0];

        var cols = new string[33];
        cols[0] = "0";  // category
        cols[1] = "0";  // intra-offset (motion_key = 0)
        cols[2] = col2_skinClass.ToString(); // col2 = skin_class
        cols[3] = "0";  // rate_src_x
        cols[4] = "1";  // divisor_x (non-zero to avoid forced-to-1 guard making tests trivial)
        cols[5] = "0";  // rate_src_y
        cols[6] = "1";  // divisor_y
        cols[7] = "0";  // int_b
        cols[8] = "0";  // float_c
        cols[9] = "0";  // float_d
        cols[10] = "0"; // float_e
        cols[11] = "0"; // float_f
        cols[12] = "0"; // float_g
        cols[13] = "0"; // float_h
        cols[14] = "0"; // float_i
        // cols 15..23 = motion_ids_a
        // spec: Docs/RE/formats/actormotion.md §Per-record layout — motion_ids_a[0..8] → cols 15..23
        for (int i = 0; i < 9; i++) cols[15 + i] = a[i].ToString();
        // cols 24..32 = motion_ids_b
        // spec: Docs/RE/formats/actormotion.md §Per-record layout — motion_ids_b[0..8] → cols 24..32
        for (int i = 0; i < 9; i++) cols[24 + i] = b[i].ToString();

        return string.Join("\t", cols);
    }
}
