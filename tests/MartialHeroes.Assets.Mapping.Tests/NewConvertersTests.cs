using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  BudSceneGltfConverter — .bud → glTF
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless structural tests for <see cref="BudSceneGltfConverter"/>.
/// No GPU, no disk, no Godot dependency.
///
/// Uses a synthetic <see cref="BudScene"/> mirroring the observed sample structure:
///   1 object, 5 vertices (32 bytes each), 9 indices (= 3 triangles).
/// spec: Docs/RE/formats/terrain_scene.md §File-size formula — verified for 195-byte sample.
/// </summary>
public sealed class BudSceneGltfConverterTests
{
    // -------------------------------------------------------------------------
    // Synthetic fixture
    // -------------------------------------------------------------------------

    /// <summary>
    /// One-object scene matching the observed .bud sample structure.
    /// 5 vertices (CONFIRMED vertex count in spec sample), 9 indices (3 triangles).
    /// spec: Docs/RE/formats/terrain_scene.md §Object header — vertex_count=5, index_count=9: CONFIRMED.
    /// spec: Docs/RE/formats/terrain_scene.md §Vertex record — pos_x/y/z, normal_x/y/z, uv_u/uv_v: CONFIRMED.
    /// </summary>
    private static BudScene MakeOneBudScene()
    {
        BudVertex[] verts =
        [
            new BudVertex(1000f, 70f, 2000f, 0f, 1f, 0f, 1.0f, 2.0f),
            new BudVertex(1010f, 70f, 2000f, 0f, 1f, 0f, 2.0f, 2.0f),
            new BudVertex(1010f, 80f, 2000f, 0f, 1f, 0f, 2.0f, 3.0f),
            new BudVertex(1000f, 80f, 2000f, 0f, 1f, 0f, 1.0f, 3.0f),
            new BudVertex(1005f, 90f, 2000f, 0f, 1f, 0f, 1.5f, 4.0f),
        ];

        ushort[] indices = [0, 1, 2, 0, 2, 3, 0, 3, 4];

        return new BudScene
        {
            Objects =
            [
                new BudObject
                {
                    TypeByte = 0, // spec: §Object header — type_byte=0: PARTIAL (only value observed)
                    TexId = 1, // spec: §Object header — tex_id=1 (1-based): PARTIAL
                    Vertices = verts,
                    Indices = indices,
                },
            ],
        };
    }

    /// <summary>Two-object scene (multi-primitive path).</summary>
    private static BudScene MakeTwoObjectScene()
    {
        BudVertex v = new(0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f);
        BudVertex v1 = new(1f, 0f, 0f, 0f, 1f, 0f, 1f, 0f);
        BudVertex v2 = new(0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f);

        BudObject obj = new()
        {
            TypeByte = 0,
            TexId = 1,
            Vertices = [v, v1, v2],
            Indices = [0, 1, 2],
        };

        return new BudScene { Objects = [obj, obj] };
    }

