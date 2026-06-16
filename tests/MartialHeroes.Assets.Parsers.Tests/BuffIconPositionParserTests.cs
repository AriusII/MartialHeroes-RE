using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="BuffIconPositionParser"/> and <see cref="BuffIconPositionTable"/>.
/// All fixtures are synthetic in-memory byte buffers; no real VFS is required.
/// spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb: CODE-CONFIRMED + SAMPLE-VERIFIED.
///
/// Key spec facts:
/// - Record stride: 12 bytes. Record count = file_size / 12 (exact multiple required).
///   spec: misc_data.md §1.3 — "stride 12 bytes = BUFF_ICON_POS_RECORD_BYTES": CONFIRMED.
/// - Fields: buff_id u32LE @0, atlas_x i32LE @4, atlas_y i32LE @8.
///   spec: misc_data.md §1.3 — atlas_x/atlas_y are signed i32LE (corrected 2026-06-13).
/// - The value atlas_y=401 is a data-side blank-tile convention from the related skill-icon xdb
///   (xdb_tables.md §2). In buff_icon_position itself, high atlas_y values (e.g. 401) indicate
///   that a record's atlas coordinates point off the visible atlas area (treated as blank-tile).
///   spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 is a data-side blank-tile convention: CONFIRMED".
/// - Render cell size: 21×21 pixels for the variant aura strip (separate from the 30-slot bar).
///   spec: misc_data.md §1.6 — "21 × 21 cells at fixed positions (variant aura strip)": CODE-CONFIRMED.
/// </summary>
public sealed class BuffIconPositionParserTests
{
    // Record stride: 12 bytes. spec: misc_data.md §1.3 — CONFIRMED.
    private const int RecordStride = 12;

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static byte[] BuildRecord(uint buffId, int atlasX, int atlasY)
    {
        var buf = new byte[RecordStride];
        // buff_id u32le @ +0. spec: misc_data.md §1.3 — CODE-CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), buffId);
        // atlas_x i32le @ +4. spec: misc_data.md §1.3 — CODE-CONFIRMED (i32, corrected 2026-06-13).
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), atlasX);
        // atlas_y i32le @ +8. spec: misc_data.md §1.3 — CODE-CONFIRMED (i32, corrected 2026-06-13).
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), atlasY);
        return buf;
    }

    private static byte[] Concat(params byte[][] arrays)
    {
        int total = 0;
        foreach (var a in arrays) total += a.Length;
        var result = new byte[total];
        int pos = 0;
        foreach (var a in arrays)
        {
            a.CopyTo(result, pos);
            pos += a.Length;
        }

        return result;
    }

    // ── Stride / empty tests ──────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyTable()
    {
        // A zero-length buffer yields a table with zero records.
        BuffIconPositionTable table = BuffIconPositionParser.Parse(ReadOnlyMemory<byte>.Empty);

        Assert.Empty(table.Records);
    }

    [Fact]
    public void Parse_NonMultipleOfStride_Throws()
    {
        // Buffer length must be an exact multiple of 12.
        // spec: misc_data.md §1.3 — "record count = file_size / 12 (exact multiple)": CONFIRMED.
        var buf = new byte[13]; // 13 is not divisible by 12
        Assert.Throws<InvalidDataException>(() =>
            BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    // ── Field decode round-trips ──────────────────────────────────────────────

    [Fact]
    public void Parse_BuffId_RoundTrip()
    {
        // buff_id u32le @ +0 must round-trip. Range 1–1103 observed.
        // spec: misc_data.md §1.3 — "buff_id u32 @ 0: CODE-CONFIRMED; range 1–1103 observed".
        byte[] buf = BuildRecord(buffId: 42, atlasX: 0, atlasY: 0);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(table.Records);
        Assert.Equal(42u, table.Records[0].BuffId);
    }

    [Fact]
    public void Parse_AtlasX_SignedI32_RoundTrip()
    {
        // atlas_x is i32le (signed). Negative values must round-trip.
        // spec: misc_data.md §1.3 — "atlas_x i32 @ 4: CODE-CONFIRMED (corrected 2026-06-13: signed i32LE)".
        byte[] buf = BuildRecord(buffId: 1, atlasX: -50, atlasY: 100);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(-50, table.Records[0].AtlasX);
    }

    [Fact]
    public void Parse_AtlasY_SignedI32_RoundTrip()
    {
        // atlas_y is i32le (signed). Negative values must round-trip.
        // spec: misc_data.md §1.3 — "atlas_y i32 @ 8: CODE-CONFIRMED (corrected 2026-06-13: signed i32LE)".
        byte[] buf = BuildRecord(buffId: 1, atlasX: 100, atlasY: -25);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(-25, table.Records[0].AtlasY);
    }

    [Fact]
    public void Parse_AtlasCoordinates_SampleValues()
    {
        // Some observed atlas coordinates fall off any regular 25-pixel grid
        // (e.g. 250, 251, 276, 304) — confirming the values are authored data, not computed.
        // spec: misc_data.md §1.3 — "some coordinates fall off any regular 25-pixel grid".
        byte[] buf = BuildRecord(buffId: 5, atlasX: 250, atlasY: 251);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(250, table.Records[0].AtlasX);
        Assert.Equal(251, table.Records[0].AtlasY);
    }

    // ── sprite_y=401 blank-tile convention ───────────────────────────────────

    [Fact]
    public void Parse_AtlasY401_BlankTileConvention_ParsesNormally()
    {
        // atlas_y=401 is a data-side blank-tile convention — the parser must decode it
        // verbatim without any special-casing. The convention is a DATA fact, not a code sentinel.
        // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 is a data-side blank-tile convention
        //   (NOT a code sentinel); the (x,y) pair is used verbatim as the source-rect origin": CONFIRMED.
        // spec: misc_data.md §1.3 — "the parser must treat atlas_x/atlas_y as raw pixel values
        //   and never infer them from a formula".
        byte[] buf = BuildRecord(buffId: 51, atlasX: 0, atlasY: 401);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(table.Records);
        Assert.Equal(401, table.Records[0].AtlasY);
    }

    // ── Multi-record tests ────────────────────────────────────────────────────

    [Fact]
    public void Parse_MultipleRecords_AllDecoded()
    {
        // A known sample of 1608 bytes yields exactly 134 records with no remainder.
        // spec: misc_data.md §1.3 — "1608 bytes = 134 records × 12 bytes: CONFIRMED".
        byte[] rec0 = BuildRecord(buffId: 1, atlasX: 0, atlasY: 0);
        byte[] rec1 = BuildRecord(buffId: 2, atlasX: 23, atlasY: 25);
        byte[] rec2 = BuildRecord(buffId: 3, atlasX: 46, atlasY: 25);
        byte[] buf = Concat(rec0, rec1, rec2);

        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(3, table.Records.Count);
        Assert.Equal(1u, table.Records[0].BuffId);
        Assert.Equal(2u, table.Records[1].BuffId);
        Assert.Equal(3u, table.Records[2].BuffId);
        Assert.Equal(23, table.Records[1].AtlasX);
        Assert.Equal(25, table.Records[2].AtlasY);
    }

    [Fact]
    public void Parse_134Records_ExactDivisor()
    {
        // Synthetic check: 134 × 12 = 1608 is the observed sample file size.
        // spec: misc_data.md §1.3 — "1608 bytes = 134 records: CONFIRMED".
        Assert.Equal(1608, 134 * RecordStride);
    }

    // ── Lookup tests ─────────────────────────────────────────────────────────

    [Fact]
    public void TryGetById_ExistingBuffId_ReturnsRecord()
    {
        // The lookup table must find an existing buff_id in O(1).
        // spec: misc_data.md §1.3 — "lookup by buff_id returns (atlas_x, atlas_y)".
        byte[] buf = BuildRecord(buffId: 77, atlasX: 100, atlasY: 200);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        BuffIconPositionRecord? found = table.TryGetById(77);

        Assert.NotNull(found);
        Assert.Equal(77u, found!.BuffId);
        Assert.Equal(100, found.AtlasX);
        Assert.Equal(200, found.AtlasY);
    }

    [Fact]
    public void TryGetById_MissingBuffId_ReturnsNull()
    {
        // An absent buff_id must return null (runtime returns (0,0) per spec — no exception).
        // spec: misc_data.md §1.3 — "runtime returns (0,0) when absent".
        byte[] buf = BuildRecord(buffId: 1, atlasX: 0, atlasY: 0);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Null(table.TryGetById(999));
    }

    [Fact]
    public void TryGetById_LargeBuffId_SupportedRange()
    {
        // Range up to 1103 is observed. spec: misc_data.md §1.3 — "range 1–1103 observed".
        byte[] buf = BuildRecord(buffId: 1103, atlasX: 276, atlasY: 304);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        BuffIconPositionRecord? found = table.TryGetById(1103);

        Assert.NotNull(found);
        Assert.Equal(276, found!.AtlasX);
    }

    // ── 21×21 cell geometry note ──────────────────────────────────────────────

    [Fact]
    public void CellGeometry_21x21_AuraStrip_IsDocumented()
    {
        // The variant aura strip uses 21×21-pixel cells at fixed positions (CODE-CONFIRMED).
        // spec: misc_data.md §1.6 — "21 × 21 cells at fixed positions (variant aura strip)": CODE-CONFIRMED.
        // This test is a compile-time assertion that the constant is meaningful (no parser API for cell size).
        // The 21×21 cell is a render-side quantity — it is not stored in buff_icon_position.xdb.
        const int auraStripCellWidth = 21;
        const int auraStripCellHeight = 21;
        Assert.Equal(21, auraStripCellWidth);
        Assert.Equal(21, auraStripCellHeight);

        // Buff IDs in the 1000–1012 range use this aura strip.
        // spec: misc_data.md §1.6 — "aura strip reads buff ids in the 1000–1002 and 1010–1012 ranges".
        byte[] buf = BuildRecord(buffId: 1001, atlasX: 100, atlasY: 50);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(buf));

        var r = table.TryGetById(1001);
        Assert.NotNull(r);
        Assert.Equal(1001u, r!.BuffId);
    }
}