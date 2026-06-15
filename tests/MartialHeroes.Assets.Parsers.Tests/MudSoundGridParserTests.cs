using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="MudSoundGridParser"/> and <see cref="MudSoundGrid"/>.
/// All fixtures are synthetic in-memory byte buffers; no real VFS is required.
/// spec: Docs/RE/formats/mud.md
///
/// IMPORTANT: effId2 (byte @ +7) is CONSUMED but typically zero in all known samples.
/// Do NOT assert it is always zero — only assert the confirmed consume behaviour.
/// Bytes 0 and 1 (WlkSoundIndex/RunSoundIndex) are PLAUSIBLE footstep indices —
/// the walk/run hypothesis is PLAUSIBLE but UNVERIFIED; do NOT assert semantic meaning.
/// spec: Docs/RE/formats/mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE.
/// spec: Docs/RE/formats/mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE.
/// </summary>
public sealed class MudSoundGridParserTests
{
    // Grid constants.
    // spec: mud.md §Identification — "File size: fixed 32768 bytes (0x8000)": CONFIRMED.
    private const int FixedFileSize = MudSoundGrid.FixedFileSize; // 32768
    private const int TileCount = MudSoundGrid.TileCount;         // 4096
    private const int Cols = MudSoundGrid.Cols;                    // 64
    private const int Rows = MudSoundGrid.Rows;                    // 64
    private const int TileStride = 8; // struct MudSoundTile is 8 bytes

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static byte[] AllZeroBlob() => new byte[FixedFileSize];

    private static byte[] BlobWithTileAt(int col, int row, byte[] tileBytes)
    {
        // Index formula: tileIndex = col + (row << 6).
        // spec: mud.md §Indexing (world → tile) — "tile_index = col + (row << 6)": CONFIRMED.
        int idx = col + (row << 6);
        var buf = new byte[FixedFileSize];
        tileBytes.CopyTo(buf, idx * TileStride);
        return buf;
    }

    // ── Fixed-size constants ──────────────────────────────────────────────────

    [Fact]
    public void Constants_MatchSpec()
    {
        // spec: mud.md §Identification — "Fixed size: 32768 bytes (0x8000)": CONFIRMED.
        Assert.Equal(32768, FixedFileSize);
        Assert.Equal(0x8000, FixedFileSize);

        // spec: mud.md §Grid geometry — "64 × 64 tiles": CONFIRMED.
        Assert.Equal(64, Cols);
        Assert.Equal(64, Rows);
        Assert.Equal(4096, TileCount);

        // spec: mud.md §Grid geometry — "Tile world size: 16 world units": CONFIRMED.
        Assert.Equal(16, MudSoundGrid.TileWorldSize);
    }

    // ── Fixed-size validation ─────────────────────────────────────────────────

    [Fact]
    public void Parse_CorrectSize_Succeeds()
    {
        // A 32768-byte blob must parse without throwing.
        byte[] buf = AllZeroBlob();
        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        Assert.NotNull(grid.Tiles);
        Assert.Equal(TileCount, grid.Tiles.Length);
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        // A buffer shorter than 32768 bytes must throw InvalidDataException.
        // spec: mud.md §Identification — "loader allocates/reads exactly 0x8000 bytes": CONFIRMED.
        var shortBuf = new byte[32767];
        Assert.Throws<InvalidDataException>(() => MudSoundGridParser.Parse(shortBuf.AsSpan()));
    }

    [Fact]
    public void Parse_TooLong_Throws()
    {
        var longBuf = new byte[32769];
        Assert.Throws<InvalidDataException>(() => MudSoundGridParser.Parse(longBuf.AsSpan()));
    }

    // ── Tile layout — CONFIRMED fields ───────────────────────────────────────

    [Fact]
    public void Parse_BgmZoneId_AtOffset2_RoundTrip()
    {
        // bgmZoneId (byte @ +2) must round-trip through the parser.
        // spec: mud.md §Tile layout offset 2 — bgmZoneId u8: CONFIRMED.
        byte[] tile = { 0, 0, 5, 0, 0, 0, 0, 0 };
        byte[] buf = BlobWithTileAt(col: 0, row: 0, tile);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        Assert.Equal(5, grid.Tiles[0].BgmZoneId);
    }

