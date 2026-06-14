using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="TerrainLayerParsers.ParseFx4"/>.
/// All fixtures are built in-memory from scratch — no real game files required.
///
/// Format under test: <c>.fx4</c> terrain overlay layer file.
///
/// Layout (CONFIRMED-FROM-LOADER):
///   u32 tile_count @ file offset 0x00.
///   Per tile: 48-byte TileHeader + vertex_count × 44-byte VF_44 vertices + index_count × 2-byte u16 indices.
///   vertex_count @ tile-relative +0x28: CONFIRMED.
///   index_count  @ tile-relative +0x2C: CONFIRMED.
///   tile_metadata (leading 40 bytes of TileHeader): UNVERIFIED semantics, preserved faithfully.
///   Vertex format: VF_44 (same as FX2, not FX1/FX3/FX5).
///   File-size formula: 4 + Σ tiles (48 + vertex_count × 44 + index_count × 2).
///
/// spec: Docs/RE/formats/terrain_layers.md §1.11 FX4 Format: CONFIRMED-FROM-LOADER.
/// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 (44 B): CONFIRMED.
/// </summary>
public sealed class Fx4ParserTests
{
    // ── Binary fixture helpers ──────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteF32LE(byte[] buf, int off, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(off, 4), v);

    private static byte[] Le4(uint v)
    {
        var b = new byte[4];
        WriteU32LE(b, 0, v);
        return b;
    }

    /// <summary>
    /// Builds a 48-byte tile header with vertex_count at +0x28 and index_count at +0x2C.
    /// Leading 40 bytes (tile_metadata) are left as zero — semantics UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — TileHeader (48 bytes): CONFIRMED.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — vertex_count @ +0x28, index_count @ +0x2C: CONFIRMED.
    /// </summary>
    private static byte[] BuildTileHeader(uint vertexCount, uint indexCount)
    {
        byte[] hdr = new byte[48]; // all zeros (tile_metadata UNVERIFIED)
        WriteU32LE(hdr, 0x28, vertexCount); // spec: §1.11 vertex_count @ +0x28: CONFIRMED.
        WriteU32LE(hdr, 0x2C, indexCount); // spec: §1.11 index_count @ +0x2C: CONFIRMED.
        return hdr;
    }

    /// <summary>
    /// Builds a single VF_44 vertex (44 bytes).
    /// Layout: XYZ f32×3 + NX/NY/NZ f32×3 + RGBA u8×4 + U0/V0 f32×2 + U1/V1 f32×2 = 44 bytes.
    /// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 (44 B): CONFIRMED.
    /// </summary>
    private static byte[] BuildVf44Vertex(float x, float y, float z,
        float nx = 0f, float ny = 1f, float nz = 0f,
        byte r = 0, byte g = 0, byte b = 0, byte a = 0xFF,
        float u0 = 0f, float v0 = 0f, float u1 = 0f, float v1 = 0f)
    {
        byte[] v = new byte[44];
        // Position XYZ f32×3 @ +0/4/8: CONFIRMED (leading position float3 parser-verified via AABB).
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — VF_44 leading position float3 parser-verified.
        WriteF32LE(v, 0, x);
        WriteF32LE(v, 4, y);
        WriteF32LE(v, 8, z);
        // Normal XYZ f32×3 @ +12/16/20: CONFIRMED (VF_44 layout from §1.2).
        WriteF32LE(v, 12, nx);
        WriteF32LE(v, 16, ny);
        WriteF32LE(v, 20, nz);
        // RGBA u8×4 @ +24/25/26/27: CONFIRMED (VF_44 layout from §1.2).
        v[24] = r;
        v[25] = g;
        v[26] = b;
        v[27] = a;
        // UV0 f32×2 @ +28/32: CONFIRMED (VF_44 layout from §1.2).
        WriteF32LE(v, 28, u0);
        WriteF32LE(v, 32, v0);
        // UV1 f32×2 @ +36/40: CONFIRMED (VF_44 layout from §1.2, extra 8 bytes vs VF_36).
        WriteF32LE(v, 36, u1);
        WriteF32LE(v, 40, v1);
        return v;
    }

