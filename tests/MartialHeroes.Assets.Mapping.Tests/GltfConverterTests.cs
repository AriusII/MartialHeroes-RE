using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

/// <summary>
/// Headless structural tests for <see cref="GltfConverter"/>.
/// No GPU, no disk, no Godot dependency.
/// </summary>
public sealed class GltfConverterTests
{
    // -------------------------------------------------------------------------
    // Synthetic mesh fixtures
    // -------------------------------------------------------------------------

    /// <summary>Two-triangle quad (4 vertices, 6 indices).</summary>
    private static StaticMesh MakeQuad() => new StaticMesh
    {
        Positions = [
            new Vec3(0f, 0f, 0f),
            new Vec3(1f, 0f, 0f),
            new Vec3(1f, 1f, 0f),
            new Vec3(0f, 1f, 0f),
        ],
        Uvs = [
            new Vec2(0f, 0f),
            new Vec2(1f, 0f),
            new Vec2(1f, 1f),
            new Vec2(0f, 1f),
        ],
        Indices = [0, 1, 2, 0, 2, 3],
    };

    /// <summary>Single triangle (3 vertices, 3 indices).</summary>
    private static StaticMesh MakeSingleTriangle() => new StaticMesh
    {
        Positions = [
            new Vec3(0f, 0f, 0f),
            new Vec3(2f, 0f, 0f),
            new Vec3(1f, 2f, 0f),
        ],
        Uvs = [
            new Vec2(0f, 0f),
            new Vec2(1f, 0f),
            new Vec2(0.5f, 1f),
        ],
        Indices = [0, 1, 2],
    };