    // -------------------------------------------------------------------------
    // GLB container structure
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_Header_HasCorrectMagicAndVersion()
    {
        // glTF 2.0 spec §binary-gltf §Header: magic = 0x46546C67 ("glTF"), version = 2.
        using var ms = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(MakeOneBudScene(), ms);
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
    public void WriteGlb_Chunks_ArePaddedTo4Bytes()
    {
        // glTF 2.0 spec §binary-gltf §Padding.
        using var ms = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(MakeOneBudScene(), ms);
        byte[] glb = ms.ToArray();

        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0u, jsonLen % 4u);

        int binHdrOff = 12 + 8 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOff));
        Assert.Equal(0u, binLen % 4u);
    }

    // -------------------------------------------------------------------------
    // Accessor counts and types
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_OneObject_HasFourAccessors()
    {
        // Per object: POSITION(0), NORMAL(1), TEXCOORD_0(2), indices(3) → 4 accessors.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(4, doc.RootElement.GetProperty("accessors").GetArrayLength());
    }

    [Fact]
    public void WriteGlb_TwoObjects_HasEightAccessors()
    {
        // 2 objects × 4 accessors = 8.
        string json = ExtractJson(MakeTwoObjectScene());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(8, doc.RootElement.GetProperty("accessors").GetArrayLength());
    }

    [Fact]
    public void WriteGlb_PositionAccessor_HasCorrectVertexCount()
    {
        // 5 vertices confirmed in spec sample.
        // spec: Docs/RE/formats/terrain_scene.md §Object header — vertex_count=5: CONFIRMED.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        Assert.Equal(5, posAcc.GetProperty("count").GetInt32());
        Assert.Equal("VEC3", posAcc.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, posAcc.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_PositionAccessor_HasMinMax()
    {
        // glTF 2.0 spec §Accessor: min/max required for POSITION.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        Assert.True(posAcc.TryGetProperty("min", out _));
        Assert.True(posAcc.TryGetProperty("max", out _));
    }

    [Fact]
    public void WriteGlb_PositionAccessor_XIsNegated()
    {
        // Positions: PosX=1000..1010 → after X-flip min=-1010, max=-1000.
        // spec: Docs/RE/formats/terrain_scene.md §Coordinate system — X-axis: CONFIRMED.
        // glTF 2.0 spec §3.4: negate X for LH→RH conversion.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        float minX = posAcc.GetProperty("min")[0].GetSingle();
        float maxX = posAcc.GetProperty("max")[0].GetSingle();

        Assert.Equal(-1010f, minX, precision: 3);
        Assert.Equal(-1000f, maxX, precision: 3);
    }

    [Fact]
    public void WriteGlb_IndexAccessor_HasCorrectCount()
    {
        // 9 indices = 3 triangles.
        // spec: Docs/RE/formats/terrain_scene.md §index_count=9: CONFIRMED.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        // Index accessor is at index 3 (base 0: pos, nrm, uv, idx).
        var idxAcc = doc.RootElement.GetProperty("accessors")[3];
        Assert.Equal(9, idxAcc.GetProperty("count").GetInt32());
        Assert.Equal("SCALAR", idxAcc.GetProperty("type").GetString());
        Assert.Equal(5123 /*UNSIGNED_SHORT*/, idxAcc.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_NormalAccessor_IsVec3Float()
    {
        // spec: Docs/RE/formats/terrain_scene.md §Vertex record — normal_x/y/z: CONFIRMED.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        var nrmAcc = doc.RootElement.GetProperty("accessors")[1];
        Assert.Equal("VEC3", nrmAcc.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, nrmAcc.GetProperty("componentType").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Primitive structure
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_MeshHasOnePrimitive_ForOneObject()
    {
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        var prims = doc.RootElement.GetProperty("meshes")[0].GetProperty("primitives");
        Assert.Equal(1, prims.GetArrayLength());

        var attrs = prims[0].GetProperty("attributes");
        Assert.True(attrs.TryGetProperty("POSITION", out _));
        Assert.True(attrs.TryGetProperty("NORMAL", out _));
        Assert.True(attrs.TryGetProperty("TEXCOORD_0", out _));
        Assert.True(prims[0].TryGetProperty("indices", out _));
    }

    [Fact]
    public void WriteGlb_MeshHasTwoPrimitives_ForTwoObjects()
    {
        string json = ExtractJson(MakeTwoObjectScene());
        using var doc = JsonDocument.Parse(json);

        var prims = doc.RootElement.GetProperty("meshes")[0].GetProperty("primitives");
        Assert.Equal(2, prims.GetArrayLength());
    }

    [Fact]
    public void WriteGlb_PrimitiveExtras_ContainsTexId()
    {
        // spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id: PARTIAL.
        string json = ExtractJson(MakeOneBudScene());
        using var doc = JsonDocument.Parse(json);

        var extras = doc.RootElement.GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("extras");
        Assert.Equal(1, extras.GetProperty("texId").GetInt32());
        Assert.Equal(0, extras.GetProperty("typeByte").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Winding order (X-flip reversal)
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_Indices_AreWindingSwapped()
    {
        // Original triangle indices [0,1,2] → after X-flip winding swap → [0,2,1].
        // spec: Docs/RE/formats/terrain_scene.md §Index array — triangle list: CONFIRMED.
        // glTF 2.0 spec §3.7.2.1: CCW winding.
        var scene = new BudScene
        {
            Objects =
            [
                new BudObject
                {
                    TypeByte = 0,
                    TexId = 1,
                    Vertices =
                    [
                        new BudVertex(0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f),
                        new BudVertex(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f),
                        new BudVertex(0f, 1f, 0f, 0f, 1f, 0f, 0f, 0f),
                    ],
                    Indices = [0, 1, 2],
                },
            ],
        };

        using var ms = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(scene, ms);
        byte[] glb = ms.ToArray();

        // Locate index buffer via the JSON.
        string json = ExtractJsonFromBytes(glb);
        using var doc = JsonDocument.Parse(json);
        var idxAcc = doc.RootElement.GetProperty("accessors")[3];
        int bvIdx = idxAcc.GetProperty("bufferView").GetInt32();
        var bv = doc.RootElement.GetProperty("bufferViews")[bvIdx];
        int byteOff = bv.GetProperty("byteOffset").GetInt32();

        byte[] bin = ExtractBinChunk(glb);
        ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(byteOff));
        ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(byteOff + 2));
        ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(byteOff + 4));

        Assert.Equal((ushort)0, i0);
        Assert.Equal((ushort)2, i1); // swapped
        Assert.Equal((ushort)1, i2); // swapped
    }

    // -------------------------------------------------------------------------
    // Empty scene
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_EmptyScene_ProducesValidGlb()
    {
        var empty = new BudScene { Objects = [] };
        using var ms = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(empty, ms);
        byte[] glb = ms.ToArray();

        Assert.True(glb.Length >= 12);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        Assert.Equal(0x46546C67u, magic);

        // JSON must be parseable.
        string json = ExtractJsonFromBytes(glb);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("asset").ValueKind);
    }

    // -------------------------------------------------------------------------
    // Determinism
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_IsDeterministic()
    {
        BudScene scene = MakeOneBudScene();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(scene, ms1);
        BudSceneGltfConverter.WriteGlb(scene, ms2);
        Assert.Equal(ms1.ToArray(), ms2.ToArray());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractJson(BudScene scene)
    {
        using var ms = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(scene, ms);
        return ExtractJsonFromBytes(ms.ToArray());
    }

    private static string ExtractJsonFromBytes(byte[] glb)
    {
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return Encoding.UTF8.GetString(glb, 20, (int)jsonLen).TrimEnd(' ');
    }

    private static byte[] ExtractBinChunk(byte[] glb)
    {
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int binHdrOff = 12 + 8 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOff));
        int binDataOff = binHdrOff + 8;
        return glb[binDataOff..(binDataOff + (int)binLen)];
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CollisionLayerGltfConverter — .up / .exd → glTF
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless structural tests for <see cref="CollisionLayerGltfConverter"/>.
///
/// Uses 12 triangles (matching the observed .up sample count) and
/// 2 triangles (matching the observed .exd sample count).
/// spec: Docs/RE/formats/terrain_layers.md §2.1 — triangle_count=12: CONFIRMED.
/// spec: Docs/RE/formats/terrain_layers.md §3.1 — triangle_count=2:  CONFIRMED.
/// </summary>
public sealed class CollisionLayerGltfConverterTests
{
    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static CollisionTriangleList MakeUpList()
    {
        // 12 flat triangles at Y=100 (all vertices coplanar — confirmed by spec).
        // spec: Docs/RE/formats/terrain_layers.md §2.2 — all vertex Y = plane_height: CONFIRMED.
        var tris = new CollisionTriangle[12];
        for (int i = 0; i < 12; i++)
        {
            float baseX = i * 10f;
            tris[i] = new CollisionTriangle(
                V1X: baseX, V1Y: 100f, V1Z: 0f,
                V2X: baseX + 10f, V2Y: 100f, V2Z: 0f,
                V3X: baseX + 5f, V3Y: 100f, V3Z: 10f,
                PlaneHeight: 100f);
        }

        return new CollisionTriangleList { Triangles = tris };
    }

    private static CollisionTriangleList MakeExdList()
    {
        // 2 triangles (observed .exd sample count).
        // spec: Docs/RE/formats/terrain_layers.md §3.1 — triangle_count=2: CONFIRMED.
        CollisionTriangle t0 = new(0f, 50f, 0f, 10f, 50f, 0f, 5f, 50f, 10f, 50f);
        CollisionTriangle t1 = new(20f, 50f, 0f, 30f, 50f, 0f, 25f, 50f, 10f, 50f);
        return new CollisionTriangleList { Triangles = [t0, t1] };
    }

    // -------------------------------------------------------------------------
    // GLB header
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_UpLayer_HasGlTFMagic()
    {
        // glTF 2.0 spec §binary-gltf §Header.
        using var ms = new MemoryStream();
        CollisionLayerGltfConverter.WriteGlb(MakeUpList(), ms);
        byte[] glb = ms.ToArray();

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        Assert.Equal(0x46546C67u, magic);
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
    }

    [Fact]
    public void WriteGlb_Chunks_ArePaddedTo4Bytes()
    {
        // glTF 2.0 spec §binary-gltf §Padding.
        using var ms = new MemoryStream();
        CollisionLayerGltfConverter.WriteGlb(MakeUpList(), ms);
        byte[] glb = ms.ToArray();

        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0u, jsonLen % 4u);

        int binHdrOff = 12 + 8 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOff));
        Assert.Equal(0u, binLen % 4u);
    }

    // -------------------------------------------------------------------------
    // Accessor counts
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_UpLayer_TwoAccessors_PositionAndIndices()
    {
        // Expected: POSITION (0), indices (1).
        string json = ExtractJson(MakeUpList());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("accessors").GetArrayLength());
    }

    [Fact]
    public void WriteGlb_UpLayer_PositionAccessor_CountEquals36()
    {
        // 12 triangles × 3 vertices = 36.
        // spec: Docs/RE/formats/terrain_layers.md §2.1 — triangle_count=12: CONFIRMED.
        string json = ExtractJson(MakeUpList());
        using var doc = JsonDocument.Parse(json);

        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        Assert.Equal(36, posAcc.GetProperty("count").GetInt32());
        Assert.Equal("VEC3", posAcc.GetProperty("type").GetString());
    }

    [Fact]
    public void WriteGlb_UpLayer_IndexAccessor_CountEquals36()
    {
        // 12 triangles × 3 indices = 36.
        string json = ExtractJson(MakeUpList());
        using var doc = JsonDocument.Parse(json);

        var idxAcc = doc.RootElement.GetProperty("accessors")[1];
        Assert.Equal(36, idxAcc.GetProperty("count").GetInt32());
        Assert.Equal("SCALAR", idxAcc.GetProperty("type").GetString());
        Assert.Equal(5123 /*UNSIGNED_SHORT*/, idxAcc.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_ExdLayer_PositionAccessor_CountEquals6()
    {
        // 2 triangles × 3 vertices = 6.
        // spec: Docs/RE/formats/terrain_layers.md §3.1 — triangle_count=2: CONFIRMED.
        string json = ExtractJson(MakeExdList());
        using var doc = JsonDocument.Parse(json);
        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        Assert.Equal(6, posAcc.GetProperty("count").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Position min/max (X-flip)
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_PositionAccessor_HasMinMax()
    {
        // glTF 2.0 spec §Accessor: min/max required for POSITION.
        string json = ExtractJson(MakeUpList());
        using var doc = JsonDocument.Parse(json);
        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        Assert.True(posAcc.TryGetProperty("min", out _));
        Assert.True(posAcc.TryGetProperty("max", out _));
    }

    [Fact]
    public void WriteGlb_PositionAccessor_XIsNegated()
    {
        // For the ExD fixture: V1X values are 0..30, V3X=5,25 → max original X=30 → min glTF X=-30.
        // spec: Docs/RE/formats/terrain_layers.md §Overview — Y-up world space: CONFIRMED.
        // glTF 2.0 spec §3.4: negate X for LH→RH.
        string json = ExtractJson(MakeExdList());
        using var doc = JsonDocument.Parse(json);
        var posAcc = doc.RootElement.GetProperty("accessors")[0];
        float minX = posAcc.GetProperty("min")[0].GetSingle();
        Assert.True(minX < 0f, "X min must be negative after handedness flip.");
    }

    // -------------------------------------------------------------------------
    // Winding swap
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_Indices_AreWindingSwapped()
    {
        // Triangle [v0,v1,v2] → after X-flip winding swap → [0,2,1].
        // spec: Docs/RE/formats/terrain_layers.md §2.1 — triangle records v1/v2/v3: CONFIRMED.
        // glTF 2.0 spec §3.7.2.1: CCW winding.
        CollisionTriangle single = new(
            V1X: 0f, V1Y: 0f, V1Z: 0f,
            V2X: 1f, V2Y: 0f, V2Z: 0f,
            V3X: 0f, V3Y: 1f, V3Z: 0f,
            PlaneHeight: 0f);

        var list = new CollisionTriangleList { Triangles = [single] };
        using var ms = new MemoryStream();
        CollisionLayerGltfConverter.WriteGlb(list, ms);
        byte[] glb = ms.ToArray();

        string json = ExtractJsonFromBytes(glb);
        using var doc = JsonDocument.Parse(json);
        var idxAcc = doc.RootElement.GetProperty("accessors")[1];
        int bvIdx = idxAcc.GetProperty("bufferView").GetInt32();
        int byteOff = doc.RootElement.GetProperty("bufferViews")[bvIdx].GetProperty("byteOffset").GetInt32();

        byte[] bin = ExtractBinChunk(glb);
        ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(byteOff));
        ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(byteOff + 2));
        ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(byteOff + 4));

        Assert.Equal((ushort)0, i0);
        Assert.Equal((ushort)2, i1); // swapped v2↔v3
        Assert.Equal((ushort)1, i2); // swapped
    }

    // -------------------------------------------------------------------------
    // Determinism
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_IsDeterministic()
    {
        CollisionTriangleList list = MakeUpList();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        CollisionLayerGltfConverter.WriteGlb(list, ms1);
        CollisionLayerGltfConverter.WriteGlb(list, ms2);
        Assert.Equal(ms1.ToArray(), ms2.ToArray());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractJson(CollisionTriangleList list)
    {
        using var ms = new MemoryStream();
        CollisionLayerGltfConverter.WriteGlb(list, ms);
        return ExtractJsonFromBytes(ms.ToArray());
    }

    private static string ExtractJsonFromBytes(byte[] glb)
    {
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return Encoding.UTF8.GetString(glb, 20, (int)jsonLen).TrimEnd(' ');
    }

    private static byte[] ExtractBinChunk(byte[] glb)
    {
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int binHdrOff = 12 + 8 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOff));
        int binDataOff = binHdrOff + 8;
        return glb[binDataOff..(binDataOff + (int)binLen)];
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  AssetPassthrough — image and audio passthrough
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless structural tests for <see cref="AssetPassthrough"/>.
/// Verifies format detection and that raw bytes pass through unchanged.
/// </summary>
public sealed class AssetPassthroughTests
{
    // -------------------------------------------------------------------------
    // PNG passthrough
    // -------------------------------------------------------------------------

    [Fact]
    public void PassthroughImage_Png_DetectsFormatAndPreservesBytes()
    {
        // Minimal valid PNG: signature + IHDR chunk.
        // PNG signature: spec: Docs/RE/formats/texture.md §PNG §Identification: SAMPLE-VERIFIED.
        // IHDR width/height at offsets 16/20 (u32 BE).
        // spec: Docs/RE/formats/texture.md §PNG §IHDR: SAMPLE-VERIFIED.
        byte[] png = BuildMinimalPng(16, 16);
        var mem = new ReadOnlyMemory<byte>(png);

        ImagePassthroughResult result = AssetPassthrough.PassthroughImage(mem);

        Assert.Equal(ImageFormat.Png, result.Format);
        Assert.Equal(16, result.Width);
        Assert.Equal(16, result.Height);
        // Bytes span aliases input — same content.
        Assert.True(result.Bytes.Span.SequenceEqual(png));
    }

    [Fact]
    public void PassthroughImage_Png_SameBytesAsInput()
    {
        // No re-encoding: the returned bytes must be byte-identical to the input.
        byte[] png = BuildMinimalPng(4, 4);
        ReadOnlyMemory<byte> input = new(png);
        ImagePassthroughResult result = AssetPassthrough.PassthroughImage(input);
        Assert.True(result.Bytes.Span.SequenceEqual(png));
    }

    // -------------------------------------------------------------------------
    // BMP passthrough
    // -------------------------------------------------------------------------

    [Fact]
    public void PassthroughImage_Bmp_DetectsFormatAndReadsWidthHeight()
    {
        // BMP magic "BM" + BITMAPFILEHEADER (14) + BITMAPINFOHEADER width at 0x12, height at 0x16.
        // spec: Docs/RE/formats/texture.md §BMP §BITMAPINFOHEADER — Width i32 @ 0x12, Height i32 @ 0x16: SAMPLE-VERIFIED.
        byte[] bmp = BuildMinimalBmp(128, 64);
        var mem = new ReadOnlyMemory<byte>(bmp);

        ImagePassthroughResult result = AssetPassthrough.PassthroughImage(mem);

        Assert.Equal(ImageFormat.Bmp, result.Format);
        Assert.Equal(128, result.Width);
        Assert.Equal(64, result.Height);
        Assert.True(result.Bytes.Span.SequenceEqual(bmp));
    }

    // -------------------------------------------------------------------------
    // OGG passthrough
    // -------------------------------------------------------------------------

    [Fact]
    public void PassthroughAudio_Ogg_DetectsFormatAndPreservesBytes()
    {
        // OGG magic: "OggS" = 0x4F 0x67 0x67 0x53.
        // spec: Docs/RE/formats/sound_tables.md §7.1 — magic "OggS": SAMPLE-VERIFIED.
        byte[] ogg = BuildMinimalOgg();
        AudioPassthroughResult result = AssetPassthrough.PassthroughAudio(new ReadOnlyMemory<byte>(ogg));

        Assert.Equal(AudioFormat.OggVorbis, result.Format);
        Assert.True(result.Bytes.Span.SequenceEqual(ogg));
    }

    // -------------------------------------------------------------------------
    // WAV passthrough
    // -------------------------------------------------------------------------

    [Fact]
    public void PassthroughAudio_Wav_DetectsFormatAndPreservesBytes()
    {
        // WAV: "RIFF" at 0, "WAVE" at 8.
        // spec: Docs/RE/formats/sound_tables.md §7.2 — "RIFF"+"WAVE": SAMPLE-VERIFIED.
        byte[] wav = BuildMinimalWav();
        AudioPassthroughResult result = AssetPassthrough.PassthroughAudio(new ReadOnlyMemory<byte>(wav));

        Assert.Equal(AudioFormat.RiffWave, result.Format);
        Assert.True(result.Bytes.Span.SequenceEqual(wav));
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public void PassthroughImage_UnknownFormat_ThrowsNotSupported()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        Assert.Throws<NotSupportedException>(() =>
            AssetPassthrough.PassthroughImage(new ReadOnlyMemory<byte>(garbage)));
    }

    [Fact]
    public void PassthroughAudio_UnknownFormat_ThrowsNotSupported()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x0A, 0x0B, 0x0C];
        Assert.Throws<NotSupportedException>(() =>
            AssetPassthrough.PassthroughAudio(new ReadOnlyMemory<byte>(garbage)));
    }

    // -------------------------------------------------------------------------
    // Minimal byte-array builders (not real assets — just magic + header fields)
    // -------------------------------------------------------------------------

    private static byte[] BuildMinimalPng(int w, int h)
    {
        // Build a 33-byte fake PNG: 8-byte sig + 4 len + 4 "IHDR" + 4 w + 4 h + 9 more bytes.
        // Only width/height fields are used by the passthrough reader.
        byte[] buf = new byte[33];
        // Signature
        buf[0] = 0x89;
        buf[1] = 0x50;
        buf[2] = 0x4E;
        buf[3] = 0x47;
        buf[4] = 0x0D;
        buf[5] = 0x0A;
        buf[6] = 0x1A;
        buf[7] = 0x0A;
        // IHDR chunk length (4 bytes, BE) = 13
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8), 13);
        // Chunk type "IHDR"
        buf[12] = 0x49;
        buf[13] = 0x48;
        buf[14] = 0x44;
        buf[15] = 0x52;
        // Width and height (BE u32)
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(16), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(20), (uint)h);
        // Remaining IHDR bytes (bit depth, color type, etc.) — zeros for this minimal stub.
        return buf;
    }

    private static byte[] BuildMinimalBmp(int w, int h)
    {
        // Build a 54-byte BMP stub (header-only; no pixel data).
        // spec: Docs/RE/formats/texture.md §BMP §BITMAPFILEHEADER (14 B) + §BITMAPINFOHEADER (40 B).
        byte[] buf = new byte[54];
        // BITMAPFILEHEADER
        buf[0] = 0x42;
        buf[1] = 0x4D; // "BM"
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(2), (uint)buf.Length); // file size
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(10), 54u); // pixel data offset
        // BITMAPINFOHEADER
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(14), 40u); // header size
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(18), w); // width at 0x12
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(22), h); // height at 0x16
        return buf;
    }

    private static byte[] BuildMinimalOgg()
    {
        // Minimal "OggS" magic prefix (actual Ogg content not needed for magic detection).
        // spec: Docs/RE/formats/sound_tables.md §7.1: SAMPLE-VERIFIED.
        byte[] buf = new byte[16];
        buf[0] = 0x4F;
        buf[1] = 0x67;
        buf[2] = 0x67;
        buf[3] = 0x53; // "OggS"
        return buf;
    }

    private static byte[] BuildMinimalWav()
    {
        // "RIFF" at 0, size at 4, "WAVE" at 8.
        // spec: Docs/RE/formats/sound_tables.md §7.2: SAMPLE-VERIFIED.
        byte[] buf = new byte[16];
        buf[0] = 0x52;
        buf[1] = 0x49;
        buf[2] = 0x46;
        buf[3] = 0x46; // "RIFF"
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), 8u); // chunk size
        buf[8] = 0x57;
        buf[9] = 0x41;
        buf[10] = 0x56;
        buf[11] = 0x45; // "WAVE"
        return buf;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  XeffJsonConverter — .xeff → JSON
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless structural tests for <see cref="XeffJsonConverter"/>.
/// Verifies that the JSON output is parseable, contains required fields,
/// and faithfully represents the input data.
/// </summary>
public sealed class XeffJsonConverterTests
{
    // -------------------------------------------------------------------------
    // Synthetic fixture
    // -------------------------------------------------------------------------

