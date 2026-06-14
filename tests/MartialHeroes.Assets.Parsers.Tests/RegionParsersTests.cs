using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="RegionGridParser"/> and <see cref="RegionZoneTableParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/specs/world_systems.md Ch. 16.
/// </summary>
public sealed class RegionParsersTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Writes a u32le value into a 4-byte array.</summary>
    private static byte[] Le4(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    /// <summary>
    /// Builds a minimal synthetic <c>region&lt;area&gt;.bin</c> buffer.
    /// Layout: u32le width | u32le height | u8[width*height] cells | u32le originX | u32le originZ.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — file layout: CONFIRMED.
    /// </summary>
    private static byte[] BuildRegionGrid(uint width, uint height, byte[] cells, uint originX, uint originZ)
    {
        using var ms = new System.IO.MemoryStream();
        ms.Write(Le4(width));
        ms.Write(Le4(height));
        ms.Write(cells);
        ms.Write(Le4(originX));
        ms.Write(Le4(originZ));
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a synthetic <c>regiontable&lt;area&gt;.bin</c> buffer: 32 records × 48 bytes.
    /// Sets the zone-type u32le at +40 per the supplied <paramref name="zoneTypes"/> array.
    /// All other bytes are zero.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2.
    /// </summary>
    private static byte[] BuildZoneTable(uint[] zoneTypes)
    {
        Assert.Equal(32, zoneTypes.Length); // exactly 32 records
        var buf = new byte[RegionZoneTableParser.ExpectedTableSize]; // 32 × 48 = 1 536
        for (int i = 0; i < 32; i++)
        {
            int offset = i * RegionZoneTableParser.RecordStride + 40; // +40 = zone-type field
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), zoneTypes[i]);
        }

        return buf;
    }

    // =========================================================================
    // RegionGridParser tests
    // =========================================================================

    [Fact]
    public void RegionGridParser_Parse_Reads_Dimensions_And_Origin()
    {
        // Arrange — 2×3 grid, cells 0..5, originX=1000, originZ=2000.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — width/height/cells/originX/originZ.
        uint width = 2, height = 3;
        byte[] cells = [0, 1, 2, 3, 4, 5];
        byte[] data = BuildRegionGrid(width, height, cells, originX: 1000, originZ: 2000);

        // Act
        RegionGridData result = RegionGridParser.Parse(data.AsMemory());

        // Assert
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(1000u, result.OriginX);
        Assert.Equal(2000u, result.OriginZ);
        Assert.Equal(cells, result.Cells.ToArray());
    }

    [Fact]
    public void RegionGridParser_Parse_ZeroSized_Grid_Succeeds()
    {
        // A 0×0 grid is a degenerate but valid edge case (no cells, two origin fields follow immediately).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "yields region id 0" for out-of-bounds.
        byte[] data = BuildRegionGrid(0, 0, [], originX: 0, originZ: 0);

        RegionGridData result = RegionGridParser.Parse(data.AsMemory());

        Assert.Equal(0u, result.Width);
        Assert.Equal(0u, result.Height);
        Assert.Empty(result.Cells.ToArray());
    }

    [Fact]
    public void RegionGridParser_Parse_ThrowsOn_TruncatedBuffer()
    {
        // Declare a 4×4 grid (16 cells) but provide only 10 bytes — too short.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — truncation must be rejected.
        using var ms = new System.IO.MemoryStream();
        ms.Write(Le4(4)); // width = 4
        ms.Write(Le4(4)); // height = 4
        // Only 2 cells instead of 16, no origin fields.
        ms.Write(new byte[] { 0, 1 });

        Assert.Throws<InvalidDataException>(() =>
            RegionGridParser.Parse(ms.ToArray().AsMemory()));
    }

    [Fact]
    public void RegionGridParser_Parse_CellBytes_SlicedFromOriginalMemory()
    {
        // Verify that cell bytes are correct for a specific layout.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "1 byte per cell = region id (0..31)": CONFIRMED.
        byte[] cells = [7, 3, 0, 15, 31, 2];
        byte[] data = BuildRegionGrid(3, 2, cells, originX: 512, originZ: 256);

        RegionGridData result = RegionGridParser.Parse(data.AsMemory());

        Assert.Equal(cells, result.Cells.ToArray());
    }

    // =========================================================================
    // RegionZoneTableParser tests
    // =========================================================================

    [Fact]
    public void RegionZoneTableParser_Parse_Returns_32_Records()
    {
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "fixed 32 records": CONFIRMED.
        uint[] zoneTypes = new uint[32]; // all Safe (0)
        byte[] data = BuildZoneTable(zoneTypes);

        RegionZoneRecord[] records = RegionZoneTableParser.Parse(data.AsMemory());

        Assert.Equal(32, records.Length);
    }

    [Fact]
    public void RegionZoneTableParser_Parse_ZoneTypeField_AtPlusForty()
    {
        // Zone-type u32le at record offset +40.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+40 4 u32 zone type": CONFIRMED.
        uint[] zoneTypes = new uint[32];
        zoneTypes[0] = 0; // Safe    (PLAUSIBLE)
        zoneTypes[1] = 1; // OpenPvp (CONFIRMED)
        zoneTypes[5] = 2; // Closed  (CONFIRMED)
        zoneTypes[31] = 3; // Unknown (UNVERIFIED)

        byte[] data = BuildZoneTable(zoneTypes);
        RegionZoneRecord[] records = RegionZoneTableParser.Parse(data.AsMemory());

        Assert.Equal(0u, records[0].ZoneTypeRaw);
        Assert.Equal(1u, records[1].ZoneTypeRaw);
        Assert.Equal(2u, records[5].ZoneTypeRaw);
        Assert.Equal(3u, records[31].ZoneTypeRaw);
    }

    [Fact]
    public void RegionZoneTableParser_Parse_RegionId_IsIndex()
    {
        // Each record's RegionId must equal its index in the array.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "indexed directly by region id (0..31)": CONFIRMED.
        uint[] zoneTypes = new uint[32];
        byte[] data = BuildZoneTable(zoneTypes);

        RegionZoneRecord[] records = RegionZoneTableParser.Parse(data.AsMemory());

        for (int i = 0; i < 32; i++)
            Assert.Equal(i, records[i].RegionId);
    }

    [Fact]
    public void RegionZoneTableParser_Parse_ThrowsOn_ShortBuffer()
    {
        // A buffer of 1 535 bytes is one byte short of the 1 536-byte minimum.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "32 records × 48 bytes = 1 536 bytes": CONFIRMED.
        byte[] shortBuffer = new byte[RegionZoneTableParser.ExpectedTableSize - 1];

        Assert.Throws<InvalidDataException>(() =>
            RegionZoneTableParser.Parse(shortBuffer.AsMemory()));
    }

    [Fact]
    public void RegionZoneTableParser_Parse_OpaqueLeading_Is40Bytes()
    {
        // Opaque leading bytes at +0..+39: exactly 40 bytes per record.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+0 40 (opaque)": UNVERIFIED size.
        uint[] zoneTypes = new uint[32];
        byte[] data = BuildZoneTable(zoneTypes);

        RegionZoneRecord[] records = RegionZoneTableParser.Parse(data.AsMemory());

        Assert.Equal(40, records[0].OpaqueLeading.Length);
    }

    [Fact]
    public void RegionZoneTableParser_Parse_OpaqueTrailing_Is4Bytes()
    {
        // Opaque trailing bytes at +44..+47: exactly 4 bytes per record.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+44 4 (opaque) trailing bytes": UNVERIFIED.
        uint[] zoneTypes = new uint[32];
        byte[] data = BuildZoneTable(zoneTypes);

        RegionZoneRecord[] records = RegionZoneTableParser.Parse(data.AsMemory());

        Assert.Equal(4, records[0].OpaqueTrailing.Length);
    }

    [Fact]
    public void RegionZoneTableParser_Parse_AcceptsBuffer_LargerThan1536()
    {
        // Extra bytes beyond 1 536 must be silently ignored.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "fixed record count 32".
        uint[] zoneTypes = new uint[32];
        byte[] baseData = BuildZoneTable(zoneTypes);
        byte[] padded = new byte[baseData.Length + 128]; // 128 extra bytes
        baseData.CopyTo(padded, 0);

        RegionZoneRecord[] records = RegionZoneTableParser.Parse(padded.AsMemory());

        Assert.Equal(32, records.Length);
    }
}