    // -------------------------------------------------------------------------
    // GLB container structure tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_Header_HasCorrectMagicAndVersion()
    {
        // glTF 2.0 spec §binary-gltf §Header: magic = 0x46546C67 ("glTF"), version = 2.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeQuad(), ms);
        byte[] glb = ms.ToArray();

        Assert.True(glb.Length >= 12, "GLB must be at least 12 bytes (header).");
        uint magic   = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4));
        uint length  = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8));

        Assert.Equal(0x46546C67u, magic);   // "glTF"
        Assert.Equal(2u, version);
        Assert.Equal((uint)glb.Length, length);
    }

    [Fact]
    public void WriteGlb_JsonChunk_HasCorrectChunkType()
    {
        // glTF 2.0 spec §binary-gltf §Chunks: JSON chunk type = 0x4E4F534A.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeQuad(), ms);
        byte[] glb = ms.ToArray();

        // JSON chunk header is at offset 12.
        uint jsonChunkType = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16));
        Assert.Equal(0x4E4F534Au, jsonChunkType); // 'JSON'
    }

    [Fact]
    public void WriteGlb_BinChunk_HasCorrectChunkType()
    {
        // glTF 2.0 spec §binary-gltf §Chunks: BIN chunk type = 0x004E4942.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeQuad(), ms);
        byte[] glb = ms.ToArray();

        // JSON chunk header at 12; JSON chunk data starts at 20.
        uint jsonChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int binChunkHeaderOffset = 12 + 8 + (int)jsonChunkLength;
        uint binChunkType = BinaryPrimitives.ReadUInt32LittleEndian(
            glb.AsSpan(binChunkHeaderOffset + 4));
        Assert.Equal(0x004E4942u, binChunkType); // 'BIN\0'
    }

    [Fact]
    public void WriteGlb_AllChunks_ArePaddedTo4Bytes()
    {
        // glTF 2.0 spec §binary-gltf §Padding.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeQuad(), ms);
        byte[] glb = ms.ToArray();

        uint jsonChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0u, jsonChunkLength % 4u);

        int binChunkHeaderOffset = 12 + 8 + (int)jsonChunkLength;
        uint binChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binChunkHeaderOffset));
        Assert.Equal(0u, binChunkLength % 4u);
    }

    // -------------------------------------------------------------------------
    // glTF JSON structural tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_Json_ContainsExpectedAccessorCount()
    {
        // Expect 3 accessors: POSITION (0), TEXCOORD_0 (1), indices (2).
        string json = ExtractJson(MakeQuad());
        using var doc = JsonDocument.Parse(json);

        int accessorCount = doc.RootElement.GetProperty("accessors").GetArrayLength();
        Assert.Equal(3, accessorCount);
    }

    [Fact]
    public void WriteGlb_PositionAccessor_HasCorrectCountAndType()
    {
        StaticMesh mesh = MakeQuad();
        string json = ExtractJson(mesh);
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        Assert.Equal(mesh.Positions.Length, posAccessor.GetProperty("count").GetInt32());
        Assert.Equal("VEC3", posAccessor.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, posAccessor.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_TexCoordAccessor_HasCorrectCountAndType()
    {
        StaticMesh mesh = MakeQuad();
        string json = ExtractJson(mesh);
        using var doc = JsonDocument.Parse(json);

        var uvAccessor = doc.RootElement.GetProperty("accessors")[1];
        Assert.Equal(mesh.Uvs.Length, uvAccessor.GetProperty("count").GetInt32());
        Assert.Equal("VEC2", uvAccessor.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, uvAccessor.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_IndexAccessor_HasCorrectCountAndU16ComponentType()
    {
        StaticMesh mesh = MakeQuad();
        string json = ExtractJson(mesh);
        using var doc = JsonDocument.Parse(json);

        var idxAccessor = doc.RootElement.GetProperty("accessors")[2];
        Assert.Equal(mesh.Indices.Length, idxAccessor.GetProperty("count").GetInt32());
        Assert.Equal("SCALAR", idxAccessor.GetProperty("type").GetString());
        Assert.Equal(5123 /*UNSIGNED_SHORT*/, idxAccessor.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_PositionAccessor_MinMaxArePresent()
    {
        // glTF 2.0 spec §Accessor: min/max are required for POSITION.
        string json = ExtractJson(MakeSingleTriangle());
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        Assert.True(posAccessor.TryGetProperty("min", out _), "POSITION accessor must have min.");
        Assert.True(posAccessor.TryGetProperty("max", out _), "POSITION accessor must have max.");
    }

    [Fact]
    public void WriteGlb_PositionAccessor_MinMaxValues_CorrectWithHandednessFlip()
    {
        // Positions are (0,0,0), (2,0,0), (1,2,0).
        // After X-flip: (-0,0,0), (-2,0,0), (-1,2,0).
        // min = [-2, 0, 0], max = [0, 2, 0].
        // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x: CONFIRMED.
        // glTF 2.0 spec §3.4: right-handed, negate X to flip handedness.
        string json = ExtractJson(MakeSingleTriangle());
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        float minX = posAccessor.GetProperty("min")[0].GetSingle();
        float maxX = posAccessor.GetProperty("max")[0].GetSingle();
        float minY = posAccessor.GetProperty("min")[1].GetSingle();
        float maxY = posAccessor.GetProperty("max")[1].GetSingle();

        Assert.Equal(-2f, minX, precision: 5);
        Assert.Equal( 0f, maxX, precision: 5);
        Assert.Equal( 0f, minY, precision: 5);
        Assert.Equal( 2f, maxY, precision: 5);
    }

    [Fact]
    public void WriteGlb_Json_ContainsAssetVersion2()
    {
        // glTF 2.0 spec §asset object: version "2.0" is required.
        string json = ExtractJson(MakeQuad());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("2.0", doc.RootElement.GetProperty("asset").GetProperty("version").GetString());
    }

    // -------------------------------------------------------------------------
    // Binary buffer content tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_BinaryBuffer_ContainsExpectedPositionBytes()
    {
        // First vertex position (0,0,0) → after X-flip: (−0, 0, 0) = (0, 0, 0) as float.
        // Second vertex (1,0,0) → after X-flip: (−1, 0, 0).
        // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x: CONFIRMED.
        StaticMesh mesh = MakeSingleTriangle();
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(mesh, ms);
        byte[] glb = ms.ToArray();

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);

        var posAccessor = doc.RootElement.GetProperty("accessors")[0];
        int bvIndex = posAccessor.GetProperty("bufferView").GetInt32();
        var bv = doc.RootElement.GetProperty("bufferViews")[bvIndex];

        int byteOffset = bv.GetProperty("byteOffset").GetInt32();

        byte[] binData = ExtractBinChunk(glb);

        // Vertex 0: (0,0,0) → X-flipped = (0,0,0)
        float v0x = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(byteOffset));
        float v0y = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(byteOffset + 4));
        float v0z = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(byteOffset + 8));
        Assert.Equal( 0f, v0x, precision: 5);
        Assert.Equal( 0f, v0y, precision: 5);
        Assert.Equal( 0f, v0z, precision: 5);

        // Vertex 1: (2,0,0) → X-flipped = (−2, 0, 0)
        float v1x = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(byteOffset + 12));
        Assert.Equal(-2f, v1x, precision: 5);
    }

    [Fact]
    public void WriteGlb_BinaryBuffer_ContainsCorrectIndexBytes()
    {
        // Single triangle: indices [0,1,2].
        // After winding reversal (X-flip reverses CW→CCW): [0, 2, 1].
        // spec: Docs/RE/formats/mesh.md §Index list: CONFIRMED.
        // glTF 2.0 spec §3.7.2.1: counter-clockwise winding.
        StaticMesh mesh = MakeSingleTriangle();
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(mesh, ms);
        byte[] glb = ms.ToArray();

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);

        var idxAccessor = doc.RootElement.GetProperty("accessors")[2];
        int bvIndex = idxAccessor.GetProperty("bufferView").GetInt32();
        var bv = doc.RootElement.GetProperty("bufferViews")[bvIndex];
        int byteOffset = bv.GetProperty("byteOffset").GetInt32();

        byte[] binData = ExtractBinChunk(glb);

        ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(binData.AsSpan(byteOffset));
        ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(binData.AsSpan(byteOffset + 2));
        ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(binData.AsSpan(byteOffset + 4));

        // Winding swap: original [0,1,2] → written as [0,2,1]
        Assert.Equal((ushort)0, i0);
        Assert.Equal((ushort)2, i1);
        Assert.Equal((ushort)1, i2);
    }

    [Fact]
    public void WriteGlb_Output_IsDeterministic()
    {
        // Running the same mesh twice must produce identical byte sequences.
        StaticMesh mesh = MakeQuad();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        GltfConverter.WriteGlb(mesh, ms1);
        GltfConverter.WriteGlb(mesh, ms2);
        Assert.Equal(ms1.ToArray(), ms2.ToArray());
    }

    // -------------------------------------------------------------------------
    // SkinnedMesh overload smoke test
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_SkinnedMesh_EmitsValidGlbWithBaseGeometry()
    {
        // SkinnedMesh path uses corner expansion; verify a valid GLB is produced.
        var mesh = new SkinnedMesh
        {
            IdA       = 1,
            IdB       = 2,
            Name      = "TestSkin",
            FaceCount = 1,
            Corners   = [
                new SknCorner(0, 0f, 0f),
                new SknCorner(1, 1f, 0f),
                new SknCorner(2, 0.5f, 1f),
            ],
            Positions = [
                new Vec3(0f, 0f, 0f),
                new Vec3(1f, 0f, 0f),
                new Vec3(0.5f, 1f, 0f),
            ],
            Normals   = [
                new Vec3(0f, 0f, 1f),
                new Vec3(0f, 0f, 1f),
                new Vec3(0f, 0f, 1f),
            ],
            Weights   = [],
        };

        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(mesh, skeleton: null, ms);
        byte[] glb = ms.ToArray();

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        Assert.Equal(0x46546C67u, magic);

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("accessors").GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractJson(StaticMesh mesh)
    {
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(mesh, ms);
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
