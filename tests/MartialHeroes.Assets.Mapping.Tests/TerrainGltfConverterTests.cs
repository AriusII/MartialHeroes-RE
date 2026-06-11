using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

/// <summary>
/// Headless structural tests for <see cref="TerrainGltfConverter"/>.
/// No GPU, no disk, no Godot dependency.
///
/// Validates:
///   - GLB header / chunk structure.
///   - Accessor counts: 65×65 = 4225 vertices, 64×64×2×3 = 24576 indices.
///   - Index count equals 64×64×2 triangles × 3.
///   - COLOR_0 accessor presence with normalised UNSIGNED_BYTE VEC4.
///   - TEXCOORD_0 accessor presence.
///   - POSITION accessor min/max with coordinate-system flip.
///   spec: Docs/RE/formats/terrain.md §5.1 Grid geometry.
/// </summary>
public sealed class TerrainGltfConverterTests
{
    // -------------------------------------------------------------------------
    // Synthetic TerrainCell fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a synthetic 65×65 <see cref="TerrainCell"/> with known values:
    ///   - Heights: vertex index as float (0.0 … 4224.0).
    ///   - Normals: all zeros (encoding unverified anyway).
    ///   - LookupTable / DirectionMap: all zeros.
    ///   - DiffuseColours: RGBA packed as (R=r%256, G=0, B=0, A=255) for vertex index r.
    ///
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 1 — Heightmap f32le 65×65: CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 5 — Diffuse colour u8×4 65×65: CONFIRMED.
    /// </summary>
    private static TerrainCell MakeSyntheticCell()
    {
        const int n = TerrainCell.VertexCount; // 4225

        float[] heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = i;

        // Normals: decoded (float Nx, float Ny, float Nz) tuples per vertex.
        // spec: Docs/RE/formats/terrain.md §5.5 Block 2 — "i8/127.0f decode": CONFIRMED.
        var normals = new (float Nx, float Ny, float Nz)[n];
        // All zero (identity normal) for this synthetic fixture.

        byte[] lookup = new byte[256];    // TextureIndexGrid
        byte[] direction = new byte[256]; // DirectionFlags

        // DiffuseColours: decoded (float R, float G, float B, float A) tuples.
        // spec: Docs/RE/formats/terrain.md §5.8 Block 5 — "×0.5 decode": CONFIRMED.
        // Fixture uses R=i%256 / 255f, G=0, B=0, A=1.0f to produce unique vertex colours.
        var diffuse = new (float R, float G, float B, float A)[n];
        for (int i = 0; i < n; i++)
        {
            diffuse[i] = ((i % 256) / 255f, 0f, 0f, 1.0f);
        }

        return new TerrainCell
        {
            Heights = heights,
            Normals = normals,
            TextureIndexGrid = lookup,
            DirectionFlags = direction,
            DiffuseColours = diffuse,
        };
    }

