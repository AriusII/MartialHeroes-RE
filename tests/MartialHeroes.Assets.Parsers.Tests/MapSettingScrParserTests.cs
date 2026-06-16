using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="MapSettingScrParser"/> — per-zone map settings.
/// All buffers are built in-memory; no real game file is required.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.17.6 mapsetting.scr:
///   "stride 84 bytes (0x54), 52 records (4,368 / 84 = 52, exact)": SAMPLE-VERIFIED.
///   "No file header; record count = file_size / 84": SAMPLE-VERIFIED.
///   "zone_id u32 @ +0": SAMPLE-VERIFIED.
///   "zone_name CP949 inline within 84-byte record": SAMPLE-VERIFIED (presence).
///   "XZ world bounds (4 × f32) somewhere in body": SAMPLE-VERIFIED (presence);
///   exact inner offsets UNVERIFIED per §2.17.6.
/// </remarks>
public sealed class MapSettingScrParserTests
{
    // ─── binary helpers ────────────────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteI32LE(byte[] buf, int offset, int v) =>
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    /// <summary>
    /// Builds a mapsetting.scr fixture with one record, using the field offsets established
    /// in the implementation (which cites <c>misc_data.md §7.1</c> for the precise inner offsets).
    /// </summary>
    /// <remarks>
    /// Per <c>config_tables.md §2.17.6</c> the exact inner offsets are UNVERIFIED by spec;
    /// the implementation uses the detailed offsets from <c>misc_data.md §7.1</c>.
    /// This test validates the record-level shape (stride, zone_id, name presence, bounds presence).
    /// </remarks>
    private static byte[] BuildOneZoneRecord(
        int zoneId,
        string zoneName,
        int worldMinX, int worldMinZ, int worldMaxX, int worldMaxZ)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        // 84 bytes per record; no file header.
        // spec: Docs/RE/formats/config_tables.md §2.17.6 — "stride 84 bytes (0x54)": SAMPLE-VERIFIED.
        byte[] buf = new byte[84];

        // zone_id i32LE @ 0x00.
        // spec: Docs/RE/formats/config_tables.md §2.17.6 — "zone_id u32 @ +0": SAMPLE-VERIFIED.
        // (implementation uses i32; same bit pattern for positive ids)
        WriteI32LE(buf, 0x00, zoneId);

        // zone_name CP949 char[36] @ 0x04.
        // The inner offset 0x04 is from misc_data.md §7.1 (SAMPLE-VERIFIED there).
        // config_tables.md §2.17.6 confirms name presence only.
        byte[] nameBytes = cp949.GetBytes(zoneName);
        int nameLen = Math.Min(nameBytes.Length, 35); // leave NUL at end
        nameBytes.AsSpan(0, nameLen).CopyTo(buf.AsSpan(0x04, 36));

        // XZ world bounds i32×4 @ 0x28..0x37.
        // The inner offsets 0x28..0x37 are from misc_data.md §7.1 (PLAUSIBLE there).
        // config_tables.md §2.17.6 confirms bounds presence only.
        WriteI32LE(buf, 0x28, worldMinX);
        WriteI32LE(buf, 0x2C, worldMinZ);
        WriteI32LE(buf, 0x30, worldMaxX);
        WriteI32LE(buf, 0x34, worldMaxZ);

        return buf;
    }

    // =========================================================================
    // 1. Stride 84 bytes and count formula
    // =========================================================================

    [Fact]
    public void Stride84_CountFormula_52Records_Exact()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.6 —
        //   "4,368 / 84 = 52 (exact)": SAMPLE-VERIFIED.
        Assert.Equal(0, 4368 % 84);
        Assert.Equal(52, 4368 / 84);
    }

    [Fact]
    public void Parse_RecordCount_DecodedFromStrideDivision()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.6 —
        //   "No file header; record count = file_size / 84": SAMPLE-VERIFIED.
        byte[] rec = BuildOneZoneRecord(1, "테스트", 0, 0, 1024, 1024);

        // Single record: 84 bytes → 1 zone.
        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(rec));

        Assert.Single(zones);
    }

    // =========================================================================
    // 2. zone_id decoded
    // =========================================================================

    [Fact]
    public void Parse_ZoneId_Decoded()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.6 — "zone_id u32 @ +0": SAMPLE-VERIFIED.
        byte[] rec = BuildOneZoneRecord(42, "존A", 0, 0, 512, 512);

        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(rec));

        Assert.Equal(42, zones[0].ZoneId);
    }

    // =========================================================================
    // 3. zone_name CP949 decoded
    // =========================================================================

    [Fact]
    public void Parse_ZoneName_CP949_Decoded()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.6 —
        //   "zone_name CP949 inline within 84-byte record": SAMPLE-VERIFIED (presence).
        // Uses a known Korean zone name encoded in CP949.
        byte[] rec = BuildOneZoneRecord(1, "마을", 0, 0, 1024, 1024);

        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(rec));

        Assert.Equal("마을", zones[0].ZoneName);
    }

    [Fact]
    public void Parse_ZoneName_EmptyString_ForNulFirst()
    {
        // When the name field starts with NUL (all-zero), ZoneName should be empty.
        byte[] rec = BuildOneZoneRecord(5, "", 0, 0, 512, 512);

        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(rec));

        Assert.Equal(string.Empty, zones[0].ZoneName);
    }

    // =========================================================================
    // 4. XZ world bounds decoded
    // =========================================================================

    [Fact]
    public void Parse_WorldBounds_Decoded()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.6 —
        //   "XZ world bounds (4 × f32) somewhere in body": SAMPLE-VERIFIED (presence).
        // Exact inner offsets come from misc_data.md §7.1 (PLAUSIBLE there).
        byte[] rec = BuildOneZoneRecord(2, "들판", -512, -256, 2048, 1024);

        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(rec));

        Assert.Equal(-512, zones[0].WorldMinX);
        Assert.Equal(-256, zones[0].WorldMinZ);
        Assert.Equal(2048, zones[0].WorldMaxX);
        Assert.Equal(1024, zones[0].WorldMaxZ);
    }

    // =========================================================================
    // 5. Two records — stride alignment verified
    // =========================================================================

    [Fact]
    public void Parse_TwoRecords_StrideMaintained()
    {
        // Validates that the stride is correct: second record starts at byte 84.
        byte[] rec0 = BuildOneZoneRecord(10, "지역A", 0, 0, 1024, 1024);
        byte[] rec1 = BuildOneZoneRecord(20, "지역B", 1024, 0, 2048, 1024);
        byte[] buf = new byte[168]; // 2 × 84
        rec0.CopyTo(buf, 0);
        rec1.CopyTo(buf, 84);

        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, zones.Length);
        Assert.Equal(10, zones[0].ZoneId);
        Assert.Equal(20, zones[1].ZoneId);
        Assert.Equal("지역B", zones[1].ZoneName);
    }

    // =========================================================================
    // 6. Error: non-multiple-of-84 buffer
    // =========================================================================

    [Fact]
    public void Parse_NonMultipleOf84_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.6 —
        //   "record count = file_size / 84 (exact multiple required)".
        byte[] bad = new byte[85]; // 85 is not a multiple of 84

        Assert.Throws<InvalidDataException>(() => MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Parse_EmptyBuffer_YieldsZeroRecords()
    {
        // An empty buffer is 0 records (0 / 84 = 0).
        MapZoneRecord[] zones = MapSettingScrParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(zones);
    }
}