    private static XeffData MakeSyntheticXeff()
    {
        // spec: Docs/RE/formats/effects.md §A.2 File Header — effect_id, element_count: VERIFIED.
        return new XeffData
        {
            EffectId = 42u,
            Elements =
            [
                new XeffElement
                {
                    // spec: Docs/RE/formats/effects.md §A.3.1 — EmitterType: PARSER-CONFIRMED.
                    EmitterType = 2u, // 2=directional (only known value)
                    EmitterSubtype = 0u,
                    AnimFlag = 0u,
                    TexCount = 1u,
                    FieldUnknownA = 99u, // UNRESOLVED — emitted raw
                    TextureNames = ["fireball"],
                    AlphaKeyframes = [0.0f, 0.5f, 1.0f],
                    ScaleX = [1.0f, 2.0f],
                    ScaleY = [1.0f, 2.0f],
                    ScaleZ = [1.0f, 2.0f],
                    AnimLoop = 0,
                    AnimStride = 0u,
                    AnimBaseTime = 0u,
                    AnimKeyframes = null,
                    // Static state path (AnimLoop=0).
                    // spec: Docs/RE/formats/effects.md §A.3.6 Branch B: PARSER-CONFIRMED.
                    StaticState = new XeffStaticState
                    {
                        Params = [1f, 2f, 3f, 4f, 5f, 6f],
                        RotXDeg = 0f,
                        RotYDeg = 45f,
                        RotZDeg = 0f,
                    },
                },
            ],
        };
    }

