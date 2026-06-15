using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="CitemsParser"/>.
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/items_scr.md §2 citems.scr — CONFIRMED.
/// </summary>
public sealed class CitemsParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    // Record stride: 1052 bytes (0x41C). CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.1 — "Fixed stride: 1052 bytes (0x41C)": CONFIRMED.
    private const int RecordStride = 1052; // 0x41C

    private static Encoding Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    /// <summary>
    /// Builds a single 1052-byte citems.scr record.
    /// spec: Docs/RE/formats/items_scr.md §2.2 — Record layout: CONFIRMED.
    /// </summary>
    private static byte[] BuildRecord(
        uint slotIndex = 1u,
        string itemName = "Item",
        ushort unknown36 = 0,
        uint cashPriceNx = 100u,
        uint slotSeq2 = 1u,
        uint itemUid = 100u,
        uint flag4C = 1u,
        string[]? descParagraphs = null)
    {
        byte[] buf = new byte[RecordStride];
        var enc = Cp949();

        // slot_index u32LE @ 0x00. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — slot_index u32LE @ 0x00: CONFIRMED (512/512).
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00, 4), slotIndex);

        // item_name CP949[48] @ 0x04. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
        byte[] nameBytes = enc.GetBytes(itemName);
        int copyLen = Math.Min(nameBytes.Length, 47);
        nameBytes.AsSpan(0, copyLen).CopyTo(buf.AsSpan(0x04));
        buf[0x04 + copyLen] = 0x00;

        // unknown_36 u16LE @ 0x36. CONFIRMED present.
        // spec: Docs/RE/formats/items_scr.md §2.2 — unknown_36 u16LE @ 0x36: CONFIRMED present; role UNVERIFIED.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x36, 2), unknown36);

        // cash_price_nx u32LE @ 0x38. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x38, 4), cashPriceNx);

        // slot_seq_2 u32LE @ 0x3C. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — slot_seq_2 u32LE @ 0x3C: CONFIRMED (sequential); role UNVERIFIED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x3C, 4), slotSeq2);

        // item_uid u32LE @ 0x48. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x48, 4), itemUid);

        // flag_4C u32LE @ 0x4C. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x4C, 4), flag4C);

        // 6 × 81-byte description paragraphs from 0x0E4. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.4 — "6 × 81-byte paragraphs from 0x0E4": CONFIRMED (512/512).
        if (descParagraphs != null)
        {
            int[] paraOffsets = [0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279];
            for (int p = 0; p < Math.Min(descParagraphs.Length, 6); p++)
            {
                byte[] pb = enc.GetBytes(descParagraphs[p]);
                int plen = Math.Min(pb.Length, 80);
                pb.AsSpan(0, plen).CopyTo(buf.AsSpan(paraOffsets[p]));
                buf[paraOffsets[p] + plen] = 0x00;
            }
        }

        return buf;
    }

    private static byte[] TwoRecords(byte[] r1, byte[] r2)
    {
        byte[] buf = new byte[r1.Length + r2.Length];
        r1.CopyTo(buf, 0);
        r2.CopyTo(buf, r1.Length);
        return buf;
    }

    // =========================================================================
    // 1. Stride validation
    // =========================================================================

    [Fact]
    public void Parse_EmptyBuffer_YieldsEmptyCatalog()
    {
        // Zero-length input must decode to zero records (0 % 1052 == 0).
        // spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052 (exact)": CONFIRMED.
        CitemsCatalog cat = CitemsParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(cat.Records);
    }

    [Fact]
    public void Parse_NotMultipleOfStride_ThrowsInvalidDataException()
    {
        // 100 is not divisible by 1052.
        // spec: Docs/RE/formats/items_scr.md §2.3 — "must be exact multiple of 1052": CONFIRMED.
        byte[] buf = new byte[100];
        Assert.Throws<InvalidDataException>(() => CitemsParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_ExactlyOneRecord_Decodes()
    {
        // Exactly 1052 bytes — one record. CONFIRMED stride.
        // spec: Docs/RE/formats/items_scr.md §2.1 — "Fixed stride: 1052 bytes (0x41C)": CONFIRMED.
        byte[] buf = BuildRecord();
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Single(cat.Records);
    }

    // =========================================================================
    // 2. Field decoding
    // =========================================================================

    [Fact]
    public void Parse_SlotIndex_RoundTrips()
    {
        // slot_index u32LE @ 0x00. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — slot_index u32LE @ 0x00: CONFIRMED (512/512).
        byte[] buf = BuildRecord(slotIndex: 42u);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(42u, cat.Records[0].SlotIndex);
    }

    [Fact]
    public void Parse_ItemName_At0x04_DecodedCorrectly()
    {
        // item_name CP949[48] @ 0x04. CONFIRMED; bytes at 0x04 ARE the name string.
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
        // spec: Docs/RE/formats/items_scr.md §2.5 — "item_ref at +0x04 DOES NOT EXIST".
        byte[] buf = BuildRecord(itemName: "PremiumSword");
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("PremiumSword", cat.Records[0].ItemName);
    }

    [Fact]
    public void Parse_CashPriceNx_RoundTrips()
    {
        // cash_price_nx u32LE @ 0x38. CONFIRMED (value).
        // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
        byte[] buf = BuildRecord(cashPriceNx: 950u);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(950u, cat.Records[0].CashPriceNx);
    }

    [Fact]
    public void Parse_ItemUid_RoundTrips()
    {
        // item_uid u32LE @ 0x48. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
        byte[] buf = BuildRecord(itemUid: 283001234u);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(283001234u, cat.Records[0].ItemUid);
    }

    [Fact]
    public void Parse_Flag4C_RoundTrips()
    {
        // flag_4C u32LE @ 0x4C. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
        byte[] buf = BuildRecord(flag4C: 1u);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(1u, cat.Records[0].Flag4C);
    }

    // =========================================================================
    // 3. Description paragraphs
    // =========================================================================

    [Fact]
    public void Parse_DescParagraphs_ExactlySix_Decoded()
    {
        // 6 × 81-byte description paragraphs from 0x0E4. CONFIRMED (512/512).
        // spec: Docs/RE/formats/items_scr.md §2.4 — "6 × 81-byte paragraphs from 0x0E4": CONFIRMED (512/512).
        string[] paras = ["P0", "P1", "P2", "P3", "P4", "P5"];
        byte[] buf = BuildRecord(descParagraphs: paras);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        CitemsRecord rec = cat.Records[0];

        Assert.Equal(6, rec.DescParagraphs.Length);
        for (int i = 0; i < 6; i++)
            Assert.Equal(paras[i], rec.DescParagraphs[i]);
    }

    [Fact]
    public void Parse_EmptyDescParagraphs_YieldEmptyStrings()
    {
        // Record with no text set in paragraph slots must yield 6 empty strings.
        // spec: Docs/RE/formats/items_scr.md §2.4 — paragraphs are null-terminated within 81-byte buffer.
        byte[] buf = BuildRecord();
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(6, cat.Records[0].DescParagraphs.Length);
        foreach (string para in cat.Records[0].DescParagraphs)
            Assert.Equal(string.Empty, para);
    }

    // =========================================================================
    // 4. Multi-record walk
    // =========================================================================

    [Fact]
    public void Parse_TwoRecords_BothDecoded_InOrder()
    {
        // Two records: verify count and per-record field isolation (no cross-record bleed).
        // spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052".
        byte[] r0 = BuildRecord(slotIndex: 1u, itemName: "Alpha", itemUid: 100u);
        byte[] r1 = BuildRecord(slotIndex: 2u, itemName: "Beta", itemUid: 200u);
        byte[] buf = TwoRecords(r0, r1);

        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, cat.Records.Count);
        Assert.Equal(1u, cat.Records[0].SlotIndex);
        Assert.Equal("Alpha", cat.Records[0].ItemName);
        Assert.Equal(100u, cat.Records[0].ItemUid);

        Assert.Equal(2u, cat.Records[1].SlotIndex);
        Assert.Equal("Beta", cat.Records[1].ItemName);
        Assert.Equal(200u, cat.Records[1].ItemUid);
    }

    // =========================================================================
    // 5. TryGetByUid lookup
    // =========================================================================

    [Fact]
    public void TryGetByUid_KnownUid_ReturnsRecord()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
        byte[] buf = BuildRecord(itemUid: 999u, itemName: "UniqueItem");
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        CitemsRecord? found = cat.TryGetByUid(999u);
        Assert.NotNull(found);
        Assert.Equal("UniqueItem", found!.ItemName);
    }

    [Fact]
    public void TryGetByUid_UnknownUid_ReturnsNull()
    {
        // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
        byte[] buf = BuildRecord(itemUid: 100u);
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Null(cat.TryGetByUid(999u));
    }

    // =========================================================================
    // 6. Remainder raw slice
    // =========================================================================

    [Fact]
    public void Parse_RemainderRaw_HasExpectedLength()
    {
        // Remainder @ 0x2CA (338 bytes). UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §2.2 — remainder 0x2CA..0x41B: UNVERIFIED.
        const int expectedLen = 1052 - 0x2CA; // 338
        byte[] buf = BuildRecord();
        CitemsCatalog cat = CitemsParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(expectedLen, cat.Records[0].RemainderRaw.Length);
    }
}