    /// <summary>
    /// Builds u16 LE index bytes.
    /// spec: Docs/RE/formats/terrain_layers.md §1.3 — u16 triangle list: CONFIRMED.
    /// </summary>
    private static byte[] BuildU16Indices(params ushort[] indices)
    {
        byte[] buf = new byte[indices.Length * 2];
        for (int i = 0; i < indices.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(i * 2, 2), indices[i]);
        return buf;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        byte[] result = new byte[total];
        int off = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, off);
            off += p.Length;
        }

        return result;
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFx4_ZeroTiles_ReturnsEmptyLayer()
    {
        // tile_count = 0 → zero tiles decoded.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_count u32 @ 0x00: CONFIRMED.
        byte[] buf = Le4(0u); // tile_count = 0

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0u, layer.TileCount);
        Assert.Empty(layer.Tiles);
    }

    [Fact]
    public void ParseFx4_SingleTile_TileCountDecoded()
    {
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_count u32 @ 0x00: CONFIRMED.
        byte[] buf = Concat(
            Le4(1u), // tile_count = 1
            BuildTileHeader(vertexCount: 3, indexCount: 3),
            BuildVf44Vertex(1f, 2f, 3f),
            BuildVf44Vertex(4f, 5f, 6f),
            BuildVf44Vertex(7f, 8f, 9f),
            BuildU16Indices(0, 1, 2)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(1u, layer.TileCount);
        Assert.Single(layer.Tiles);
    }

    [Fact]
    public void ParseFx4_SingleTile_VertexAndIndexCountFromHeader()
    {
        // vertex_count @ tile-relative +0x28 and index_count @ +0x2C drive the reads.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — vertex_count @ +0x28: CONFIRMED.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — index_count @ +0x2C: CONFIRMED.
        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vertexCount: 3, indexCount: 3),
            BuildVf44Vertex(10f, 0f, 0f),
            BuildVf44Vertex(0f, 10f, 0f),
            BuildVf44Vertex(0f, 0f, 10f),
            BuildU16Indices(0, 1, 2)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));
        Fx4Tile tile = layer.Tiles[0];

        Assert.Equal(3u, tile.VertexCount);
        Assert.Equal(3u, tile.IndexCount);
        Assert.Equal(3, tile.Vertices.Length);
        Assert.Equal(3, tile.Indices.Length);
    }

    [Fact]
    public void ParseFx4_SingleTile_VertexPositionDecoded()
    {
        // Leading position float3 (X, Y, Z) of VF_44 is parser-verified via AABB compute.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — leading position float3 parser-verified.
        // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 — pos_x/y/z @ +0/4/8: CONFIRMED.
        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vertexCount: 1, indexCount: 3),
            BuildVf44Vertex(x: 1024.5f, y: 75.25f, z: -512.125f),
            BuildU16Indices(0, 0, 0)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));
        FxVertex44 v = layer.Tiles[0].Vertices[0];

        Assert.Equal(1024.5f, v.X, precision: 4);
        Assert.Equal(75.25f, v.Y, precision: 4);
        Assert.Equal(-512.125f, v.Z, precision: 4);
    }

    [Fact]
    public void ParseFx4_SingleTile_Vf44UVChannels()
    {
        // VF_44 has dual UV channels (U1/V1) — the extra 8 bytes vs VF_36.
        // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 — U1 @ +36, V1 @ +40: CONFIRMED.
        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vertexCount: 1, indexCount: 3),
            BuildVf44Vertex(0f, 0f, 0f, u0: 0.25f, v0: 0.5f, u1: 0.75f, v1: 0.875f),
            BuildU16Indices(0, 0, 0)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));
        FxVertex44 v = layer.Tiles[0].Vertices[0];

        Assert.Equal(0.25f, v.U0, precision: 5);
        Assert.Equal(0.5f, v.V0, precision: 5);
        Assert.Equal(0.75f, v.U1, precision: 5);
        Assert.Equal(0.875f, v.V1, precision: 5);
    }

    [Fact]
    public void ParseFx4_SingleTile_IndicesDecoded()
    {
        // u16 index buffer, plain triangle list.
        // spec: Docs/RE/formats/terrain_layers.md §1.3 — u16 triangle list: CONFIRMED.
        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vertexCount: 4, indexCount: 6),
            BuildVf44Vertex(0f, 0f, 0f),
            BuildVf44Vertex(1f, 0f, 0f),
            BuildVf44Vertex(1f, 1f, 0f),
            BuildVf44Vertex(0f, 1f, 0f),
            BuildU16Indices(0, 1, 2, 0, 2, 3)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));
        ushort[] idx = layer.Tiles[0].Indices;

        Assert.Equal(6, idx.Length);
        Assert.Equal((ushort)0, idx[0]);
        Assert.Equal((ushort)1, idx[1]);
        Assert.Equal((ushort)2, idx[2]);
        Assert.Equal((ushort)0, idx[3]);
        Assert.Equal((ushort)2, idx[4]);
        Assert.Equal((ushort)3, idx[5]);
    }

    [Fact]
    public void ParseFx4_SingleTile_RawTileHeaderIs48Bytes()
    {
        // The raw tile header (48 bytes) is preserved faithfully.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — TileHeader (48 bytes): CONFIRMED.
        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vertexCount: 1, indexCount: 3),
            BuildVf44Vertex(0f, 0f, 0f),
            BuildU16Indices(0, 0, 0)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(48, layer.Tiles[0].RawTileHeader.Length);
    }

    [Fact]
    public void ParseFx4_TwoTiles_BothDecoded()
    {
        // Multiple tiles decoded sequentially.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — "per tile (× tileCount)": CONFIRMED.
        byte[] buf = Concat(
            Le4(2u),
            // Tile 0: 3 vertices, 3 indices.
            BuildTileHeader(vertexCount: 3, indexCount: 3),
            BuildVf44Vertex(1f, 0f, 0f),
            BuildVf44Vertex(2f, 0f, 0f),
            BuildVf44Vertex(3f, 0f, 0f),
            BuildU16Indices(0, 1, 2),
            // Tile 1: 1 vertex, 3 indices (degenerate but valid for parse test).
            BuildTileHeader(vertexCount: 1, indexCount: 3),
            BuildVf44Vertex(99f, 88f, 77f),
            BuildU16Indices(0, 0, 0)
        );

        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2u, layer.TileCount);
        Assert.Equal(2, layer.Tiles.Length);
        Assert.Equal(3u, layer.Tiles[0].VertexCount);
        Assert.Equal(1u, layer.Tiles[1].VertexCount);
        Assert.Equal(99f, layer.Tiles[1].Vertices[0].X, precision: 4);
    }

    [Fact]
    public void ParseFx4_FileSizeFormula_MatchesSingleTile()
    {
        // File-size formula: 4 + 48 + vertex_count × 44 + index_count × 2.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — "4 + Σ tiles (48 + vcount×44 + icount×2)": CONFIRMED.
        const uint vCount = 3;
        const uint iCount = 3;
        int expectedSize = 4 + 48 + (int)vCount * 44 + (int)iCount * 2;

        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vCount, iCount),
            BuildVf44Vertex(0f, 0f, 0f), BuildVf44Vertex(1f, 0f, 0f), BuildVf44Vertex(0f, 1f, 0f),
            BuildU16Indices(0, 1, 2)
        );

        Assert.Equal(expectedSize, buf.Length);

        // Parser should succeed on this exact-size buffer.
        Fx4Layer layer = TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf));
        Assert.Single(layer.Tiles);
    }

    [Fact]
    public void ParseFx4_TooShortForTileCount_ThrowsInvalidDataException()
    {
        // Buffer shorter than 4 bytes cannot hold tile_count.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_count u32 @ 0x00.
        byte[] tooShort = new byte[3];
        Assert.Throws<InvalidDataException>(() =>
            TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(tooShort)));
    }

    [Fact]
    public void ParseFx4_TileHeaderTruncated_ThrowsInvalidDataException()
    {
        // Buffer claims tile_count=1 but has no room for the tile header.
        // spec: parser mandate — "thrown on truncation".
        byte[] buf = Le4(1u); // tile_count=1, but no tile data follows
        Assert.Throws<InvalidDataException>(() =>
            TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseFx4_GeometryTruncated_ThrowsInvalidDataException()
    {
        // Tile header claims 5 vertices but only 2 are provided.
        byte[] buf = Concat(
            Le4(1u),
            BuildTileHeader(vertexCount: 5, indexCount: 0), // claims 5 verts
            BuildVf44Vertex(0f, 0f, 0f), // only 2 verts
            BuildVf44Vertex(1f, 0f, 0f)
        );
        Assert.Throws<InvalidDataException>(() =>
            TerrainLayerParsers.ParseFx4(new ReadOnlyMemory<byte>(buf)));
    }
}