    [Fact]
    public void Parse_BgeAmbientId0_AtOffset3_RoundTrip()
    {
        // bgeAmbientId0 (byte @ +3) must round-trip.
        // spec: mud.md §Tile layout offset 3 — bgeAmbientId0 u8: CONFIRMED.
        byte[] tile = { 0, 0, 0, 11, 0, 0, 0, 0 };
        byte[] buf = BlobWithTileAt(col: 10, row: 0, tile);
        int idx = 10 + (0 << 6); // col + (row << 6)

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        Assert.Equal(11, grid.Tiles[idx].BgeAmbientId0);
    }

    [Fact]
    public void Parse_BgeAmbientId1_AtOffset4_RoundTrip()
    {
        // bgeAmbientId1 (byte @ +4) must round-trip.
        // spec: mud.md §Tile layout offset 4 — bgeAmbientId1 u8: CONFIRMED.
        byte[] tile = { 0, 0, 0, 0, 3, 0, 0, 0 };
        byte[] buf = BlobWithTileAt(col: 0, row: 2, tile);
        int idx = 0 + (2 << 6);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        Assert.Equal(3, grid.Tiles[idx].BgeAmbientId1);
    }

    [Fact]
    public void Parse_EffId0_AtOffset5_RoundTrip()
    {
        // effId0 (byte @ +5) must round-trip.
        // spec: mud.md §Tile layout offset 5 — effId0 u8: CONFIRMED.
        byte[] tile = { 0, 0, 0, 0, 0, 7, 0, 0 };
        byte[] buf = BlobWithTileAt(col: 63, row: 0, tile);
        int idx = 63 + (0 << 6);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        Assert.Equal(7, grid.Tiles[idx].EffId0);
    }

    [Fact]
    public void Parse_EffId1_AtOffset6_RoundTrip()
    {
        // effId1 (byte @ +6) must round-trip.
        // spec: mud.md §Tile layout offset 6 — effId1 u8: CONFIRMED.
        byte[] tile = { 0, 0, 0, 0, 0, 0, 8, 0 };
        byte[] buf = BlobWithTileAt(col: 0, row: 63, tile);
        int idx = 0 + (63 << 6);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        Assert.Equal(8, grid.Tiles[idx].EffId1);
    }

    [Fact]
    public void Parse_EffId2_AtOffset7_IsConsumed()
    {
        // effId2 (byte @ +7) is CONSUMED. All known samples observe 0, but the spec says CONFIRMED.
        // We verify it round-trips (not that it is always 0).
        // spec: mud.md §Tile layout offset 7 — effId2 u8: CONFIRMED.
        byte[] tile = { 0, 0, 0, 0, 0, 0, 0, 4 };
        byte[] buf = BlobWithTileAt(col: 32, row: 32, tile);
        int idx = 32 + (32 << 6);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        // effId2 is decoded and accessible — consumed path is verified by round-trip.
        Assert.Equal(4, grid.Tiles[idx].EffId2);
    }

    // ── Plausible-only fields — do NOT assert semantics ───────────────────────

    [Fact]
    public void Parse_WlkAndRunBytes_AreDecodedButNotSemantic()
    {
        // Bytes 0 and 1 (WlkSoundIndex / RunSoundIndex) are decoded but their footstep pairing
        // is PLAUSIBLE, NOT confirmed as consumed. Do not assert semantic meaning.
        // spec: mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE.
        // spec: mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE.
        byte[] tile = { 5, 6, 1, 2, 3, 4, 0, 0 };
        byte[] buf = BlobWithTileAt(col: 1, row: 1, tile);
        int idx = 1 + (1 << 6);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());

