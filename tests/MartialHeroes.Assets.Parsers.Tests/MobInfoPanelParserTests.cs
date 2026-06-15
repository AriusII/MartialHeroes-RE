using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="MobInfoPanelParser"/> (.mi format).
/// Tests exercise the mechanical decode only (container structure + 7×u32 field stride).
/// Field semantics are PLAUSIBLE/UNRESOLVED (live-debugger-pending) — asserted only as
/// opaque u32 values, never as business-semantic assertions.
/// spec: Docs/RE/formats/mi.md §Container structure + §Record layout.
/// </summary>
public sealed class MobInfoPanelParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    // Container: 4-byte header + recordCount × 28-byte records.
    // spec: Docs/RE/formats/mi.md §Container structure — "4 + recordCount × 28 = file size": HIGH.
    private const int HeaderSize = 4;    // spec: §Header layout — recordCount u32le @ 0x00: HIGH.
    private const int RecordStride = 28; // spec: §Record layout — "Record stride: 28 bytes": HIGH.

    /// <summary>
    /// Builds a syntactically correct .mi buffer containing the given widget records.
    /// Each record is 7 × u32le. Semantics of fields 1-6 are PLAUSIBLE (not asserted here).
    /// spec: Docs/RE/formats/mi.md §Record layout — "28 bytes per record (7 × u32le)": HIGH.
    /// </summary>
    private static byte[] BuildMiBuffer(params uint[][] widgetFields)
    {
        // widgetFields[i] must be length 7.
        byte[] buf = new byte[HeaderSize + widgetFields.Length * RecordStride];

        // recordCount u32le @ 0x00.
        // spec: Docs/RE/formats/mi.md §Header layout — "recordCount u32le @ 0x00": HIGH.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00, 4), (uint)widgetFields.Length);

        for (int i = 0; i < widgetFields.Length; i++)
        {
            int recBase = HeaderSize + i * RecordStride;
            uint[] fields = widgetFields[i];
            for (int f = 0; f < 7; f++)
            {
                // Each of the 7 fields is u32le.
                // spec: Docs/RE/formats/mi.md §Record layout — "7 × u32le": HIGH.
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(recBase + f * 4, 4), fields[f]);
            }
        }

        return buf;
    }

    // =========================================================================
    // 1. Container validation
    // =========================================================================

    [Fact]
    public void Parse_TooShortForHeader_ThrowsInvalidDataException()
    {
        // Buffer shorter than 4 bytes cannot hold the record-count header.
        // spec: Docs/RE/formats/mi.md §Header layout — "recordCount u32le @ 0x00": HIGH.
        byte[] buf = new byte[3];
        Assert.Throws<InvalidDataException>(() => MobInfoPanelParser.Parse(buf.AsSpan()));
    }

    [Fact]
    public void Parse_ZeroRecords_YieldsEmptyContainer()
    {
        // count = 0 → 4 bytes total, valid. Records array is empty.
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + 0 × 28 = 4 bytes".
        byte[] buf = BuildMiBuffer(); // 0 records
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());

        Assert.Equal(0u, result.RecordCount);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Parse_WrongLength_ThrowsInvalidDataException()
    {
        // count = 2 but only 1-record worth of body bytes → size mismatch.
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + recordCount × 28 = file size": HIGH.
        byte[] buf = new byte[HeaderSize + RecordStride]; // space for 1 record
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), 2u); // claims 2 records
        Assert.Throws<InvalidDataException>(() => MobInfoPanelParser.Parse(buf.AsSpan()));
    }

    // =========================================================================
    // 2. Record count and stride (mechanical decode, no semantic assertions)
    // =========================================================================

    [Fact]
    public void Parse_OneRecord_RecordCountIsOne()
    {
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + 1 × 28 = 32 bytes".
        byte[] buf = BuildMiBuffer([1u, 0u, 0u, 0u, 0u, 0u, 0u]);
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());

        Assert.Equal(1u, result.RecordCount);
        Assert.Single(result.Records);
    }

    [Fact]
    public void Parse_ThreeRecords_RecordCountIsThree()
    {
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + 3 × 28 = 88 bytes".
        byte[] buf = BuildMiBuffer(
            [1u, 0u, 0u, 0u, 0u, 0u, 0u],
            [2u, 0u, 0u, 0u, 0u, 0u, 0u],
            [3u, 0u, 0u, 0u, 0u, 0u, 0u]);
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());

        Assert.Equal(3u, result.RecordCount);
        Assert.Equal(3, result.Records.Length);
    }

    // =========================================================================
    // 3. Field 0 (WidgetId) — opaque u32 round-trip (PLAUSIBLE sequential ordinal)
    // NOTE: field semantics are PLAUSIBLE (spec mi.md — live-debugger-pending).
    //       Tests only confirm that the u32 value stored at field-0 is decoded correctly.
    // =========================================================================

    [Fact]
    public void Parse_WidgetId_OpaqueU32_RoundTrips()
    {
        // Field 0 (+0x00) is decoded as a u32; the specific value round-trips.
        // spec: Docs/RE/formats/mi.md §Record layout field 0 @ +0x00 — "structure SAMPLE-VERIFIED / meaning PLAUSIBLE".
        byte[] buf = BuildMiBuffer([42u, 0u, 0u, 0u, 0u, 0u, 0u]);
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());
        Assert.Equal(42u, result.Records[0].WidgetId);
    }

    [Fact]
    public void Parse_AllSevenFields_OpaqueU32_RoundTrip()
    {
        // All 7 fields in a record must be decoded correctly (opaque u32 checks only).
        // spec: Docs/RE/formats/mi.md §Record layout — "7 × u32le": HIGH (stride); PLAUSIBLE (semantics).
        byte[] buf = BuildMiBuffer([10u, 20u, 19u, 5u, 100u, 101u, 5u]);
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());
        MiWidgetRecord rec = result.Records[0];

        Assert.Equal(10u, rec.WidgetId);
        Assert.Equal(20u, rec.FieldA0);
        Assert.Equal(19u, rec.FieldA1);
        Assert.Equal(5u, rec.FieldKind);
        Assert.Equal(100u, rec.FieldB0);
        Assert.Equal(101u, rec.FieldB1);
        Assert.Equal(5u, rec.FieldLink);
    }

    [Fact]
    public void Parse_NoneSentinel_RoundTrips()
    {
        // 0xFFFFFFFF is the none-sentinel observed in optional fields.
        // spec: Docs/RE/formats/mi.md §Record layout — "None sentinel: 0xFFFFFFFF": HIGH (value); PLAUSIBLE (field set).
        const uint none = 0xFFFFFFFF;
        byte[] buf = BuildMiBuffer([1u, none, none, 2u, 50u, none, none]);
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());
        MiWidgetRecord rec = result.Records[0];

        Assert.Equal(none, rec.FieldA0);
        Assert.Equal(none, rec.FieldA1);
        Assert.Equal(none, rec.FieldB1);
        Assert.Equal(none, rec.FieldLink);
    }

    // =========================================================================
    // 4. Cross-record isolation
    // =========================================================================

    [Fact]
    public void Parse_TwoRecords_IndependentFieldDecoding()
    {
        // Two records must not cross-contaminate field values.
        // spec: Docs/RE/formats/mi.md §Record layout — stride 28 bytes: HIGH.
        byte[] buf = BuildMiBuffer(
            [1u, 100u, 99u, 1u, 200u, 201u, 1u],
            [2u, 300u, 299u, 2u, 400u, 401u, 2u]);
        MiPanelData result = MobInfoPanelParser.Parse(buf.AsSpan());

        Assert.Equal(1u, result.Records[0].WidgetId);
        Assert.Equal(100u, result.Records[0].FieldA0);

        Assert.Equal(2u, result.Records[1].WidgetId);
        Assert.Equal(300u, result.Records[1].FieldA0);
    }

    // =========================================================================
    // 5. ReadOnlyMemory overload
    // =========================================================================

    [Fact]
    public void Parse_MemoryOverload_SameResultAsSpanOverload()
    {
        // Both overloads must yield the same result.
        byte[] buf = BuildMiBuffer([7u, 0u, 0u, 3u, 0u, 0u, 0u]);
        MiPanelData fromSpan = MobInfoPanelParser.Parse(buf.AsSpan());
        MiPanelData fromMemory = MobInfoPanelParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(fromSpan.RecordCount, fromMemory.RecordCount);
        Assert.Equal(fromSpan.Records[0].WidgetId, fromMemory.Records[0].WidgetId);
    }
}
