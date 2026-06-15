using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="MudBlobParser"/> and <see cref="MudBlob"/>.
/// All fixtures are synthetic in-memory byte buffers; no real VFS is required.
/// spec: Docs/RE/formats/terrain.md §6. Ambient-sound tile blob — .mud
///
/// IMPORTANT: Bytes 0 and 1 of each record (walk/run footstep hypothesis) are REFUTED as
/// consumed fields — they are NOT read by any analysed ambient-update path, and the
/// walk/run pairing is PLAUSIBLE, not confirmed. Do NOT assert semantic meaning for bytes 0/1.
/// spec: Docs/RE/formats/terrain.md §6.2 — "pad0 u8 @ +0: VERIFIED (always zero)".
/// spec: Docs/RE/formats/terrain.md §6.2 — "pad1 u8 @ +1: VERIFIED (always zero)".
/// </summary>
public sealed class MudBlobParserTests
{
    // Grid constants.
    // spec: terrain.md §6.1 — "64 columns × 64 rows × 8 bytes = 32768 bytes": CONFIRMED.
    private const int GridCols = MudBlob.GridCols;     // 64
    private const int GridRows = MudBlob.GridRows;     // 64
    private const int RecordStride = MudBlob.RecordStride; // 8
    private const int FixedSize = MudBlob.FixedSize;   // 32768

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static byte[] AllZeroBlob() => new byte[FixedSize];

    private static byte[] BlobWithTileAtIndex(int tileIndex, byte[] tileBytes)
    {
        var buf = new byte[FixedSize];
        tileBytes.CopyTo(buf, tileIndex * RecordStride);
        return buf;
    }

    // ── Fixed-size validation ─────────────────────────────────────────────────

    [Fact]
    public void FixedSize_Is32768()
    {
        // spec: terrain.md §6 — "Total file size: exactly 32 768 bytes (0x8000)": CONFIRMED.
        Assert.Equal(32768, FixedSize);
        Assert.Equal(0x8000, FixedSize);
    }

    [Fact]
    public void Parse_CorrectSize_Succeeds()
    {
        // A 32768-byte buffer must parse without throwing.
        byte[] buf = AllZeroBlob();
        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.NotNull(blob.Tiles);
        Assert.Equal(GridRows * GridCols, blob.Tiles.Length); // 4096 tiles
    }

    [Fact]
    public void Parse_WrongSize_Throws()
    {
        // Any size other than 32768 must throw InvalidDataException.
        // spec: terrain.md §6 — fixed file size validation.
        var shortBuf = new byte[32767];
        Assert.Throws<InvalidDataException>(() => MudBlobParser.Parse(shortBuf.AsSpan()));
    }

    [Fact]
    public void Parse_LargerBuffer_Throws()
    {
        // A buffer one byte too large must also throw.
        var longBuf = new byte[32769];
        Assert.Throws<InvalidDataException>(() => MudBlobParser.Parse(longBuf.AsSpan()));
    }

    // ── Record layout tests ───────────────────────────────────────────────────

    [Fact]
    public void Parse_AllZero_TilesAreAllZero()
    {
        // An all-zero blob must yield 4096 tiles all with zero fields.
        byte[] buf = AllZeroBlob();
        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        // All CONFIRMED fields must be zero.
        // spec: terrain.md §6.2 — music_group, ambient_idx_0/1, effect_idx_0/1/2 all zero.
        Assert.All(blob.Tiles, t =>
        {
            Assert.Equal(0, t.MusicGroup);
            Assert.Equal(0, t.AmbientIdx0);
            Assert.Equal(0, t.AmbientIdx1);
            Assert.Equal(0, t.EffectIdx0);
            Assert.Equal(0, t.EffectIdx1);
            Assert.Equal(0, t.EffectIdx2);
        });
    }

    [Fact]
    public void Parse_MusicGroup_AtOffset2_RoundTrip()
    {
        // music_group (byte @ +2) must round-trip through the parser.
        // spec: terrain.md §6.2 — "music_group u8 @ +2: VERIFIED".
        byte[] tile = { 0, 0, 7, 0, 0, 0, 0, 0 }; // music_group = 7
        byte[] buf = BlobWithTileAtIndex(0, tile);

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(7, blob.Tiles[0].MusicGroup);
    }

    [Fact]
    public void Parse_AmbientIdx0_AtOffset3_RoundTrip()
    {
        // ambient_idx_0 (byte @ +3) must round-trip.
        // spec: terrain.md §6.2 — "ambient_idx_0 u8 @ +3: VERIFIED".
        byte[] tile = { 0, 0, 0, 12, 0, 0, 0, 0 };
        byte[] buf = BlobWithTileAtIndex(5, tile); // tile at index 5

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(12, blob.Tiles[5].AmbientIdx0);
    }

    [Fact]
    public void Parse_AmbientIdx1_AtOffset4_RoundTrip()
    {
        // ambient_idx_1 (byte @ +4) must round-trip.
        // spec: terrain.md §6.2 — "ambient_idx_1 u8 @ +4: VERIFIED".
        byte[] tile = { 0, 0, 0, 0, 3, 0, 0, 0 };
        byte[] buf = BlobWithTileAtIndex(10, tile);

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(3, blob.Tiles[10].AmbientIdx1);
    }