        // We only assert that the parser doesn't crash on non-zero bytes at 0/1.
        // We DO assert the CONFIRMED fields in the same tile are correct.
        Assert.Equal(1, grid.Tiles[idx].BgmZoneId);
        Assert.Equal(2, grid.Tiles[idx].BgeAmbientId0);
        Assert.Equal(3, grid.Tiles[idx].BgeAmbientId1);
        Assert.Equal(4, grid.Tiles[idx].EffId0);
    }

    // ── GetTile helper tests ──────────────────────────────────────────────────

    [Fact]
    public void GetTile_LocalCoordinates_ReturnsCorrectTile()
    {
        // GetTile uses: col = (localX / 16) & 0x3F; row = (localZ / 16) & 0x3F.
        // spec: mud.md §Indexing (world → tile) — col/row formula: CONFIRMED.
        // For localX=16, localZ=32: col=1, row=2, idx = 1 + (2 << 6) = 129.
        int localX = 16, localZ = 32;
        int expectedCol = (localX / MudSoundGrid.TileWorldSize) & 0x3F; // 1
        int expectedRow = (localZ / MudSoundGrid.TileWorldSize) & 0x3F; // 2
        int expectedIdx = expectedCol + (expectedRow << 6); // 129

        byte[] tile = { 0, 0, 42, 0, 0, 0, 0, 0 }; // bgmZoneId=42
        byte[] buf = BlobWithTileAt(col: expectedCol, row: expectedRow, tile);

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());
        MudSoundTile t = grid.GetTile(localX, localZ);

        Assert.Equal(42, t.BgmZoneId);
        Assert.Equal(grid.Tiles[expectedIdx].BgmZoneId, t.BgmZoneId);
    }

    [Fact]
    public void GetTile_CoordinateMask_WrapsAt64()
    {
        // The & 0x3F mask wraps coordinates at 64 tiles (1024 world units).
        // spec: mud.md §Indexing (world → tile) — "col = (local_x / 16) & 0x3F": CONFIRMED.
        // localX = 1024 → col = (1024/16) & 0x3F = 64 & 63 = 0 → same column as localX=0.
        byte[] tileThenEmpty = new byte[FixedFileSize];
        // tile at col=0, row=0 has bgmZoneId=7
        tileThenEmpty[0 * 8 + 2] = 7; // tile index 0, byte 2 = bgmZoneId

        MudSoundGrid grid = MudSoundGridParser.Parse(tileThenEmpty.AsSpan());

        // localX=0 → col=0 → tile[0] → bgmZoneId=7
        Assert.Equal(7, grid.GetTile(0, 0).BgmZoneId);
        // localX=1024 → col=64&63=0 → same tile → bgmZoneId=7
        Assert.Equal(7, grid.GetTile(1024, 0).BgmZoneId);
    }

    // ── ResolveSoundIndices helper tests ──────────────────────────────────────

    [Fact]
    public void ResolveSoundIndices_Confirmed_FieldsMatch()
    {
        // ResolveSoundIndices must expose CONFIRMED fields correctly.
        // spec: mud.md §Resolution chain — mud tile byte → sound table → leaf audio.
        byte[] tile = { 0, 0, 3, 5, 6, 2, 1, 4 };
        byte[] buf = BlobWithTileAt(col: 5, row: 5, tile);
        int localX = 5 * MudSoundGrid.TileWorldSize; // 80
        int localZ = 5 * MudSoundGrid.TileWorldSize; // 80

        MudSoundGrid grid = MudSoundGridParser.Parse(buf.AsSpan());
        var indices = grid.ResolveSoundIndices(localX, localZ);

        // CONFIRMED fields only.
        // spec: mud.md §Tile layout offset 2 — bgmZoneId: CONFIRMED.
        Assert.Equal(3, indices.BgmIndex);
        // spec: mud.md §Tile layout offset 3/4 — bgeAmbientId0/1: CONFIRMED.
        Assert.Equal(5, indices.BgeIndices.Slot0);
        Assert.Equal(6, indices.BgeIndices.Slot1);
        // spec: mud.md §Tile layout offset 5/6/7 — effId0/1/2: CONFIRMED.
        Assert.Equal(2, indices.EffIndices.Slot0);
        Assert.Equal(1, indices.EffIndices.Slot1);
        Assert.Equal(4, indices.EffIndices.Slot2);
    }

    // ── ReadOnlyMemory overload ───────────────────────────────────────────────

    [Fact]
    public void Parse_ReadOnlyMemory_Overload_Succeeds()
    {
        byte[] buf = AllZeroBlob();
        MudSoundGrid grid = MudSoundGridParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(TileCount, grid.Tiles.Length);
    }
}
