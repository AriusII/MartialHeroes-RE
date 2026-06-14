using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for the Wave-3c typed data-table parsers:
/// <list type="bullet">
/// <item><see cref="CitemsParser"/> — <c>data/script/citems.scr</c> (1052-byte stride × 512 records)</item>
/// <item><see cref="ItemsScrParser"/> — <c>data/script/items.scr</c> (variable-length; Model A)</item>
/// <item><see cref="EventsScrParser"/> — <c>data/script/events.scr</c> (520-byte stride)</item>
/// <item><see cref="AutoQuestionParser"/> — <c>data/script/autoquestion_cl.scr</c> (92-byte stride)</item>
/// <item><see cref="ChatFilterParser"/> — <c>data/cursor/curse.txt</c> + <c>data/cursor/cursechat.txt</c></item>
/// </list>
/// All fixtures are hand-built in-memory; no real VFS files are required.
/// spec: Docs/RE/formats/items_scr.md, Docs/RE/formats/events_scr.md, Docs/RE/formats/text_tables.md
/// </summary>
public sealed class Wave3cParserTests
{
    // ─── Binary helpers ────────────────────────────────────────────────────────

    private static void WriteU16LE(byte[] buf, int off, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(off, 2), v);

    private static void WriteU32LE(byte[] buf, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), v);

    /// <summary>
    /// Writes a CP949-encoded, null-terminated string into a fixed-width field inside <paramref name="buf"/>.
    /// </summary>
    private static void WriteCp949(byte[] buf, int off, int fieldLen, string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        byte[] encoded = cp949.GetBytes(text);
        int copyLen = Math.Min(encoded.Length, fieldLen - 1); // leave room for NUL terminator
        Array.Copy(encoded, 0, buf, off, copyLen);
        buf[off + copyLen] = 0; // NUL terminator
    }

    // =========================================================================
    // 1. CitemsParser — citems.scr (stride 1052 × 512)
    // =========================================================================
    // spec: Docs/RE/formats/items_scr.md §2 citems.scr — CONFIRMED.

    /// <summary>
    /// Builds a minimal citems.scr fixture with the given number of records.
    /// CORRECTIONS applied (spec: Docs/RE/formats/items_scr.md §2.5):
    /// - item_name CP949[48] @ 0x04 (NOT @ 0x08 with a separate item_ref @ 0x04).
    /// - No item_ref field exists — those bytes ARE the item_name.
    /// - Description is 6 × 81-byte paragraphs from 0x0E4 (NOT a single buffer near 0xDC).
    /// spec: Docs/RE/formats/items_scr.md §2.2 Record layout.
    /// </summary>
    private static byte[] BuildCitemsScr(int recordCount,
        uint slotIndexBase = 1,
        string itemName = "TestItem",
        uint cashPrice = 99,
        uint itemUid = 283000000)
    {
        // stride 1052 bytes. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.1 — "Fixed stride: 1052 bytes (0x41C)": CONFIRMED.
        const int stride = 1052;
        byte[] buf = new byte[recordCount * stride];

        for (int i = 0; i < recordCount; i++)
        {
            int off = i * stride;

            // slot_index u32LE @ 0x00. Sequential 1-based. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — slot_index u32LE @ 0x00: CONFIRMED.
            WriteU32LE(buf, off + 0x00, slotIndexBase + (uint)i);

            // item_name CP949[48] @ 0x04. CONFIRMED (512/512).
            // NOTE: the 4 bytes at +0x04 ARE the start of the name string — no separate item_ref field exists.
            // spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
            // spec: Docs/RE/formats/items_scr.md §2.5 — "item_ref at +0x04 DOES NOT EXIST".
            WriteCp949(buf, off + 0x04, 48, itemName);

            // zero padding 0x30..0x35 — left as zero (pre-zeroed). CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — pad_30 u8[6] @ 0x30: CONFIRMED (zero).

            // unknown_36 u16LE @ 0x36. CONFIRMED present; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — unknown_36 u16LE @ 0x36: CONFIRMED present; role UNVERIFIED.
            WriteU16LE(buf, off + 0x36, 7);

            // cash_price_nx u32LE @ 0x38. CONFIRMED (value). role INFERRED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
            WriteU32LE(buf, off + 0x38, cashPrice + (uint)i);

            // slot_seq_2 u32LE @ 0x3C. CONFIRMED (sequential).
            // spec: Docs/RE/formats/items_scr.md §2.2 — slot_seq_2 u32LE @ 0x3C: CONFIRMED.
            WriteU32LE(buf, off + 0x3C, slotIndexBase + (uint)i);

            // pad_40 u8[8] @ 0x40 — left zero. CONFIRMED (zero).
            // spec: Docs/RE/formats/items_scr.md §2.2 — pad_40 u8[8] @ 0x40: CONFIRMED.

            // item_uid u32LE @ 0x48. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
            WriteU32LE(buf, off + 0x48, itemUid + (uint)i);

            // flag_4C u32LE @ 0x4C. Value 1. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
            WriteU32LE(buf, off + 0x4C, 1u);

            // Description paragraphs and remainder region @ 0x50+ — left zero.
            // spec: Docs/RE/formats/items_scr.md §2.4 — 6 × 81-byte desc paragraphs from 0x0E4: CONFIRMED.
        }

        return buf;
    }

    [Fact]
    public void Citems_Stride1052_RecordCountFromDivision()
    {
        // 512 × 1052 = 538,624 bytes. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.3 — "file_size / 1052 = 512": CONFIRMED.
        Assert.Equal(0, 538624 % 1052);
        Assert.Equal(512, 538624 / 1052);
    }

    [Fact]
    public void Citems_Parse_SingleRecord_SlotIndexAndItemName()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — slot_index @ 0x00, item_name @ 0x04: CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.5 — item_name at 0x04 (48 bytes), NOT 0x08 (40 bytes): CONFIRMED.
        byte[] buf = BuildCitemsScr(1, slotIndexBase: 1, itemName: "아이템A");
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(catalog.Records);
        Assert.Equal(1u, catalog.Records[0].SlotIndex);
        Assert.Equal("아이템A", catalog.Records[0].ItemName);
    }

    [Fact]
    public void Citems_Parse_CashPrice_Decoded()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value).
        byte[] buf = BuildCitemsScr(1, cashPrice: 990);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(990u, catalog.Records[0].CashPriceNx);
    }

    [Fact]
    public void Citems_Parse_ItemUid_Decoded()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED (records 0–1).
        byte[] buf = BuildCitemsScr(2, itemUid: 283000001);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(283000001u, catalog.Records[0].ItemUid);
        Assert.Equal(283000002u, catalog.Records[1].ItemUid);
    }

    [Fact]
    public void Citems_Parse_Flag4C_IsOne()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED (value = 1).
        byte[] buf = BuildCitemsScr(1);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(1u, catalog.Records[0].Flag4C);
    }

    [Fact]
    public void Citems_Parse_RemainderRaw_IsCorrectLength()
    {
        // remainder region starts at 0x2CA; length = 1052 - 0x2CA = 338 bytes.
        // spec: Docs/RE/formats/items_scr.md §2.2 — record remainder 0x2CA..0x41B: UNVERIFIED.
        // Note: the old TrailingRaw member (at 0x50, 1004 bytes) has been superseded and renamed to
        // RemainderRaw covering 0x2CA..0x41B (338 bytes). Do NOT reference TrailingRaw.
        // spec: Docs/RE/formats/items_scr.md §2.5 — corrections.
        byte[] buf = BuildCitemsScr(1);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(1052 - 0x2CA, catalog.Records[0].RemainderRaw.Length); // 338 bytes
    }

    [Fact]
    public void Citems_Parse_TwoRecords_BothDecoded()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — sequential slot_index across records: CONFIRMED.
        byte[] buf = BuildCitemsScr(2, slotIndexBase: 10);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, catalog.Records.Count);
        Assert.Equal(10u, catalog.Records[0].SlotIndex);
        Assert.Equal(11u, catalog.Records[1].SlotIndex);
    }

    [Fact]
    public void Citems_Lookup_TryGetByUid_Found()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid @ 0x48: CONFIRMED.
        byte[] buf = BuildCitemsScr(3, itemUid: 5000);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        CitemsRecord? rec = catalog.TryGetByUid(5001u);
        Assert.NotNull(rec);
        Assert.Equal(5001u, rec!.ItemUid);
    }

    [Fact]
    public void Citems_Lookup_TryGetByUid_NotFound_ReturnsNull()
    {
        byte[] buf = BuildCitemsScr(1, itemUid: 9000);
        CitemsCatalog catalog = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Null(catalog.TryGetByUid(0u));
    }

    [Fact]
    public void Citems_NonMultiple_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052 (exact)".
        byte[] bad = new byte[1053]; // not a multiple of 1052
        Assert.Throws<InvalidDataException>(() => CitemsParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Citems_EmptyBuffer_YieldsZeroRecords()
    {
        CitemsCatalog catalog = CitemsParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(catalog.Records);
    }

    // =========================================================================
    // 2. ItemsScrParser — items.scr (variable-length; Model A, confirmed 2026-06-14)
    // =========================================================================
    // spec: Docs/RE/formats/items_scr.md §1 items.scr — CONFIRMED "Model A".
    //
    // Public API is ONLY: static IEnumerable<ItemsScrRecord> Parse(ReadOnlyMemory<byte> data).
    // There is NO ReadFirstRecord and NO ItemsScrLeadingFields type (both were part of the
    // REFUTED model — do not reintroduce).
    // spec: Docs/RE/formats/items_scr.md §1.7 — Superseded / Refuted.

    /// <summary>
    /// Builds a minimal items.scr fixture with one record following Model A:
    ///   [ fixed 548-byte (0x224) block ][ effect_count × 8 bytes trailing effect entries ]
    /// spec: Docs/RE/formats/items_scr.md §1.2 — CONFIRMED model.
    /// spec: Docs/RE/formats/items_scr.md §1.4 — field offsets within the fixed block.
    /// </summary>
    private static byte[] BuildItemsScrRecord(
        string itemName,
        uint itemUid,
        string itemDesc = "",
        byte effectCount = 0)
    {
        // Fixed block: 548 bytes (0x224). CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.2 — "fixed 548-byte (0x224) block": CONFIRMED.
        const int fixedBlockSize = 0x224; // 548

        int totalSize = fixedBlockSize + effectCount * 8;
        byte[] buf = new byte[totalSize];

        // item_name CP949[52] @ 0x000. CONFIRMED (90,937/90,937).
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED.
        WriteCp949(buf, 0x000, 52, itemName);

        // item_uid u32LE @ 0x034. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32LE @0x034: CONFIRMED.
        WriteU32LE(buf, 0x034, itemUid);

        // item_desc CP949 NUL-term @ 0x038 — CONFIRMED present; extent UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc CP949 @0x038: CONFIRMED present.
        if (!string.IsNullOrEmpty(itemDesc))
            WriteCp949(buf, 0x038, fixedBlockSize - 0x038, itemDesc);

        // effect_count u8 @ 0x220. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED.
        buf[0x220] = effectCount;

        return buf;
    }

    [Fact]
    public void ItemsScr_Parse_FirstRecord_Name_Decoded()
    {
        // Parse the first record's ItemName via the confirmed Model-A API.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED (90,937/90,937).
        byte[] buf = BuildItemsScrRecord("전사의 검", 201000001u);
        ItemsScrRecord rec = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).First();

        Assert.Equal("전사의 검", rec.ItemName);
    }

    [Fact]
    public void ItemsScr_Parse_FirstRecord_Uid_Decoded()
    {
        // Parse the first record's ItemUid via the confirmed Model-A API.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32LE @0x034: CONFIRMED (90,937/90,937).
        byte[] buf = BuildItemsScrRecord("아이템", 201999999u);
        ItemsScrRecord rec = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).First();

        Assert.Equal(201999999u, rec.ItemUid);
    }

    [Fact]
    public void ItemsScr_Parse_FirstRecord_Desc_Decoded()
    {
        // Parse the first record's ItemDesc via the confirmed Model-A API.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc CP949 @0x038: CONFIRMED present; extent UNVERIFIED.
        byte[] buf = BuildItemsScrRecord("Test", 12345u, itemDesc: "설명 텍스트");
        ItemsScrRecord rec = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).First();

        Assert.Equal("설명 텍스트", rec.ItemDesc);
    }

    [Fact]
    public void ItemsScr_Parse_BufferTooShortForFixedBlock_YieldsNoRecords()
    {
        // A buffer shorter than 548 bytes (0x224) yields no records (short-read EOF handling).
        // spec: Docs/RE/formats/items_scr.md §1.3 — "Terminate at EOF / short read": CONFIRMED.
        byte[] tooShort = new byte[0x30]; // only 48 bytes — shorter than the 548-byte fixed block
        var records = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(tooShort)).ToList();

        Assert.Empty(records);
    }

    [Fact]
    public void ItemsScr_Parse_FirstRecord_FixedBlockRaw_Is548Bytes()
    {
        // The FixedBlockRaw on a parsed record must cover exactly the 548-byte fixed block.
        // spec: Docs/RE/formats/items_scr.md §1.2 — "fixed 548-byte (0x224) block": CONFIRMED.
        byte[] buf = BuildItemsScrRecord("ItemX", 77777u);
        ItemsScrRecord rec = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).First();

        Assert.Equal(0x224, rec.FixedBlockRaw.Length); // 548 bytes
    }

    [Fact]
    public void ItemsScr_Parse_FirstRecord_EffectCount_ZeroWhenNoTrailingEntries()
    {
        // A record with no trailing effect entries must have EffectCount == 0.
        // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED.
        byte[] buf = BuildItemsScrRecord("NoEffects", 99999u, effectCount: 0);
        ItemsScrRecord rec = ItemsScrParser.Parse(new ReadOnlyMemory<byte>(buf)).First();

        Assert.Equal(0, rec.EffectCount);
        Assert.Empty(rec.Effects);
    }

    // =========================================================================
    // 3. EventsScrParser — events.scr (stride 520 × 1848)
    // =========================================================================
    // spec: Docs/RE/formats/events_scr.md §1 events.scr — sample_verified.

    /// <summary>
    /// Builds a minimal events.scr fixture: one 520-byte record.
    /// UNVERIFIED flag and trailer bytes are left as zero.
    /// spec: Docs/RE/formats/events_scr.md §1.3 Record layout.
    /// </summary>
    private static byte[] BuildEventsScr(
        uint eventId, ushort eventType = 1, ushort dayCount = 7,
        uint levelMin = 100, uint levelMax = 1000,
        uint[]? idsA = null, uint[]? idsB = null,
        int recordCount = 1)
    {
        // stride 520 bytes. CONFIRMED.
        // spec: Docs/RE/formats/events_scr.md §1.1 — "stride 520 bytes (0x208)": sample_verified.
        const int stride = 520;
        byte[] buf = new byte[recordCount * stride];

        // Fill record 0 with supplied values; leave remaining records as all-zero.
        // event_id u32LE @ 0x00. HIGH.
        // spec: Docs/RE/formats/events_scr.md §1.3 — event_id u32LE @ 0x00: HIGH.
        WriteU32LE(buf, 0x00, eventId);

        // event_type u16LE @ 0x04. PARTIAL.
        // spec: Docs/RE/formats/events_scr.md §1.3 — event_type u16LE @ 0x04: PARTIAL.
        WriteU16LE(buf, 0x04, eventType);

        // day_count u16LE @ 0x06. PARTIAL.
        // spec: Docs/RE/formats/events_scr.md §1.3 — day_count u16LE @ 0x06: PARTIAL.
        WriteU16LE(buf, 0x06, dayCount);

        // reserved_a u8[68] @ 0x08 — left zero.
        // spec: Docs/RE/formats/events_scr.md §1.3 — reserved_a @ 0x08: UNVERIFIED.

        // level_min u32LE @ 0x4C. HIGH.
        // spec: Docs/RE/formats/events_scr.md §1.3 — level_min u32LE @ 0x4C: HIGH.
        WriteU32LE(buf, 0x4C, levelMin);

        // level_max u32LE @ 0x50. HIGH.
        // spec: Docs/RE/formats/events_scr.md §1.3 — level_max u32LE @ 0x50: HIGH.
        WriteU32LE(buf, 0x50, levelMax);

        // rate_array u32LE[50] @ 0x68. CONFIRMED CONSUMED (formerly ids_array_a -- CORRECTED).
        // Values are rates; divide by 1,000,000 to get rate fraction.
        // spec: Docs/RE/formats/events_scr.md §1.3 -- rate_array u32LE[50] @ 0x68: CONFIRMED CONSUMED.
        if (idsA != null)
        {
            for (int k = 0; k < Math.Min(idsA.Length, 50); k++)
                WriteU32LE(buf, 0x68 + k * 4, idsA[k]);
        }

        // actor_array u32LE[52] @ 0x130. CONFIRMED CONSUMED (formerly ids_array_b -- CORRECTED).
        // Values are 9-digit actor IDs.
        // spec: Docs/RE/formats/events_scr.md §1.3 -- actor_array u32LE[52] @ 0x130: CONFIRMED CONSUMED.
        if (idsB != null)
        {
            for (int k = 0; k < Math.Min(idsB.Length, 52); k++)
                WriteU32LE(buf, 0x130 + k * 4, idsB[k]);
        }

        return buf;
    }

    [Fact]
    public void EventsScr_Stride520_RecordCountFromDivision()
    {
        // spec: Docs/RE/formats/events_scr.md §1.1 — "file_size / 520 = 1848": sample_verified.
        Assert.Equal(0, 960960 % 520);
        Assert.Equal(1848, 960960 / 520);
    }

    [Fact]
    public void EventsScr_Parse_SingleRecord_EventId()
    {
        // spec: Docs/RE/formats/events_scr.md §1.3 -- event_id @ 0x00: CONFIRMED CONSUMED.
        // NOTE: level_min (@0x4C) and level_max (@0x50) are CONFIRMED NOT-CONSUMED -- removed from model.
        // spec: Docs/RE/formats/events_scr.md §1.6 -- "client reads ONLY event_id, mode_flag, rate_array, actor_array."
        byte[] buf = BuildEventsScr(10551u, levelMin: 100, levelMax: 1000);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(records);
        Assert.Equal(10551u, records[0].EventId);
        // level_min/level_max are accessible via Raw if needed; they are NOT-CONSUMED model fields.
    }

    [Fact]
    public void EventsScr_Parse_EventType_DayCount()
    {
        // spec: Docs/RE/formats/events_scr.md §1.3 — event_type @ 0x04, day_count @ 0x06: PARTIAL.
        byte[] buf = BuildEventsScr(1u, eventType: 1, dayCount: 7);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal((ushort)1, records[0].EventType);
        Assert.Equal((ushort)7, records[0].DayCount);
    }

    [Fact]
    public void EventsScr_Parse_ActorArray_Decoded()
    {
        // spec: Docs/RE/formats/events_scr.md §1.3 -- actor_array u32LE[52] @ 0x130: CONFIRMED CONSUMED.
        // formerly named ids_array_b -- CORRECTED per spec VFS-DEEP-II update.
        // Values are 9-digit actor IDs.
        var idsB = new uint[] { 213010002u, 215010101u, 0u }; // 0 terminates
        byte[] buf = BuildEventsScr(1u, idsB: idsB);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        // Only non-zero entries are decoded (zero-terminated).
        Assert.Equal(2, records[0].ActorArray.Count);
        Assert.Equal(213010002u, records[0].ActorArray[0]);
        Assert.Equal(215010101u, records[0].ActorArray[1]);
    }

    [Fact]
    public void EventsScr_Parse_RateArray_Decoded()
    {
        // spec: Docs/RE/formats/events_scr.md §1.3 -- rate_array u32LE[50] @ 0x68: CONFIRMED CONSUMED.
        // formerly named ids_array_a -- CORRECTED per spec VFS-DEEP-II update.
        // Values are rates; divide by 1,000,000 to get rate fraction.
        var idsA = new uint[] { 800001u, 850002u };
        byte[] buf = BuildEventsScr(1u, idsA: idsA);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records[0].RateArray.Count);
        Assert.Equal(800001u, records[0].RateArray[0]);
    }

    [Fact]
    public void EventsScr_Parse_Raw_Is520Bytes()
    {
        // spec: Docs/RE/formats/events_scr.md §1.3 — stride 520 bytes: CONFIRMED.
        byte[] buf = BuildEventsScr(1u);
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(520, records[0].Raw.Length);
    }

    [Fact]
    public void EventsScr_Parse_AllZeroRecord_IsIncluded()
    {
        // Sparse table: all-zero records (empty slots) are included in the output.
        // spec: Docs/RE/formats/events_scr.md §1.2 — "all-zero reward arrays = empty slot".
        byte[] buf = new byte[520 * 2]; // two all-zero records
        EventsScrRecord[] records = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records.Length);
        Assert.Equal(0u, records[0].EventId); // empty slot
    }

    [Fact]
    public void EventsScr_NonMultiple_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/events_scr.md §1.1 — "record count = file_size / 520 (exact)".
        byte[] bad = new byte[521]; // not a multiple of 520
        Assert.Throws<InvalidDataException>(() => EventsScrParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void EventsScr_EmptyBuffer_YieldsZeroRecords()
    {
        EventsScrRecord[] records = EventsScrParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(records);
    }

    // =========================================================================
    // 4. AutoQuestionParser — autoquestion_cl.scr (stride 92 × 1300)
    // =========================================================================
    // spec: Docs/RE/formats/events_scr.md §2 autoquestion_cl.scr — sample_verified.

    /// <summary>
    /// Builds a minimal autoquestion_cl.scr fixture: one 92-byte record.
    /// spec: Docs/RE/formats/events_scr.md §2.2 Record layout.
    /// </summary>
    private static byte[] BuildAutoQuestion(uint questionId, string question, string prompt)
    {
        // stride 92 bytes (0x5C). CONFIRMED.
        // spec: Docs/RE/formats/events_scr.md §2.1 — "Record stride: 92 bytes (0x5C)": CONFIRMED.
        const int stride = 92;
        byte[] buf = new byte[stride];

        // question_id u32LE @ 0x00. HIGH.
        // spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
        WriteU32LE(buf, 0x00, questionId);

        // text_block CP949 @ 0x04 (84 bytes): two consecutive null-terminated CP949 strings.
        // spec: Docs/RE/formats/events_scr.md §2.2 — text_block char[] CP949 @ 0x04 (84 bytes): HIGH.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        byte[] qBytes = cp949.GetBytes(question);
        byte[] pBytes = cp949.GetBytes(prompt);

        int blockStart = 0x04;
        int blockEnd = blockStart + 84; // exclusive

        // Write question text.
        int pos = blockStart;
        int qCopy = Math.Min(qBytes.Length, blockEnd - pos - 1);
        Array.Copy(qBytes, 0, buf, pos, qCopy);
        pos += qCopy;
        buf[pos++] = 0; // null terminator after question

        // Write prompt text (if room remains).
        int pCopy = Math.Min(pBytes.Length, blockEnd - pos - 1);
        if (pCopy > 0)
        {
            Array.Copy(pBytes, 0, buf, pos, pCopy);
            pos += pCopy;
        }
        // Remaining bytes in block are zero (already zero-initialised).

        // record_padding u8[4] @ 0x58 — left zero (pre-zeroed). HIGH.
        // spec: Docs/RE/formats/events_scr.md §2.2 — record_padding u8[4] @ 0x58: HIGH.

        return buf;
    }

    [Fact]
    public void AutoQuestion_Parse_QuestionId()
    {
        // spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
        byte[] buf = BuildAutoQuestion(42u, "일 더하기 일은?", "숫자로 입력하세요");
        AutoQuestionRecord[] records = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(records);
        Assert.Equal(42u, records[0].QuestionId);
    }

    [Fact]
    public void AutoQuestion_Parse_QuestionText_Decoded()
    {
        // spec: Docs/RE/formats/events_scr.md §2.2 — text_block first null-terminated CP949 string: HIGH.
        byte[] buf = BuildAutoQuestion(1u, "백 곱하기 백은?", "숫자로 입력");
        AutoQuestionRecord[] records = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal("백 곱하기 백은?", records[0].QuestionText);
    }

    [Fact]
    public void AutoQuestion_Parse_AnswerPrompt_Decoded()
    {
        // spec: Docs/RE/formats/events_scr.md §2.2 — text_block second null-terminated CP949 string: HIGH.
        byte[] buf = BuildAutoQuestion(1u, "Q?", "숫자로 입력하세요");
        AutoQuestionRecord[] records = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal("숫자로 입력하세요", records[0].AnswerPrompt);
    }

    [Fact]
    public void AutoQuestion_Parse_TwoRecords_BothDecoded()
    {
        // Verify two back-to-back records (stride alignment).
        byte[] rec0 = BuildAutoQuestion(1u, "Q1", "Prompt1");
        byte[] rec1 = BuildAutoQuestion(2u, "Q2", "Prompt2");
        byte[] buf = new byte[rec0.Length + rec1.Length];
        rec0.CopyTo(buf, 0);
        rec1.CopyTo(buf, 92);

        AutoQuestionRecord[] records = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records.Length);
        Assert.Equal(1u, records[0].QuestionId);
        Assert.Equal(2u, records[1].QuestionId);
        Assert.Equal("Q2", records[1].QuestionText);
    }

    [Fact]
    public void AutoQuestion_Stride92_RecordCountFromDivision()
    {
        // spec: Docs/RE/formats/events_scr.md §2.1 — "1300 records × 92 bytes = 119,600 bytes": CONFIRMED.
        Assert.Equal(0, 119600 % 92);
        Assert.Equal(1300, 119600 / 92);
    }

    [Fact]
    public void AutoQuestion_NonMultiple_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/events_scr.md §2.1 — "record count = file_size / 92 (exact)".
        byte[] bad = new byte[93]; // not a multiple of 92
        Assert.Throws<InvalidDataException>(() => AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void AutoQuestion_EmptyBuffer_YieldsZeroRecords()
    {
        AutoQuestionRecord[] records = AutoQuestionParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(records);
    }

    // =========================================================================
    // 5. ChatFilterParser — curse.txt / cursechat.txt
    // =========================================================================
    // spec: Docs/RE/formats/text_tables.md §3.1 — TAB-separated, CP949, ';'-prefixed comment preamble.

    /// <summary>
    /// Builds a synthetic TAB-delimited chat-filter table as a UTF-16 string (pre-decoded).
    /// spec: Docs/RE/formats/text_tables.md §3.1 — "Delimiter: TAB; CRLF; col0 = bad word, col1 = replacement": HIGH.
    /// </summary>
    private static string BuildCurseText(params (string bad, string replace)[] entries)
    {
        // Prefix with a ';'-comment line, then data rows.
        var sb = new System.Text.StringBuilder();
        sb.Append("; curse-filter test fixture\r\n");
        foreach (var (bad, replace) in entries)
        {
            sb.Append(bad);
            sb.Append('\t'); // TAB delimiter — spec: Docs/RE/formats/text_tables.md §3.1.
            sb.Append(replace);
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    [Fact]
    public void ChatFilter_ParseText_SkipsCommentLines()
    {
        // spec: Docs/RE/formats/text_tables.md §3.1 — "';'-prefixed comment preamble": HIGH.
        string text = BuildCurseText(("나쁜말", "좋은말"));
        ChatFilterEntry[] entries = ChatFilterParser.ParseText(text);

        // Comment line must be skipped; only data row returned.
        Assert.Single(entries);
    }

    [Fact]
    public void ChatFilter_ParseText_BadWord_And_Replacement()
    {
        // spec: Docs/RE/formats/text_tables.md §3.1 — col0 "bad word", col1 "replacement": HIGH.
        string text = BuildCurseText(("욕설", "***"));
        ChatFilterEntry[] entries = ChatFilterParser.ParseText(text);

        Assert.Equal("욕설", entries[0].BadWord);
        Assert.Equal("***", entries[0].Replacement);
    }

    [Fact]
    public void ChatFilter_ParseText_MultipleEntries()
    {
        // spec: Docs/RE/formats/text_tables.md §3.1 — "no join; consumed at load time": HIGH.
        string text = BuildCurseText(
            ("word1", "rep1"),
            ("word2", "rep2"),
            ("word3", "rep3"));
        ChatFilterEntry[] entries = ChatFilterParser.ParseText(text);

        Assert.Equal(3, entries.Length);
        Assert.Equal("word2", entries[1].BadWord);
        Assert.Equal("rep2", entries[1].Replacement);
    }

    [Fact]
    public void ChatFilter_ParseText_BlankLines_Skipped()
    {
        // Blank lines between data rows must be ignored.
        string text = "; comment\r\nbad1\trep1\r\n\r\nbad2\trep2\r\n";
        ChatFilterEntry[] entries = ChatFilterParser.ParseText(text);

        Assert.Equal(2, entries.Length);
    }

    [Fact]
    public void ChatFilter_ParseText_RowWithoutTab_Skipped()
    {
        // A row that does not contain a TAB (and thus has < 2 columns) must be skipped gracefully.
        string text = "; comment\r\nnocolumn\r\nbad\trep\r\n";
        ChatFilterEntry[] entries = ChatFilterParser.ParseText(text);

        Assert.Single(entries);
        Assert.Equal("bad", entries[0].BadWord);
    }

    [Fact]
    public void ChatFilter_ParseBytes_Cp949_Roundtrip()
    {
        // Build a CP949-encoded byte buffer and verify the parser decodes it correctly.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "Encoding: CP949": HIGH.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        string text = "; test\r\n욕설\t***\r\n";
        byte[] encoded = cp949.GetBytes(text);

        ChatFilterEntry[] entries = ChatFilterParser.Parse(new ReadOnlyMemory<byte>(encoded));

        Assert.Single(entries);
        Assert.Equal("욕설", entries[0].BadWord);
        Assert.Equal("***", entries[0].Replacement);
    }

    [Fact]
    public void ChatFilter_ParseText_Empty_ReturnsEmpty()
    {
        ChatFilterEntry[] entries = ChatFilterParser.ParseText(string.Empty);
        Assert.Empty(entries);
    }
}