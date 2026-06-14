using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="BgtextureLstParser"/>.
/// All buffers are built in-memory; no real game file is required.
/// spec: Docs/RE/formats/bgtexture_lst.md
/// </summary>
public sealed class BgtextureLstParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a syntactically correct bgtexture.lst buffer with the given records.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Header layout + §Record / body layout: CONFIRMED.
    /// Layout: u32LE record_count @ 0, then record_count × 48-byte records.
    /// Each record: u8 kind @ +0 + char[47] relpath (null-terminated, zero-padded) @ +1.
    /// </summary>
    private static byte[] BuildLst(params (byte kind, string relPath)[] entries)
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED.
        int count = entries.Length;
        byte[] buf = new byte[4 + count * 48];

        // record_count u32LE @ 0x00.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)count);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        for (int i = 0; i < count; i++)
        {
            int recBase = 4 + i * 48;

            // kind u8 @ record +0.
            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — kind u8 @ +0: CONFIRMED.
            buf[recBase] = entries[i].kind;

            // rel_path char[47] @ record +1, null-terminated, zero-padded.
            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — rel_path char[47] @ +1: CONFIRMED.
            byte[] pathBytes = cp949.GetBytes(entries[i].relPath);
            int copyLen = Math.Min(pathBytes.Length, 46); // leave at least one NUL byte
            pathBytes.AsSpan(0, copyLen).CopyTo(buf.AsSpan(recBase + 1, 47));
            // bytes [recBase+1+copyLen .. recBase+48) are already zero (NUL padding).
        }

        return buf;
    }

    // =========================================================================
    // 1. Size formula: file_size = 4 + record_count * 48
    // =========================================================================

    /// <summary>
    /// Verifies the 48-byte stride and count formula.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula: CONFIRMED.
    /// map000 instance: 1222 records → 4 + 1222 × 48 = 58,660 bytes.
    /// effect instance: 1108 records → 4 + 1108 × 48 = 53,188 bytes.
    /// </summary>
    [Theory]
    [InlineData(1222, 58660)]   // data/map000/texture/bgtexture.lst
    [InlineData(1108, 53188)]   // data/effect/texture/bgtexture.lst
    [InlineData(0, 4)]          // degenerate: zero records
    [InlineData(1, 52)]         // single record
    public void SizeFormula_RecordCountTimesStride_PlusFourHeader(int recordCount, int expectedSize)
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout —
        //   "file_size = 4 + record_count * 48": CONFIRMED.
        int computedSize = 4 + recordCount * 48;
        Assert.Equal(expectedSize, computedSize);
    }

    // =========================================================================
    // 2. Record count decoded from header u32LE
    // =========================================================================

    [Fact]
    public void Parse_RecordCount_DecodedFromHeader()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
        byte[] buf = BuildLst(
            (0x01, "terrain/g3"),
            (0x01, "terrain/a1"),
            (0x01, "building/_castle"));

        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(3, cat.Count);
    }

    // =========================================================================
    // 3. Sample record: kind byte and relpath
    // =========================================================================

    [Fact]
    public void Parse_FirstRecord_KindAndRelPath_Decoded()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout —
        //   kind u8 @ +0: CONFIRMED (value 0x01); rel_path char[47] @ +1: CONFIRMED.
        byte[] buf = BuildLst((0x01, "terrain/g3"));

        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        BgtextureLstRecord rec = cat.Records[0];
        Assert.Equal(0x01, rec.Kind);
        Assert.Equal("terrain/g3", rec.RelPath);
    }

    [Fact]
    public void Parse_SecondRecord_HasIndex1()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md — "records are addressed by position": CONFIRMED.
        byte[] buf = BuildLst(
            (0x01, "terrain/a1"),
            (0x01, "building/_castle"));

        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0, cat.Records[0].Index);
        Assert.Equal(1, cat.Records[1].Index);
        Assert.Equal("building/_castle", cat.Records[1].RelPath);
    }

    // =========================================================================
    // 4. GetByPoolSlot O(1) look-up
    // =========================================================================

    [Fact]
    public void GetByPoolSlot_ReturnsRecord_ForValidIndex()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
        //   "intTexId - 1 gives the 0-based .lst record index": CONFIRMED.
        byte[] buf = BuildLst(
            (0x01, "terrain/a"),
            (0x01, "terrain/b"),
            (0x01, "terrain/c"));

        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        // Pool slot 2 (0-based) → relpath "terrain/c"
        BgtextureLstRecord? rec = cat.GetByPoolSlot(2);
        Assert.NotNull(rec);
        Assert.Equal("terrain/c", rec!.RelPath);
    }

    [Fact]
    public void GetByPoolSlot_ReturnsNull_ForOutOfRangeIndex()
    {
        byte[] buf = BuildLst((0x01, "terrain/x"));
        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Null(cat.GetByPoolSlot(999));
        Assert.Null(cat.GetByPoolSlot(-1));
    }

    // =========================================================================
    // 5. Zero-record (degenerate) file
    // =========================================================================

    [Fact]
    public void Parse_ZeroRecords_EmptyCatalog()
    {
        // A 4-byte file with record_count = 0 is valid.
        byte[] buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 0u);

        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0, cat.Count);
        Assert.Empty(cat.Records);
    }

    // =========================================================================
    // 6. Null-terminated relpath within 47-byte field
    // =========================================================================

    [Fact]
    public void Parse_RelPath_NullTerminatedWithinField()
    {
        // The relpath field is 47 bytes; we write a short path and the rest should be NUL.
        // Parser must stop at the first NUL.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout —
        //   "null-terminated, zero-padded to the full 47 bytes": CONFIRMED.
        byte[] buf = BuildLst((0x01, "short"));
        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal("short", cat.Records[0].RelPath);
    }

    // =========================================================================
    // 7. Corrupt: buffer too short for declared record count
    // =========================================================================

    [Fact]
    public void Parse_Truncated_ThrowsInvalidDataException()
    {
        // Claim 10 records but provide only 1 record's worth of body.
        byte[] buf = new byte[4 + 48]; // header says 10, body has only 1 record
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), 10u);

        Assert.Throws<InvalidDataException>(
            () => BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_EmptyBuffer_ThrowsInvalidDataException()
    {
        // Buffer shorter than the 4-byte header must throw.
        Assert.Throws<InvalidDataException>(
            () => BgtextureLstParser.Parse(ReadOnlyMemory<byte>.Empty));
    }

    // =========================================================================
    // 8. Cross-file join: 1-based intTexId → 0-based pool slot
    // =========================================================================

    [Fact]
    public void CrossFileJoin_MapIntTexId1_MapsToPoolSlot0()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
        //   "intTexId - 1 gives the 0-based .lst record index": CONFIRMED.
        byte[] buf = BuildLst(
            (0x01, "terrain/first"),
            (0x01, "terrain/second"));

        BgtextureLstCatalog cat = BgtextureLstParser.Parse(new ReadOnlyMemory<byte>(buf));

        // A .map file emitting intTexId=1 corresponds to pool slot 0 (1-based minus 1).
        int intTexId = 1;
        BgtextureLstRecord? rec = cat.GetByPoolSlot(intTexId - 1);
        Assert.NotNull(rec);
        Assert.Equal("terrain/first", rec!.RelPath);
    }
}