    private static XeffData MakeXeffWithKeyframes()
    {
        return new XeffData
        {
            EffectId = 7u,
            Elements =
            [
                new XeffElement
                {
                    EmitterType = 0u,
                    EmitterSubtype = 0u,
                    AnimFlag = 1u, // animated path
                    TexCount = 0u,
                    FieldUnknownA = 0u,
                    TextureNames = [],
                    AlphaKeyframes = [],
                    ScaleX = [],
                    ScaleY = [],
                    ScaleZ = [],
                    AnimLoop = 1, // non-zero → Branch A
                    AnimStride = 100u,
                    AnimBaseTime = 0u,
                    AnimKeyframes =
                    [
                        new XeffKeyframe
                        {
                            KfIndex = 0u,
                            Params = [1f, 2f, 3f, 4f, 5f, 6f],
                            RotXDeg = 0f,
                            RotYDeg = 90f,
                            RotZDeg = 0f,
                        },
                    ],
                    StaticState = null,
                },
            ],
        };
    }

    // -------------------------------------------------------------------------
    // Output is parseable JSON
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteJsonBytes_ProducesValidJson()
    {
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        Assert.True(json.Length > 0);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void WriteJson_Stream_IsValidJson()
    {
        using var ms = new MemoryStream();
        XeffJsonConverter.WriteJson(MakeSyntheticXeff(), ms);
        ms.Position = 0;
        using var doc = JsonDocument.Parse(ms);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // -------------------------------------------------------------------------
    // Required fields present
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteJsonBytes_ContainsEffectId()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id: VERIFIED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(42u, doc.RootElement.GetProperty("effectId").GetUInt32());
    }

    [Fact]
    public void WriteJsonBytes_ContainsElements()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — element_count=1: VERIFIED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("elements").GetArrayLength());
    }

    [Fact]
    public void WriteJsonBytes_ElementHasEmitterType()
    {
        // spec: Docs/RE/formats/effects.md §A.3.1 — emitterType=2: PARSER-CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        Assert.Equal(2u, el.GetProperty("emitterType").GetUInt32());
    }

    [Fact]
    public void WriteJsonBytes_ElementHasTextureNames()
    {
        // spec: Docs/RE/formats/effects.md §A.3.2 — texture names: PARSER-CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        Assert.Equal(1, el.GetProperty("textureNames").GetArrayLength());
        Assert.Equal("fireball", el.GetProperty("textureNames")[0].GetString());
    }

    [Fact]
    public void WriteJsonBytes_ElementHasAlphaKeyframes()
    {
        // spec: Docs/RE/formats/effects.md §A.3.3 — alpha keyframes: PARSER-CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        Assert.Equal(3, el.GetProperty("alphaKeyframes").GetArrayLength());
    }

    [Fact]
    public void WriteJsonBytes_UnresolvedField_IsEmittedRaw()
    {
        // fieldUnknownA = 99 (UNRESOLVED) must appear verbatim in the JSON output.
        // spec: Docs/RE/formats/effects.md §A.3.1 — field_unknown_a: PARSER-CONFIRMED (raw value).
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        Assert.Equal(99u, el.GetProperty("fieldUnknownA").GetUInt32());
    }

    [Fact]
    public void WriteJsonBytes_StaticState_IsPresentWhenAnimLoopZero()
    {
        // spec: Docs/RE/formats/effects.md §A.3.6 Branch B: PARSER-CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeSyntheticXeff());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        Assert.True(el.TryGetProperty("staticState", out var ss));
        Assert.Equal(6, ss.GetProperty("params").GetArrayLength());
        Assert.Equal(45f, ss.GetProperty("rotYDeg").GetSingle(), precision: 3);
    }

    [Fact]
    public void WriteJsonBytes_AnimKeyframes_ArePresentWhenAnimLoopNonZero()
    {
        // spec: Docs/RE/formats/effects.md §A.3.6 Branch A: PARSER-CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeXeffWithKeyframes());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        Assert.True(el.TryGetProperty("animKeyframes", out var kfs));
        Assert.Equal(1, kfs.GetArrayLength());
        Assert.Equal(90f, kfs[0].GetProperty("rotYDeg").GetSingle(), precision: 3);
    }

    [Fact]
    public void WriteJsonBytes_EmptyElements_IsValidJson()
    {
        var effect = new XeffData
        {
            EffectId = 0u,
            Elements = [],
        };
        byte[] json = XeffJsonConverter.WriteJsonBytes(effect);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("elements").GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Determinism
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteJsonBytes_IsDeterministic()
    {
        XeffData effect = MakeSyntheticXeff();
        byte[] json1 = XeffJsonConverter.WriteJsonBytes(effect);
        byte[] json2 = XeffJsonConverter.WriteJsonBytes(effect);
        Assert.Equal(json1, json2);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BudSceneGltfConverter — Normal and UV binary correctness
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verifies that per-vertex Normal (NORMAL) and UV (TEXCOORD_0) attributes are correctly
/// written to the GLB binary buffer with the expected coordinate-system conversion applied.
///
/// Coordinate conventions:
///   .bud stores vertex normals as (normalX, normalY, normalZ) f32, unit-length.
///   spec: Docs/RE/formats/terrain_scene.md §3.2.2 — normal_x @ +0x0C, normal_y @ +0x10, normal_z @ +0x14: CONFIRMED.
///   Handedness flip: negate X for left-handed D3D9 → right-handed glTF.
///   spec: Docs/RE/formats/terrain_scene.md §Coordinate system — X is horizontal: CONFIRMED.
///   glTF 2.0 spec §3.4: right-handed Y-up, negate X.
///
///   UV: stored as world-scale tiled f32 values.
///   spec: Docs/RE/formats/terrain_scene.md §3.2.2 — uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
///   No V-flip applied (no top-left/bottom-left mismatch documented for terrain geometry UVs).
/// </summary>
public sealed class BudSceneNormalAndUvBinaryTests
{
    // -------------------------------------------------------------------------
    // Synthetic scene with known normal and UV values
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scene with a single triangle whose vertices have known, non-trivial normals and UVs.
    /// Normal (0.577, 0.577, 0.577) is approximately a unit vector (√(1/3) ≈ 0.5774).
    /// UV values chosen to be clearly distinguishable in the buffer.
    /// spec: Docs/RE/formats/terrain_scene.md §3.2.2 — vertex record layout: CONFIRMED.
    /// </summary>
    private static BudScene MakeKnownNormalUvScene()
    {
        // A right-isoceles triangle; all three vertices share the same surface normal pointing at 45°.
        const float n = 0.577350259f; // ≈ 1/√3 — unit vector for (1,1,1) direction
        BudVertex[] verts =
        [
            new BudVertex(PosX: 0f, PosY: 0f, PosZ: 0f, NormalX: n, NormalY: n, NormalZ: n, UvU: 0.0f, UvV: 0.0f),
            new BudVertex(PosX: 1f, PosY: 0f, PosZ: 0f, NormalX: n, NormalY: n, NormalZ: n, UvU: 1.0f, UvV: 0.0f),
            new BudVertex(PosX: 0f, PosY: 0f, PosZ: 1f, NormalX: n, NormalY: n, NormalZ: n, UvU: 0.0f, UvV: 1.0f),
        ];

        return new BudScene
        {
            Objects =
            [
                new BudObject
                {
                    TypeByte = 0,
                    TexId = 1,
                    Vertices = verts,
                    Indices = [0, 1, 2],
                },
            ],
        };
    }

    // -------------------------------------------------------------------------
    // Normal attribute — presence and binary values
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_NormalAccessor_PresentInAttributes()
    {
        // NORMAL accessor must be present as attribute index 1 in the primitive.
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — normal_x/y/z CONFIRMED.
        string json = ExtractJson(MakeKnownNormalUvScene());
        using var doc = JsonDocument.Parse(json);
        var attrs = doc.RootElement
            .GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");
        Assert.True(attrs.TryGetProperty("NORMAL", out _),
            "NORMAL attribute must be present in the primitive.");
    }

    [Fact]
    public void WriteGlb_NormalBuffer_HasXFlipped()
    {
        // The X component of each normal must be negated in the binary buffer.
        // Original NormalX = +0.577… → written as −0.577…
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — normal_x @ +0x0C: CONFIRMED.
        // glTF 2.0 spec §3.4: negate X for LH→RH handedness flip.
        BudScene scene = MakeKnownNormalUvScene();
        byte[] glb = WriteToBytes(scene);
        string json = ExtractJsonFromBytes(glb);
        using var doc = JsonDocument.Parse(json);

        // Accessor index 1 = NORMAL (per: pos=0, nrm=1, uv=2, idx=3).
        int bvIdx = doc.RootElement.GetProperty("accessors")[1]
            .GetProperty("bufferView").GetInt32();
        int byteOff = doc.RootElement.GetProperty("bufferViews")[bvIdx]
            .GetProperty("byteOffset").GetInt32();

        byte[] bin = ExtractBinChunk(glb);
        // Vertex 0 normal: first 12 bytes at byteOff
        float nx = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 0));
        float ny = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 4));
        float nz = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 8));

        const float expected = 0.577350259f;
        Assert.Equal(-expected, nx, precision: 4); // X must be negated
        Assert.Equal(expected, ny, precision: 4); // Y unchanged
        Assert.Equal(expected, nz, precision: 4); // Z unchanged
    }

    [Fact]
    public void WriteGlb_NormalAccessor_CountMatchesVertexCount()
    {
        // The NORMAL accessor must have the same count as the number of vertices.
        // spec: Docs/RE/formats/terrain_scene.md §3.2 — vertex_count: CONFIRMED.
        string json = ExtractJson(MakeKnownNormalUvScene());
        using var doc = JsonDocument.Parse(json);
        // accessor[0]=POSITION, accessor[1]=NORMAL, accessor[2]=TEXCOORD_0, accessor[3]=indices
        var nrmAcc = doc.RootElement.GetProperty("accessors")[1];
        Assert.Equal(3, nrmAcc.GetProperty("count").GetInt32()); // 3 vertices
        Assert.Equal("VEC3", nrmAcc.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, nrmAcc.GetProperty("componentType").GetInt32());
    }

    // -------------------------------------------------------------------------
    // UV attribute — presence and binary values
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_TexCoordAccessor_PresentInAttributes()
    {
        // TEXCOORD_0 accessor must be present as attribute index 2 in the primitive.
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
        string json = ExtractJson(MakeKnownNormalUvScene());
        using var doc = JsonDocument.Parse(json);
        var attrs = doc.RootElement
            .GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");
        Assert.True(attrs.TryGetProperty("TEXCOORD_0", out _),
            "TEXCOORD_0 attribute must be present in the primitive.");
    }

    [Fact]
    public void WriteGlb_UvBuffer_HasCorrectValues()
    {
        // UV values must be written verbatim (no V-flip): vertex 0 → (0,0), vertex 1 → (1,0).
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — uv_u @ +0x18: CONFIRMED.
        // No top-left/bottom-left flip documented for terrain geometry UVs.
        BudScene scene = MakeKnownNormalUvScene();
        byte[] glb = WriteToBytes(scene);
        string json = ExtractJsonFromBytes(glb);
        using var doc = JsonDocument.Parse(json);

        // Accessor index 2 = TEXCOORD_0.
        int bvIdx = doc.RootElement.GetProperty("accessors")[2]
            .GetProperty("bufferView").GetInt32();
        int byteOff = doc.RootElement.GetProperty("bufferViews")[bvIdx]
            .GetProperty("byteOffset").GetInt32();

        byte[] bin = ExtractBinChunk(glb);
        // Vertex 0: U=0.0, V=0.0
        float u0 = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 0));
        float v0 = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 4));
        // Vertex 1: U=1.0, V=0.0
        float u1 = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 8));
        float v1 = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 12));
        // Vertex 2: U=0.0, V=1.0
        float u2 = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 16));
        float v2 = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(byteOff + 20));

        Assert.Equal(0.0f, u0, precision: 5);
        Assert.Equal(0.0f, v0, precision: 5);
        Assert.Equal(1.0f, u1, precision: 5);
        Assert.Equal(0.0f, v1, precision: 5);
        Assert.Equal(0.0f, u2, precision: 5);
        Assert.Equal(1.0f, v2, precision: 5);
    }

    [Fact]
    public void WriteGlb_TexCoordAccessor_IsVec2Float()
    {
        // TEXCOORD_0 accessor must be VEC2 / FLOAT.
        // glTF 2.0 spec §Accessor: TEXCOORD_0 is VEC2 float.
        string json = ExtractJson(MakeKnownNormalUvScene());
        using var doc = JsonDocument.Parse(json);
        var uvAcc = doc.RootElement.GetProperty("accessors")[2];
        Assert.Equal("VEC2", uvAcc.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, uvAcc.GetProperty("componentType").GetInt32());
        Assert.Equal(3, uvAcc.GetProperty("count").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractJson(BudScene scene)
        => ExtractJsonFromBytes(WriteToBytes(scene));

    private static byte[] WriteToBytes(BudScene scene)
    {
        using var ms = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(scene, ms);
        return ms.ToArray();
    }

    private static string ExtractJsonFromBytes(byte[] glb)
    {
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return Encoding.UTF8.GetString(glb, 20, (int)jsonLen).TrimEnd(' ');
    }

    private static byte[] ExtractBinChunk(byte[] glb)
    {
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int binHdrOff = 12 + 8 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOff));
        int binDataOff = binHdrOff + 8;
        return glb[binDataOff..(binDataOff + (int)binLen)];
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  XeffJsonConverter — named Velocity / Size / Rotation fields
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verifies that XeffJsonConverter emits properly named Velocity, Size, and Rotation
/// fields in keyframes and static states, per the resolved spec.
///
/// spec: Docs/RE/formats/effects.md §A.3.7 — velocity Vec3 (Params[0..2]): HIGH.
/// spec: Docs/RE/formats/effects.md §A.3.7 — size Vec3 (Params[3..5]): HIGH.
/// spec: Docs/RE/formats/effects.md §A.4  — rotation quaternion (Euler → quat): CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.3.3 — alpha opacity (1 - file_value): CONFIRMED.
/// </summary>
public sealed class XeffJsonNamedFieldsTests
{
    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Static-state element (AnimLoop=0) with known velocity, size, and rotation values.
    /// EmitterType=2 so rotation floats are present.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch B — emitter_type==2 reads rotation: CONFIRMED.
    /// </summary>
    private static XeffData MakeStaticStateEffect()
    {
        return new XeffData
        {
            EffectId = 100u,
            Elements =
            [
                new XeffElement
                {
                    EmitterType = 2u, // directional billboard: reads rotation floats
                    EmitterSubtype = 0u,
                    AnimFlag = 0u,
                    TexCount = 0u,
                    FieldUnknownA = 0u,
                    TextureNames = [],
                    // AlphaKeyframes: file stores 0.3 (transparency) → parser converts to 0.7 (opacity).
                    // spec: Docs/RE/formats/effects.md §A.3.3 — "1 - file_value = opacity": CONFIRMED.
                    AlphaKeyframes = [0.7f], // already opacity (parser has done 1 - 0.3)
                    ScaleX = [],
                    ScaleY = [],
                    ScaleZ = [],
                    AnimLoop = 0,
                    AnimStride = 0u,
                    AnimBaseTime = 0u,
                    AnimKeyframes = null,
                    StaticState = new XeffStaticState
                    {
                        // Params[0..2] = velocity (2, 3, 4); Params[3..5] = size (5, 6, 7).
                        // spec: Docs/RE/formats/effects.md §A.3.7 — velocity Vec3 Params[0..2]: HIGH.
                        // spec: Docs/RE/formats/effects.md §A.3.7 — size Vec3 Params[3..5]: HIGH.
                        Params = [2f, 3f, 4f, 5f, 6f, 7f],
                        // Rotation: 90° around Y-axis → Quat(0, sin(45°), 0, cos(45°)) ≈ (0, 0.7071, 0, 0.7071).
                        // spec: Docs/RE/formats/effects.md §A.4 — half-angle Euler-XYZ: CONFIRMED.
                        RotXDeg = 0f,
                        RotYDeg = 90f,
                        RotZDeg = 0f,
                    },
                },
            ],
        };
    }

    /// <summary>
    /// Animated keyframe element (AnimLoop=1) with a single keyframe containing
    /// known velocity, size, and rotation.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch A: PARSER-CONFIRMED.
    /// </summary>
    private static XeffData MakeAnimatedKeyframeEffect()
    {
        return new XeffData
        {
            EffectId = 200u,
            Elements =
            [
                new XeffElement
                {
                    EmitterType = 0u, // billboard
                    EmitterSubtype = 0u,
                    AnimFlag = 1u,
                    TexCount = 1u,
                    FieldUnknownA = 0u,
                    TextureNames = ["smoke"],
                    AlphaKeyframes = [],
                    ScaleX = [],
                    ScaleY = [],
                    ScaleZ = [],
                    AnimLoop = 1, // Branch A
                    AnimStride = 100u,
                    AnimBaseTime = 0u,
                    AnimKeyframes =
                    [
                        new XeffKeyframe
                        {
                            KfIndex = 0u,
                            // Params[0..2] = velocity (10, 20, 30); Params[3..5] = size (1, 2, 3).
                            // spec: Docs/RE/formats/effects.md §A.3.7 — velocity Params[0..2]: HIGH.
                            // spec: Docs/RE/formats/effects.md §A.3.7 — size Params[3..5]: HIGH.
                            Params = [10f, 20f, 30f, 1f, 2f, 3f],
                            // Rotation: 180° around Z → Quat(0, 0, sin(90°), cos(90°)) = (0, 0, 1, 0).
                            // spec: Docs/RE/formats/effects.md §A.4 — half-angle Euler-XYZ: CONFIRMED.
                            RotXDeg = 0f,
                            RotYDeg = 0f,
                            RotZDeg = 180f,
                        },
                    ],
                    StaticState = null,
                },
            ],
        };
    }

    // -------------------------------------------------------------------------
    // Static state: velocity, size, rotation
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteJsonBytes_StaticState_HasNamedVelocity()
    {
        // staticState.velocity must be a 3-element array [2, 3, 4].
        // spec: Docs/RE/formats/effects.md §A.3.7 — static_velocity Vec3 (Params[0..2]): HIGH.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeStaticStateEffect());
        using var doc = JsonDocument.Parse(json);
        var ss = doc.RootElement.GetProperty("elements")[0].GetProperty("staticState");

        Assert.True(ss.TryGetProperty("velocity", out var vel),
            "staticState must expose named 'velocity' field.");
        Assert.Equal(3, vel.GetArrayLength());
        Assert.Equal(2f, vel[0].GetSingle(), precision: 4);
        Assert.Equal(3f, vel[1].GetSingle(), precision: 4);
        Assert.Equal(4f, vel[2].GetSingle(), precision: 4);
    }

    [Fact]
    public void WriteJsonBytes_StaticState_HasNamedSize()
    {
        // staticState.size must be a 3-element array [5, 6, 7].
        // spec: Docs/RE/formats/effects.md §A.3.7 — static_size Vec3 (Params[3..5]): HIGH.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeStaticStateEffect());
        using var doc = JsonDocument.Parse(json);
        var ss = doc.RootElement.GetProperty("elements")[0].GetProperty("staticState");

        Assert.True(ss.TryGetProperty("size", out var sz),
            "staticState must expose named 'size' field.");
        Assert.Equal(3, sz.GetArrayLength());
        Assert.Equal(5f, sz[0].GetSingle(), precision: 4);
        Assert.Equal(6f, sz[1].GetSingle(), precision: 4);
        Assert.Equal(7f, sz[2].GetSingle(), precision: 4);
    }

    [Fact]
    public void WriteJsonBytes_StaticState_HasRotationQuaternion()
    {
        // staticState.rotation must be a 4-element [X,Y,Z,W] quaternion.
        // 90° around Y → approx (0, 0.7071, 0, 0.7071).
        // spec: Docs/RE/formats/effects.md §A.4 — half-angle Euler-XYZ: CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeStaticStateEffect());
        using var doc = JsonDocument.Parse(json);
        var ss = doc.RootElement.GetProperty("elements")[0].GetProperty("staticState");

        Assert.True(ss.TryGetProperty("rotation", out var rot),
            "staticState must expose named 'rotation' quaternion.");
        Assert.Equal(4, rot.GetArrayLength());
        // For 90° Y rotation: X=0, Y≈sin(45°)=0.7071, Z=0, W≈cos(45°)=0.7071.
        Assert.Equal(0f, rot[0].GetSingle(), precision: 3); // X
        Assert.Equal(0.7071f, rot[1].GetSingle(), precision: 3); // Y
        Assert.Equal(0f, rot[2].GetSingle(), precision: 3); // Z
        Assert.Equal(0.7071f, rot[3].GetSingle(), precision: 3); // W
    }

    [Fact]
    public void WriteJsonBytes_StaticState_StillHasRawParams()
    {
        // The raw params array must still be present for backward compat/fallback.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeStaticStateEffect());
        using var doc = JsonDocument.Parse(json);
        var ss = doc.RootElement.GetProperty("elements")[0].GetProperty("staticState");

        Assert.True(ss.TryGetProperty("params", out var p),
            "staticState must retain raw 'params' array for fallback.");
        Assert.Equal(6, p.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Animated keyframe: velocity, size, rotation
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteJsonBytes_AnimKeyframe_HasNamedVelocity()
    {
        // keyframe.velocity must be a 3-element array [10, 20, 30].
        // spec: Docs/RE/formats/effects.md §A.3.7 — kf_velocity Vec3 (Params[0..2]): HIGH.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeAnimatedKeyframeEffect());
        using var doc = JsonDocument.Parse(json);
        var kf = doc.RootElement.GetProperty("elements")[0].GetProperty("animKeyframes")[0];

        Assert.True(kf.TryGetProperty("velocity", out var vel),
            "keyframe must expose named 'velocity' field.");
        Assert.Equal(3, vel.GetArrayLength());
        Assert.Equal(10f, vel[0].GetSingle(), precision: 4);
        Assert.Equal(20f, vel[1].GetSingle(), precision: 4);
        Assert.Equal(30f, vel[2].GetSingle(), precision: 4);
    }

    [Fact]
    public void WriteJsonBytes_AnimKeyframe_HasNamedSize()
    {
        // keyframe.size must be a 3-element array [1, 2, 3].
        // spec: Docs/RE/formats/effects.md §A.3.7 — kf_size Vec3 (Params[3..5]): HIGH.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeAnimatedKeyframeEffect());
        using var doc = JsonDocument.Parse(json);
        var kf = doc.RootElement.GetProperty("elements")[0].GetProperty("animKeyframes")[0];

        Assert.True(kf.TryGetProperty("size", out var sz),
            "keyframe must expose named 'size' field.");
        Assert.Equal(3, sz.GetArrayLength());
        Assert.Equal(1f, sz[0].GetSingle(), precision: 4);
        Assert.Equal(2f, sz[1].GetSingle(), precision: 4);
        Assert.Equal(3f, sz[2].GetSingle(), precision: 4);
    }

    [Fact]
    public void WriteJsonBytes_AnimKeyframe_HasRotationQuaternion()
    {
        // keyframe.rotation must be a 4-element [X,Y,Z,W] quaternion.
        // 180° around Z → Q = (0, 0, sin(90°), cos(90°)) = (0, 0, 1, 0).
        // spec: Docs/RE/formats/effects.md §A.4 — half-angle Euler-XYZ: CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeAnimatedKeyframeEffect());
        using var doc = JsonDocument.Parse(json);
        var kf = doc.RootElement.GetProperty("elements")[0].GetProperty("animKeyframes")[0];

        Assert.True(kf.TryGetProperty("rotation", out var rot),
            "keyframe must expose named 'rotation' quaternion.");
        Assert.Equal(4, rot.GetArrayLength());
        // 180° Z: X=0, Y=0, Z=sin(90°)≈1, W=cos(90°)≈0
        Assert.Equal(0f, rot[0].GetSingle(), precision: 3); // X
        Assert.Equal(0f, rot[1].GetSingle(), precision: 3); // Y
        Assert.Equal(1f, rot[2].GetSingle(), precision: 3); // Z
        Assert.Equal(0f, rot[3].GetSingle(), precision: 3); // W
    }

    [Fact]
    public void WriteJsonBytes_AnimKeyframe_StillHasRawParams()
    {
        // Raw params array must still be present.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeAnimatedKeyframeEffect());
        using var doc = JsonDocument.Parse(json);
        var kf = doc.RootElement.GetProperty("elements")[0].GetProperty("animKeyframes")[0];
        Assert.True(kf.TryGetProperty("params", out var p));
        Assert.Equal(6, p.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Alpha keyframes: opacity emitted correctly
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteJsonBytes_AlphaKeyframes_EmittedAsOpacity()
    {
        // The parser stores AlphaKeyframes as opacity (1 - file_value).
        // The converter must emit alphaKeyframes[i].opacity.
        // spec: Docs/RE/formats/effects.md §A.3.3 — "in-memory value is opacity": CONFIRMED.
        byte[] json = XeffJsonConverter.WriteJsonBytes(MakeStaticStateEffect());
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("elements")[0];
        var alphas = el.GetProperty("alphaKeyframes");
        Assert.Equal(1, alphas.GetArrayLength());
        // The fixture stores 0.7 opacity (parser has already done 1 - 0.3 = 0.7).
        Assert.True(alphas[0].TryGetProperty("opacity", out var op),
            "alphaKeyframes entries must expose 'opacity' field.");
        Assert.Equal(0.7f, op.GetSingle(), precision: 4);
    }
}