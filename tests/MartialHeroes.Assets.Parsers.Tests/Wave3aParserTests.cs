using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for the three Wave-3a parsers:
/// <see cref="MudSoundGridParser"/> (<c>.mud</c>),
/// <see cref="RegionBinParser"/> (<c>region&lt;NNN&gt;.bin</c>),
/// <see cref="MobInfoPanelParser"/> (<c>.mi</c> / <c>mobinfo.mi</c>).
/// All fixtures are hand-built in-memory; no real VFS files are required.
/// spec: Docs/RE/formats/mud.md, Docs/RE/formats/region_grid.md, Docs/RE/formats/mi.md
/// </summary>
public sealed class Wave3aParserTests
{
    // =========================================================================
    // Shared helpers
    // =========================================================================

    private static void WriteU32Le(byte[] buf, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), value);

    // =========================================================================
    // MudSoundGridParser — spec: Docs/RE/formats/mud.md
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 32 768-byte <c>.mud</c> buffer.
    /// Every tile is filled with the supplied 8-byte tile template.
    /// spec: Docs/RE/formats/mud.md §Identification — "File size: fixed 32768 bytes (0x8000)": CONFIRMED.
    /// spec: Docs/RE/formats/mud.md §Tile layout (8 bytes): CONFIRMED.
    /// </summary>
    private static byte[] BuildMud(byte wlkSoundIndex = 0, byte runSoundIndex = 0,
        byte bgmZoneId = 0, byte bgeAmbientId0 = 0, byte bgeAmbientId1 = 0,
        byte effId0 = 0, byte effId1 = 0, byte effId2 = 0)
    {
        // spec: Docs/RE/formats/mud.md §Identification — "fixed 32768 bytes (0x8000)": CONFIRMED.
        var buf = new byte[MudSoundGrid.FixedFileSize]; // 32768

        // Fill all 4096 tiles with the template.
        // spec: Docs/RE/formats/mud.md §Grid geometry — "64 × 64 = 4096 tiles, stride 8 bytes": CONFIRMED.
        for (int i = 0; i < MudSoundGrid.TileCount; i++)
        {
            int off = i * 8; // spec: Docs/RE/formats/mud.md §Grid geometry — tile stride 8 bytes: CONFIRMED.
            buf[off + 0] = wlkSoundIndex; // spec: Docs/RE/formats/mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE
            buf[off + 1] = runSoundIndex; // spec: Docs/RE/formats/mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE
            buf[off + 2] = bgmZoneId; // spec: Docs/RE/formats/mud.md §Tile layout offset 2 — bgmZoneId: CONFIRMED.
            buf[off + 3] =
                bgeAmbientId0; // spec: Docs/RE/formats/mud.md §Tile layout offset 3 — bgeAmbientId0: CONFIRMED.
            buf[off + 4] =
                bgeAmbientId1; // spec: Docs/RE/formats/mud.md §Tile layout offset 4 — bgeAmbientId1: CONFIRMED.
            buf[off + 5] = effId0; // spec: Docs/RE/formats/mud.md §Tile layout offset 5 — effId0: CONFIRMED.
            buf[off + 6] = effId1; // spec: Docs/RE/formats/mud.md §Tile layout offset 6 — effId1: CONFIRMED.
            buf[off + 7] = effId2; // spec: Docs/RE/formats/mud.md §Tile layout offset 7 — effId2: CONFIRMED.
        }

        return buf;
    }

    [Fact]
    public void Mud_Parse_FixedSize_Returns4096Tiles()
    {
        // spec: Docs/RE/formats/mud.md §Grid geometry — "64 × 64 = 4096 tiles": CONFIRMED.
        byte[] data = BuildMud(bgmZoneId: 3);
        MudSoundGrid grid = MudSoundGridParser.Parse(data.AsMemory());
        Assert.Equal(MudSoundGrid.TileCount, grid.Tiles.Length); // 4096
    }

    [Fact]
    public void Mud_Parse_TileFields_RoundTrip()
    {
        // spec: Docs/RE/formats/mud.md §Tile layout (8 bytes): CONFIRMED (bgmZoneId, bgeAmbientId0/1, effId0/1/2).
        byte[] data = BuildMud(
            wlkSoundIndex: 0, runSoundIndex: 0,
            bgmZoneId: 5,
            bgeAmbientId0: 2, bgeAmbientId1: 7,
            effId0: 1, effId1: 3, effId2: 4);

        MudSoundGrid grid = MudSoundGridParser.Parse(data.AsSpan());

        // Check tile at index 0 (first tile in the row-major grid).
        MudSoundTile tile = grid.Tiles[0];
        Assert.Equal(0,
            tile.WlkSoundIndex); // spec: Docs/RE/formats/mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE
        Assert.Equal(0,
            tile.RunSoundIndex); // spec: Docs/RE/formats/mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE
        Assert.Equal(5, tile.BgmZoneId); // spec: Docs/RE/formats/mud.md §Tile layout offset 2 — bgmZoneId: CONFIRMED.
        Assert.Equal(2,
            tile.BgeAmbientId0); // spec: Docs/RE/formats/mud.md §Tile layout offset 3 — bgeAmbientId0: CONFIRMED.
        Assert.Equal(7,
            tile.BgeAmbientId1); // spec: Docs/RE/formats/mud.md §Tile layout offset 4 — bgeAmbientId1: CONFIRMED.
        Assert.Equal(1, tile.EffId0); // spec: Docs/RE/formats/mud.md §Tile layout offset 5 — effId0: CONFIRMED.
        Assert.Equal(3, tile.EffId1); // spec: Docs/RE/formats/mud.md §Tile layout offset 6 — effId1: CONFIRMED.
        Assert.Equal(4, tile.EffId2); // spec: Docs/RE/formats/mud.md §Tile layout offset 7 — effId2: CONFIRMED.
    }

    [Fact]
    public void Mud_Parse_LastTile_SameAsFirst_AllFilled()
    {
        // Confirm the last tile (index 4095) also round-trips — verifies the MemoryMarshal.Cast covers all tiles.
        // spec: Docs/RE/formats/mud.md §Grid geometry — "64 × 64 = 4096 tiles": CONFIRMED.
        byte[] data = BuildMud(bgmZoneId: 9, effId2: 6);
        MudSoundGrid grid = MudSoundGridParser.Parse(data.AsSpan());

        MudSoundTile last = grid.Tiles[MudSoundGrid.TileCount - 1]; // tile 4095
        Assert.Equal(9, last.BgmZoneId);
        Assert.Equal(6, last.EffId2);
    }

    [Fact]
    public void Mud_GetTile_WorldLookup_RowMajorFormula()
    {
        // Build a grid where only tile at col=3, row=2 has bgmZoneId=42.
        // Formula: tileIndex = col + (row << 6).
        // spec: Docs/RE/formats/mud.md §Indexing (world → tile) — "tile_index = col + (row << 6)": CONFIRMED.
        byte[] data = BuildMud(bgmZoneId: 0);
        int targetCol = 3;
        int targetRow = 2;
        int targetIndex = targetCol + (targetRow << 6); // = 3 + 128 = 131
        data[targetIndex * 8 + 2] = 42; // bgmZoneId = 42 for this tile.

        MudSoundGrid grid = MudSoundGridParser.Parse(data.AsSpan());

        // Access via GetTile: localX = col * 16, localZ = row * 16.
        // spec: Docs/RE/formats/mud.md §Indexing — "col = (local_x / 16) & 0x3F": CONFIRMED.
        MudSoundTile found =
            grid.GetTile(targetCol * MudSoundGrid.TileWorldSize, targetRow * MudSoundGrid.TileWorldSize);
        Assert.Equal(42, found.BgmZoneId);

        // Adjacent tile must still have bgmZoneId = 0.
        MudSoundTile adjacent = grid.GetTile((targetCol + 1) * MudSoundGrid.TileWorldSize,
            targetRow * MudSoundGrid.TileWorldSize);
        Assert.Equal(0, adjacent.BgmZoneId);
    }

    [Fact]
    public void Mud_Parse_WrongLength_ThrowsInvalidData()
    {
        // spec: Docs/RE/formats/mud.md §Identification — "loader reads exactly 0x8000 bytes" → wrong size must fail.
        byte[] tooShort = new byte[MudSoundGrid.FixedFileSize - 1];
        Assert.Throws<InvalidDataException>(() => MudSoundGridParser.Parse(tooShort.AsSpan()));
    }

    [Fact]
    public void Mud_Parse_TooLong_ThrowsInvalidData()
    {
        // An extra byte beyond 32768 must also fail — strict fixed-size check.
        // spec: Docs/RE/formats/mud.md §Identification — fixed size validation.
        byte[] tooLong = new byte[MudSoundGrid.FixedFileSize + 1];
        Assert.Throws<InvalidDataException>(() => MudSoundGridParser.Parse(tooLong.AsSpan()));
    }

    // =========================================================================
    // RegionBinParser — spec: Docs/RE/formats/region_grid.md
    // =========================================================================

    /// <summary>
    /// Builds a synthetic <c>region&lt;NNN&gt;.bin</c> buffer with the RUNTIME layout (Layout A).
    /// Layout: u32le width | u32le height | u8[width×height] cells | u32le originX | u32le originZ.
    /// spec: Docs/RE/formats/region_grid.md §Layout A — region&lt;NNN&gt;.bin (RUNTIME): HIGH.
    /// </summary>
    private static byte[] BuildRegionBin(uint width, uint height, byte[] cells, uint originX, uint originZ)
    {
        // spec: Docs/RE/formats/region_grid.md §Size derivation — "total = 16 + width × height".
        var buf = new byte[4 + 4 + cells.Length + 4 + 4];
        WriteU32Le(buf, 0, width); // spec: Docs/RE/formats/region_grid.md §Layout A — width u32le @ 0x00
        WriteU32Le(buf, 4, height); // spec: Docs/RE/formats/region_grid.md §Layout A — height u32le @ 0x04
        cells.CopyTo(buf, 8); // spec: Docs/RE/formats/region_grid.md §Layout A — regionIdGrid u8[] @ 0x08
        int originBase = 8 + cells.Length;
        WriteU32Le(buf, originBase,
            originX); // spec: Docs/RE/formats/region_grid.md §Layout A — originX u32le @ 0x08 + W×H
        WriteU32Le(buf, originBase + 4,
            originZ); // spec: Docs/RE/formats/region_grid.md §Layout A — originZ u32le @ 0x08 + W×H + 4
        return buf;
    }

    [Fact]
    public void RegionBin_Parse_Dimensions_RoundTrip()
    {
        // spec: Docs/RE/formats/region_grid.md §Layout A — width/height u32le @ 0x00/0x04: HIGH.
        byte[] cells = new byte[4 * 3]; // 4 × 3 grid
        byte[] data = BuildRegionBin(4, 3, cells, originX: 0, originZ: 0);

        RegionGrid grid = RegionBinParser.Parse(data.AsMemory());

        Assert.Equal(4, grid.Width);
        Assert.Equal(3, grid.Height);
    }

    [Fact]
    public void RegionBin_Parse_Origins_RoundTrip()
    {
        // spec: Docs/RE/formats/region_grid.md §Layout A — originX/Z u32le trailing the grid body: HIGH.
        byte[] cells = new byte[2 * 2];
        byte[] data = BuildRegionBin(2, 2, cells, originX: 512_000, originZ: 256_000);

        RegionGrid grid = RegionBinParser.Parse(data.AsSpan());

        Assert.Equal(512_000, grid.OriginX);
        Assert.Equal(256_000, grid.OriginZ);
    }

    [Fact]
    public void RegionBin_Parse_CellBytes_RoundTrip()
    {
        // spec: Docs/RE/formats/region_grid.md §Grid body layout — "one unsigned byte = region ID": HIGH.
        byte[] cells = [0x00, 0x01, 0x1F, 0x03]; // 2 × 2 grid; IDs 0, 1, 31, 3
        byte[] data = BuildRegionBin(2, 2, cells, 0, 0);

        RegionGrid grid = RegionBinParser.Parse(data.AsSpan());

        Assert.Equal(4, grid.Cells.Length);
        Assert.Equal(0x00, grid.Cells[0]);
        Assert.Equal(0x01, grid.Cells[1]);
        Assert.Equal(0x1F, grid.Cells[2]); // max legal region id = 31
        Assert.Equal(0x03, grid.Cells[3]);
    }

    [Fact]
    public void RegionBin_Parse_CellLookup_InBounds()
    {
        // A 3×3 grid where cell (col=1, row=2) = region id 7.
        // Formula: index = col + row × width.
        // spec: Docs/RE/formats/region_grid.md §Grid body layout — "index = col + row × width": HIGH.
        byte[] cells = new byte[3 * 3];
        cells[1 + 2 * 3] = 7; // col=1, row=2

        uint originX = 100_000;
        uint originZ = 200_000;
        byte[] data = BuildRegionBin(3, 3, cells, originX, originZ);

        RegionGrid grid = RegionBinParser.Parse(data.AsSpan());

        // World point that maps to col=1, row=2:
        // worldX = originX + col * 256 + 1 (any value in the cell).
        // spec: Docs/RE/formats/region_grid.md §Runtime use — "(X − originX) / 256": HIGH.
        int wX = (int)originX + 1 * RegionGrid.CellWorldSize + 5;
        int wZ = (int)originZ + 2 * RegionGrid.CellWorldSize + 10;

        byte id = grid.GetRegionId(wX, wZ);
        Assert.Equal(7, id);
    }

    [Fact]
    public void RegionBin_Parse_CellLookup_OutOfBounds_Returns0()
    {
        // Out-of-bounds access must return 0, not throw.
        // spec: Docs/RE/formats/region_grid.md §Runtime use — "Compute the row-major index and bounds-check".
        byte[] cells = new byte[2 * 2];
        byte[] data = BuildRegionBin(2, 2, cells, 0, 0);

        RegionGrid grid = RegionBinParser.Parse(data.AsSpan());

        // Far outside the grid.
        byte id = grid.GetRegionId(99_999, 99_999);
        Assert.Equal(0, id);
    }

    [Fact]
    public void RegionBin_Parse_TooShort_ThrowsInvalidData()
    {
        // spec: Docs/RE/formats/region_grid.md §Layout A — minimum valid size = 16 bytes.
        byte[] tooShort = new byte[10];
        Assert.Throws<InvalidDataException>(() => RegionBinParser.Parse(tooShort.AsSpan()));
    }

    [Fact]
    public void RegionBin_Parse_TruncatedCells_ThrowsInvalidData()
    {
        // Declare 5×5=25 cells but supply only 10 bytes of cell data — must fail.
        // spec: Docs/RE/formats/region_grid.md §Layout A — buffer length validation.
        var buf = new byte[4 + 4 + 10 + 4 + 4]; // missing 15 cells + bad origins
        WriteU32Le(buf, 0, 5); // width = 5
        WriteU32Le(buf, 4, 5); // height = 5  → need 25 cells, but only 10 bytes supplied
        Assert.Throws<InvalidDataException>(() => RegionBinParser.Parse(buf.AsSpan()));
    }

    [Fact]
    public void RegionBin_Parse_EmptyGrid_ZeroWidthHeight()
    {
        // Edge case: 0×0 grid is technically valid (16 bytes total: 8 dims + 0 cells + 8 origins).
        // spec: Docs/RE/formats/region_grid.md §Size derivation — "total = 16 + width × height".
        byte[] data = BuildRegionBin(0, 0, [], 1024, 2048);

        RegionGrid grid = RegionBinParser.Parse(data.AsSpan());

        Assert.Equal(0, grid.Width);
        Assert.Equal(0, grid.Height);
        Assert.Empty(grid.Cells);
        Assert.Equal(1024, grid.OriginX);
        Assert.Equal(2048, grid.OriginZ);
    }

    // =========================================================================
    // MobInfoPanelParser — spec: Docs/RE/formats/mi.md
    // =========================================================================

    /// <summary>
    /// Builds a synthetic <c>.mi</c> buffer with the given number of records.
    /// Container: 4-byte recordCount header + recordCount × 28-byte records.
    /// spec: Docs/RE/formats/mi.md §Container structure — "4 + recordCount × 28 = file size": HIGH.
    /// </summary>
    private static byte[] BuildMi(uint recordCount, Action<byte[], int, int>? fillRecord = null)
    {
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + recordCount × 28": HIGH.
        int totalSize = 4 + (int)recordCount * MiPanelData.RecordStride;
        var buf = new byte[totalSize];
        WriteU32Le(buf, 0, recordCount); // spec: Docs/RE/formats/mi.md §Header layout — recordCount u32 @ 0x00: HIGH.

        for (int i = 0; i < (int)recordCount; i++)
        {
            int recBase = 4 + i * MiPanelData.RecordStride;
            fillRecord?.Invoke(buf, i, recBase);
        }

        return buf;
    }

    /// <summary>Helper: writes a u32le into a byte array at the given offset.</summary>
    private static void WriteU32LeAt(byte[] buf, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), value);

    [Fact]
    public void Mi_Parse_RecordCount_21_Matches_Spec()
    {
        // The single observed instance has exactly 21 records.
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + 21 × 28 = 592 bytes": HIGH.
        byte[] data = BuildMi(21);
        Assert.Equal(592, data.Length); // spec: Docs/RE/formats/mi.md §Identification — "592 bytes"
        MiPanelData panel = MobInfoPanelParser.Parse(data.AsMemory());
        Assert.Equal(21u, panel.RecordCount);
        Assert.Equal(21, panel.Records.Length);
    }

    [Fact]
    public void Mi_Parse_RecordFields_RoundTrip()
    {
        // Build a 1-record file with known field values and verify they decode correctly.
        // spec: Docs/RE/formats/mi.md §Record layout — 7 × u32le per record: HIGH (stride); UNVERIFIED (semantics).
        byte[] data = BuildMi(1, (buf, _, recBase) =>
        {
            // spec: Docs/RE/formats/mi.md §Record layout offsets: HIGH (stride).
            WriteU32LeAt(buf, recBase + 0x00, 11); // widgetId
            WriteU32LeAt(buf, recBase + 0x04, 0xFFFFFFFF); // fieldA0 (null sentinel)
            WriteU32LeAt(buf, recBase + 0x08, 0xFFFFFFFF); // fieldA1 (null sentinel)
            WriteU32LeAt(buf, recBase + 0x0C, 3); // fieldKind
            WriteU32LeAt(buf, recBase + 0x10, 100); // fieldB0
            WriteU32LeAt(buf, recBase + 0x14, 101); // fieldB1
            WriteU32LeAt(buf, recBase + 0x18, 0xFFFFFFFF); // fieldLink (null sentinel)
        });

        MiPanelData panel = MobInfoPanelParser.Parse(data.AsSpan());

        Assert.Equal(1u, panel.RecordCount);
        MiWidgetRecord r = panel.Records[0];
        Assert.Equal(11u, r.WidgetId);
        Assert.Equal(0xFFFFFFFFu, r.FieldA0); // null sentinel — spec: Docs/RE/formats/mi.md §Enumerations
        Assert.Equal(0xFFFFFFFFu, r.FieldA1);
        Assert.Equal(3u, r.FieldKind);
        Assert.Equal(100u, r.FieldB0);
        Assert.Equal(101u, r.FieldB1);
        Assert.Equal(0xFFFFFFFFu, r.FieldLink);
    }

    [Fact]
    public void Mi_Parse_ZeroRecords_EmptyArray()
    {
        // Edge case: recordCount = 0 → 4-byte buffer, empty Records array.
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + 0 × 28 = 4 bytes".
        byte[] data = BuildMi(0);
        Assert.Equal(4, data.Length);
        MiPanelData panel = MobInfoPanelParser.Parse(data.AsSpan());
        Assert.Equal(0u, panel.RecordCount);
        Assert.Empty(panel.Records);
    }

    [Fact]
    public void Mi_Parse_TooShort_ForHeader_ThrowsInvalidData()
    {
        // Buffer shorter than 4 bytes must fail before reading the header.
        // spec: Docs/RE/formats/mi.md §Header layout — "recordCount u32 @ 0x00": HIGH.
        byte[] tooShort = new byte[3];
        Assert.Throws<InvalidDataException>(() => MobInfoPanelParser.Parse(tooShort.AsSpan()));
    }

    [Fact]
    public void Mi_Parse_LengthMismatch_ThrowsInvalidData()
    {
        // Declare 3 records but supply only 2 records' worth of data + header.
        // spec: Docs/RE/formats/mi.md §Container structure — "4 + recordCount × 28 = file size": HIGH.
        int actualSize = 4 + 2 * MiPanelData.RecordStride; // only 2 records present
        var buf = new byte[actualSize];
        WriteU32Le(buf, 0, 3); // claims 3 records
        Assert.Throws<InvalidDataException>(() => MobInfoPanelParser.Parse(buf.AsSpan()));
    }

    [Fact]
    public void Mi_Parse_MultiRecord_ThirdRecord_Fields()
    {
        // Build 3 records; verify the third record decodes from the correct offset.
        // spec: Docs/RE/formats/mi.md §Record layout — "body starts at 0x04, stride 28 bytes": HIGH.
        byte[] data = BuildMi(3, (buf, recordIndex, recBase) =>
        {
            if (recordIndex == 2) // third record
            {
                WriteU32LeAt(buf, recBase + 0x00, 99u);
                WriteU32LeAt(buf, recBase + 0x04, 200u);
                WriteU32LeAt(buf, recBase + 0x08, 199u);
                WriteU32LeAt(buf, recBase + 0x0C, 7u);
                WriteU32LeAt(buf, recBase + 0x10, 300u);
                WriteU32LeAt(buf, recBase + 0x14, 301u);
                WriteU32LeAt(buf, recBase + 0x18, 5u);
            }
        });

        MiPanelData panel = MobInfoPanelParser.Parse(data.AsSpan());

        Assert.Equal(3u, panel.RecordCount);
        MiWidgetRecord third = panel.Records[2];
        Assert.Equal(99u, third.WidgetId);
        Assert.Equal(200u, third.FieldA0);
        Assert.Equal(199u, third.FieldA1);
        Assert.Equal(7u, third.FieldKind);
        Assert.Equal(300u, third.FieldB0);
        Assert.Equal(301u, third.FieldB1);
        Assert.Equal(5u, third.FieldLink);
    }
}