    [Fact]
    public void Parse_EffectIdx0_AtOffset5_RoundTrip()
    {
        // effect_idx_0 (byte @ +5) must round-trip.
        // spec: terrain.md §6.2 — "effect_idx_0 u8 @ +5: VERIFIED".
        byte[] tile = { 0, 0, 0, 0, 0, 9, 0, 0 };
        byte[] buf = BlobWithTileAtIndex(100, tile);

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(9, blob.Tiles[100].EffectIdx0);
    }

    [Fact]
    public void Parse_EffectIdx1_AtOffset6_RoundTrip()
    {
        // effect_idx_1 (byte @ +6) must round-trip.
        // spec: terrain.md §6.2 — "effect_idx_1 u8 @ +6: VERIFIED".
        byte[] tile = { 0, 0, 0, 0, 0, 0, 14, 0 };
        byte[] buf = BlobWithTileAtIndex(63, tile); // last tile in first row

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(14, blob.Tiles[63].EffectIdx1);
    }

    [Fact]
    public void Parse_EffectIdx2_AtOffset7_RoundTrip()
    {
        // effect_idx_2 (byte @ +7) must round-trip. All observed samples show 0.
        // spec: terrain.md §6.2 — "effect_idx_2 u8 @ +7: VERIFIED (limited, always zero in known samples)".
        byte[] tile = { 0, 0, 0, 0, 0, 0, 0, 2 };
        byte[] buf = BlobWithTileAtIndex(4095, tile); // last tile

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(2, blob.Tiles[4095].EffectIdx2);
    }

    // ── Pad bytes — do NOT assert walk/run semantics ──────────────────────────

    [Fact]
    public void Parse_PadBytes_Offset0And1_ArePresent_NotSemantic()
    {
        // Bytes 0 and 1 are ALWAYS zero in observed samples and are labelled pad0/pad1.
        // Do NOT assert walk/run footstep semantics — the walk/run hypothesis is REFUTED
        // as consumed: bytes 0/1 are inert, not read by the ambient-update path.
        // spec: terrain.md §6.2 — "pad0 u8 @ +0: VERIFIED (always zero)".
        // spec: terrain.md §6.2 — "pad1 u8 @ +1: VERIFIED (always zero)".
        byte[] tile = { 0, 0, 5, 2, 1, 3, 4, 0 };
        byte[] buf = BlobWithTileAtIndex(0, tile);

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        // Only assert CONFIRMED sound fields; skip pad0/pad1 semantic assertion.
        Assert.Equal(5, blob.Tiles[0].MusicGroup);
        Assert.Equal(2, blob.Tiles[0].AmbientIdx0);
        Assert.Equal(1, blob.Tiles[0].AmbientIdx1);
        Assert.Equal(3, blob.Tiles[0].EffectIdx0);
        Assert.Equal(4, blob.Tiles[0].EffectIdx1);
        Assert.Equal(0, blob.Tiles[0].EffectIdx2);
    }

    // ── Grid dimension / tile-count tests ────────────────────────────────────

    [Fact]
    public void Parse_TileCount_Is4096()
    {
        // spec: terrain.md §6.1 — "64 × 64 = 4096 tiles": CONFIRMED.
        byte[] buf = AllZeroBlob();
        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(4096, blob.Tiles.Length);
        Assert.Equal(GridCols * GridRows, blob.Tiles.Length);
    }

    [Fact]
    public void Parse_LastTile_IndexIs4095()
    {
        // The last tile in the grid (row=63, col=63) is at index 4095 = 63*64 + 63.
        // spec: terrain.md §6.1 — row-major index = row × 64 + col: CONFIRMED.
        byte[] tile = { 0, 0, 0xFF, 0, 0, 0, 0, 0 };
        byte[] buf = BlobWithTileAtIndex(4095, tile);

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(0xFF, blob.Tiles[4095].MusicGroup);
    }

    // ── Row-major ordering test ───────────────────────────────────────────────

    [Fact]
    public void Parse_RowMajorOrder_SecondRow_IndexIs64()
    {
        // The first tile of the second row (row=1, col=0) must be at index 64.
        // spec: terrain.md §6.1 — "Row-major (Z=row, X=col); tileIndex = col + (row × 64)": CONFIRMED.
        byte[] tile = { 0, 0, 9, 0, 0, 0, 0, 0 }; // music_group=9
        byte[] buf = BlobWithTileAtIndex(64, tile); // row=1, col=0

        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        Assert.Equal(9, blob.Tiles[64].MusicGroup);
        Assert.Equal(0, blob.Tiles[63].MusicGroup); // adjacent tile in row 0 must be 0
    }

    // ── ReadOnlyMemory overload ───────────────────────────────────────────────

    [Fact]
    public void Parse_ReadOnlyMemory_Overload_Succeeds()
    {
        byte[] buf = AllZeroBlob();
        MudBlob blob = MudBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(4096, blob.Tiles.Length);
    }

    // ── Sentinel-zero test ────────────────────────────────────────────────────

    [Fact]
    public void Parse_AllFieldsZero_MeansNoSound()
    {
        // Sentinel value 0 in music_group/ambient/effect fields means "no sound".
        // spec: terrain.md §6.2 — "0=no music", "0=no sound".
        byte[] buf = AllZeroBlob();
        MudBlob blob = MudBlobParser.Parse(buf.AsSpan());

        // Every tile must be all-zero for the CONFIRMED sound fields.
        foreach (var tile in blob.Tiles)
        {
            Assert.Equal(0, tile.MusicGroup);
            Assert.Equal(0, tile.AmbientIdx0);
            Assert.Equal(0, tile.AmbientIdx1);
        }
    }
}