    // -------------------------------------------------------------------------
    // GLB container structure
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_Header_HasCorrectMagicAndVersion()
    {
        // glTF 2.0 spec §binary-gltf §Header: magic = 0x46546C67 ("glTF"), version = 2.
        using var ms = new MemoryStream();
        TerrainGltfConverter.WriteGlb(MakeSyntheticCell(), ms);
        byte[] glb = ms.ToArray();

        Assert.True(glb.Length >= 12);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8));

        Assert.Equal(0x46546C67u, magic);
        Assert.Equal(2u, version);
        Assert.Equal((uint)glb.Length, length);
    }

    [Fact]
    public void WriteGlb_AllChunks_ArePaddedTo4Bytes()
    {
        // glTF 2.0 spec §binary-gltf §Padding.
        using var ms = new MemoryStream();
        TerrainGltfConverter.WriteGlb(MakeSyntheticCell(), ms);
        byte[] glb = ms.ToArray();

        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0u, jsonLen % 4u);

        int binHdrOff = 12 + 8 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOff));
        Assert.Equal(0u, binLen % 4u);
    }

    // -------------------------------------------------------------------------
    // Vertex and index count correctness
    // spec: Docs/RE/formats/terrain.md §5.1 — "65×65 vertices, 64×64 quads": CONFIRMED.
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_PositionAccessor_HasCorrect4225VertexCount()
    {
        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        Assert.Equal(TerrainCell.VertexCount, posAccessor.GetProperty("count").GetInt32()); // 4225
        Assert.Equal("VEC3", posAccessor.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, posAccessor.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_IndexAccessor_HasCorrect24576IndexCount()
    {
        // 64×64 quads × 2 triangles × 3 vertices = 24576.
        // spec: Docs/RE/formats/terrain.md §5.1 — "64×64 quads per cell": CONFIRMED.
        const int expectedIndexCount = 64 * 64 * 2 * 3; // 24576

        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        // Index accessor is index 3 (POSITION=0, TEXCOORD_0=1, COLOR_0=2, indices=3).
        var idxAccessor = doc.RootElement.GetProperty("accessors")[3];
        Assert.Equal(expectedIndexCount, idxAccessor.GetProperty("count").GetInt32());
        Assert.Equal("SCALAR", idxAccessor.GetProperty("type").GetString());
    }

    // -------------------------------------------------------------------------
    // Accessor types
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_TexCoordAccessor_HasCorrectTypeAndCount()
    {
        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        var uvAccessor = doc.RootElement.GetProperty("accessors")[1];
        Assert.Equal(TerrainCell.VertexCount, uvAccessor.GetProperty("count").GetInt32());
        Assert.Equal("VEC2", uvAccessor.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, uvAccessor.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_Color0Accessor_IsNormalisedUnsignedByteVec4()
    {
        // COLOR_0 must be VEC4 UNSIGNED_BYTE normalised=true.
        // glTF 2.0 spec §Accessor — COLOR_0 with UNSIGNED_BYTE must have normalized=true.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — Diffuse colour u8×4 (R,G,B,A): CONFIRMED.
        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        var colAccessor = doc.RootElement.GetProperty("accessors")[2];
        Assert.Equal(TerrainCell.VertexCount, colAccessor.GetProperty("count").GetInt32());
        Assert.Equal("VEC4", colAccessor.GetProperty("type").GetString());
        Assert.Equal(5121 /*UNSIGNED_BYTE*/, colAccessor.GetProperty("componentType").GetInt32());
        Assert.True(colAccessor.GetProperty("normalized").GetBoolean());
    }

    [Fact]
    public void WriteGlb_Json_HasFourAccessors()
    {
        // Expected: POSITION(0), TEXCOORD_0(1), COLOR_0(2), indices(3).
        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(4, doc.RootElement.GetProperty("accessors").GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Position accessor min/max with handedness flip
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_PositionAccessor_HasMinMax()
    {
        // glTF 2.0 spec §Accessor: min/max required for POSITION.
        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        Assert.True(posAccessor.TryGetProperty("min", out _));
        Assert.True(posAccessor.TryGetProperty("max", out _));
    }

    [Fact]
    public void WriteGlb_PositionAccessor_MinMaxX_ReflectsHandednessFlip()
    {
        // With X-flip, col=0 → X=0 (max), col=64 → X = -(64×16)= -1024 (min).
        // spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0: CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x negated: CONFIRMED.
        const float expectedMinX = -(64 * 16.0f); // -1024.0
        const float expectedMaxX = 0f;

        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        float minX = posAccessor.GetProperty("min")[0].GetSingle();
        float maxX = posAccessor.GetProperty("max")[0].GetSingle();

        Assert.Equal(expectedMinX, minX, precision: 3);
        Assert.Equal(expectedMaxX, maxX, precision: 3);
    }

    // -------------------------------------------------------------------------
    // Binary buffer content: first vertex position
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_BinaryBuffer_FirstVertexIsAtOrigin()
    {
        // Vertex (row=0, col=0): worldX = -(0×16) = 0, worldY = height[0] = 0, worldZ = 0×16 = 0.
        // spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0: CONFIRMED.
        using var ms = new MemoryStream();
        TerrainGltfConverter.WriteGlb(MakeSyntheticCell(), ms);
        byte[] glb = ms.ToArray();

        byte[] binData = ExtractBinChunk(glb);
        // Position bufferView is bufferView 0; byteOffset = 0.
        float v0x = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(0));
        float v0y = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(4));
        float v0z = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(8));

        Assert.Equal(0f, v0x, precision: 5); // col=0 → X = -(0×16) = 0
        Assert.Equal(0f, v0y, precision: 5); // height[0] = 0
        Assert.Equal(0f, v0z, precision: 5); // row=0 → Z = 0×16 = 0
    }

    [Fact]
    public void WriteGlb_BinaryBuffer_SecondColumnVertexHasNegatedX()
    {
        // Vertex (row=0, col=1): worldX = -(1×16) = -16.
        // spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0: CONFIRMED.
        // Handedness flip: X is negated. spec: Docs/RE/formats/mesh.md §Vertex list: CONFIRMED.
        using var ms = new MemoryStream();
        TerrainGltfConverter.WriteGlb(MakeSyntheticCell(), ms);
        byte[] glb = ms.ToArray();

        byte[] binData = ExtractBinChunk(glb);
        // Vertex index 1 (col=1, row=0) starts at offset 12 (12 bytes per VEC3 f32).
        float v1x = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(12));
        Assert.Equal(-16f, v1x, precision: 5);
    }

    // -------------------------------------------------------------------------
    // Mesh attribute structure in JSON
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_MeshPrimitive_HasPositionTexcoordColor0AndIndices()
    {
        string json = ExtractJson(MakeSyntheticCell());
        using var doc = JsonDocument.Parse(json);

        var prim = doc.RootElement.GetProperty("meshes")[0]
                                  .GetProperty("primitives")[0];
        var attrs = prim.GetProperty("attributes");

        Assert.True(attrs.TryGetProperty("POSITION", out _));
        Assert.True(attrs.TryGetProperty("TEXCOORD_0", out _));
        Assert.True(attrs.TryGetProperty("COLOR_0", out _));
        Assert.True(prim.TryGetProperty("indices", out _));
    }

    [Fact]
    public void WriteGlb_Output_IsDeterministic()
    {
        TerrainCell cell = MakeSyntheticCell();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        TerrainGltfConverter.WriteGlb(cell, ms1);
        TerrainGltfConverter.WriteGlb(cell, ms2);
        Assert.Equal(ms1.ToArray(), ms2.ToArray());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractJson(TerrainCell cell)
    {
        using var ms = new MemoryStream();
        TerrainGltfConverter.WriteGlb(cell, ms);
        return ExtractJson(ms.ToArray());
    }

    private static string ExtractJson(byte[] glb)
    {
        uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return Encoding.UTF8.GetString(glb, 20, (int)jsonLength).TrimEnd(' ');
    }

    private static byte[] ExtractBinChunk(byte[] glb)
    {
        uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int binHdrOffset = 12 + 8 + (int)jsonLength;
        uint binLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOffset));
        int binDataOffset = binHdrOffset + 8;
        return glb[binDataOffset..(binDataOffset + (int)binLength)];
    }
}
