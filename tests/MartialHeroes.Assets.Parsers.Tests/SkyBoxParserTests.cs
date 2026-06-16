using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="SkyBoxParser"/>.
/// All fixtures are built in-memory from the spec — no real game bytes are committed.
/// spec: Docs/RE/formats/sky.md §A — sky%d.box geometry.
/// </summary>
public sealed class SkyBoxParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteU16LE(byte[] buf, int offset, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteAsciiFixed(byte[] buf, int offset, int fieldLen, string s)
    {
        // Write null-terminated ASCII into a fixed-width field (zero-padded).
        byte[] encoded = Encoding.ASCII.GetBytes(s);
        int copyLen = Math.Min(encoded.Length, fieldLen - 1); // always leave null terminator
        encoded.AsSpan(0, copyLen).CopyTo(buf.AsSpan(offset, copyLen));
        // Remaining bytes are already zero (new byte[] is zero-initialised).
    }

    /// <summary>
    /// Builds a minimal synthetic sky.box fixture.
    /// Layout follows spec §A.6:
    ///   u32 texture_count
    ///   texture_name[texture_count]          // 47 bytes each
    ///   for each mesh: u32 vertex_count, vertex[] (20 bytes each)
    ///   for each mesh: u32 index_count,  u16[]   (2 bytes each)
    /// spec: Docs/RE/formats/sky.md §A.6 Overall structure.
    /// </summary>
    private static byte[] BuildSkyBox(
        string[] textureNames,
        (float x, float y, float z, float u, float v)[][] verticesPerMesh,
        ushort[][] indicesPerMesh)
    {
        int n = textureNames.Length;

        // Compute buffer size.
        // Header: 4 bytes.
        // spec: Docs/RE/formats/sky.md §A.1 — texture_count u32 @ 0x00.
        int size = 4;

        // Texture-name block: n × 47 bytes.
        // spec: Docs/RE/formats/sky.md §A.2 — Record stride: 47 bytes.
        size += n * 47; // spec: §A.2

        // Vertex arrays: for each mesh, 4 + vertex_count × 20.
        // spec: Docs/RE/formats/sky.md §A.3.
        for (int i = 0; i < n; i++)
            size += 4 + verticesPerMesh[i].Length * 20; // spec: §A.3

        // Index arrays: for each mesh, 4 + index_count × 2.
        // spec: Docs/RE/formats/sky.md §A.5.
        for (int i = 0; i < n; i++)
            size += 4 + indicesPerMesh[i].Length * 2; // spec: §A.5

        var buf = new byte[size];
        int cursor = 0;

        // Header: texture_count.
        // spec: Docs/RE/formats/sky.md §A.1 — texture_count u32 @ 0x00: HIGH
        WriteU32LE(buf, cursor, (uint)n);
        cursor += 4;

        // Texture-name records.
        // spec: Docs/RE/formats/sky.md §A.2 — char[47] per record, null-terminated: HIGH
        for (int i = 0; i < n; i++)
        {
            WriteAsciiFixed(buf, cursor, 47, textureNames[i]);
            cursor += 47; // spec: §A.2 stride = 47
        }

        // Vertex arrays (first pass, all meshes).
        // spec: Docs/RE/formats/sky.md §A.6 — all vertex arrays precede all index arrays.
        for (int i = 0; i < n; i++)
        {
            var verts = verticesPerMesh[i];
            WriteU32LE(buf, cursor, (uint)verts.Length); // vertex_count
            cursor += 4;
            foreach (var (x, y, z, u, v) in verts)
            {
                // position x, y, z — f32 @ sub-offset 0x00, 0x04, 0x08.
                // spec: Docs/RE/formats/sky.md §A.4 — position f32[3]: MED
                WriteF32LE(buf, cursor + 0x00, x);
                WriteF32LE(buf, cursor + 0x04, y);
                WriteF32LE(buf, cursor + 0x08, z);
                // uv u, v — f32 @ sub-offset 0x0C, 0x10.
                // spec: Docs/RE/formats/sky.md §A.4 — uv f32[2]: MED
                WriteF32LE(buf, cursor + 0x0C, u);
                WriteF32LE(buf, cursor + 0x10, v);
                cursor += 20; // spec: §A.3 stride = 20
            }
        }

        // Index arrays (second pass, all meshes).
        // spec: Docs/RE/formats/sky.md §A.6 — index arrays after vertex arrays.
        for (int i = 0; i < n; i++)
        {
            var idxs = indicesPerMesh[i];
            WriteU32LE(buf, cursor, (uint)idxs.Length); // index_count
            cursor += 4;
            foreach (ushort idx in idxs)
            {
                // u16 index, LE.
                // spec: Docs/RE/formats/sky.md §A.5 — u16 index: HIGH
                WriteU16LE(buf, cursor, idx);
                cursor += 2; // spec: §A.5 width = 2
            }
        }

        return buf;
    }

    // =========================================================================
    // Header / count tests
    // =========================================================================

    /// <summary>
    /// A single-texture box with one vertex and zero indices parses without error.
    /// Verifies header decode, texture_count, and structural round-trip.
    /// spec: Docs/RE/formats/sky.md §A.1, §A.2, §A.3, §A.5.
    /// </summary>
    [Fact]
    public void Parse_SingleTexture_OneVertex_ZeroIndices_Succeeds()
    {
        byte[] fixture = BuildSkyBox(
            textureNames: ["sky_tex0"],
            verticesPerMesh: [[(1f, 2f, 3f, 0.5f, 0.25f)]],
            indicesPerMesh: [[]]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        // texture_count → 1 mesh + 1 name.
        // spec: Docs/RE/formats/sky.md §A.1 — texture_count u32 @ 0x00: HIGH
        Assert.Single(result.TextureNames);
        Assert.Single(result.Meshes);
        Assert.Equal("sky_tex0", result.TextureNames[0]);
    }

    /// <summary>
    /// Three textures produce three names and three meshes.
    /// spec: Docs/RE/formats/sky.md §A.1, §A.6.
    /// </summary>
    [Fact]
    public void Parse_ThreeTextures_ProducesThreeNamesAndMeshes()
    {
        byte[] fixture = BuildSkyBox(
            textureNames: ["sky_a", "sky_b", "sky_c"],
            verticesPerMesh:
            [
                [(0f, 0f, 0f, 0f, 0f)],
                [(1f, 0f, 0f, 0f, 0f)],
                [(2f, 0f, 0f, 0f, 0f)],
            ],
            indicesPerMesh: [[], [], []]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        Assert.Equal(3, result.TextureNames.Length);
        Assert.Equal(3, result.Meshes.Length);
        Assert.Equal("sky_a", result.TextureNames[0]);
        Assert.Equal("sky_b", result.TextureNames[1]);
        Assert.Equal("sky_c", result.TextureNames[2]);
    }

    // =========================================================================
    // Vertex decode tests
    // =========================================================================

    /// <summary>
    /// The 20-byte vertex stride is decoded as position(x,y,z) + UV(u,v) as specified.
    /// Confidence: MED (sample-unverified — see spec §A.4 known-unknowns).
    /// spec: Docs/RE/formats/sky.md §A.4 — position f32[3] + uv f32[2]: MED.
    /// </summary>
    [Fact]
    public void Parse_VertexDecode_PositionAndUv_MatchFixture()
    {
        // Known values chosen to be unambiguous IEEE-754 representations.
        float px = 10.0f, py = 20.0f, pz = 30.0f;
        float pu = 0.125f, pv = 0.875f;

        byte[] fixture = BuildSkyBox(
            textureNames: ["tex"],
            verticesPerMesh: [[(px, py, pz, pu, pv)]],
            indicesPerMesh: [[]]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        SkyBoxVertex v = result.Meshes[0].Vertices[0];

        // spec: Docs/RE/formats/sky.md §A.4 — position.x f32 @ sub-offset 0x00: MED
        Assert.Equal(px, v.X);
        // spec: Docs/RE/formats/sky.md §A.4 — position.y f32 @ sub-offset 0x04: MED
        Assert.Equal(py, v.Y);
        // spec: Docs/RE/formats/sky.md §A.4 — position.z f32 @ sub-offset 0x08: MED
        Assert.Equal(pz, v.Z);
        // spec: Docs/RE/formats/sky.md §A.4 — uv.u f32 @ sub-offset 0x0C: MED
        Assert.Equal(pu, v.U);
        // spec: Docs/RE/formats/sky.md §A.4 — uv.v f32 @ sub-offset 0x10: MED
        Assert.Equal(pv, v.V);
    }

    /// <summary>
    /// Multiple vertices per mesh are all decoded in order.
    /// spec: Docs/RE/formats/sky.md §A.3 — vertex_count × 20-byte stride.
    /// </summary>
    [Fact]
    public void Parse_MultipleVertices_AllDecodedInOrder()
    {
        (float x, float y, float z, float u, float v)[] verts =
        [
            (1f, 2f, 3f, 0.1f, 0.2f),
            (4f, 5f, 6f, 0.3f, 0.4f),
            (7f, 8f, 9f, 0.5f, 0.6f),
        ];

        byte[] fixture = BuildSkyBox(
            textureNames: ["t"],
            verticesPerMesh: [verts],
            indicesPerMesh: [[]]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        Assert.Equal(3, result.Meshes[0].Vertices.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            SkyBoxVertex v = result.Meshes[0].Vertices[i];
            Assert.Equal(verts[i].x, v.X);
            Assert.Equal(verts[i].y, v.Y);
            Assert.Equal(verts[i].z, v.Z);
            Assert.Equal(verts[i].u, v.U);
            Assert.Equal(verts[i].v, v.V);
        }
    }

    // =========================================================================
    // Index decode tests
    // =========================================================================

    /// <summary>
    /// u16 indices are decoded in order and correct values are reproduced.
    /// spec: Docs/RE/formats/sky.md §A.5 — u16 index array: HIGH.
    /// </summary>
    [Fact]
    public void Parse_U16Indices_DecodedInOrder()
    {
        ushort[] expected = [0, 1, 2, 2, 3, 0];

        byte[] fixture = BuildSkyBox(
            textureNames: ["t"],
            verticesPerMesh:
            [
                [
                    (0f, 0f, 0f, 0f, 0f),
                    (1f, 0f, 0f, 0f, 0f),
                    (1f, 1f, 0f, 0f, 0f),
                    (0f, 1f, 0f, 0f, 0f),
                ]
            ],
            indicesPerMesh: [expected]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        Assert.Equal(expected.Length, result.Meshes[0].Indices.Length);
        for (int k = 0; k < expected.Length; k++)
            Assert.Equal(expected[k], result.Meshes[0].Indices[k]);
    }

    /// <summary>
    /// A u16 index value of 0xFFFF (65535) round-trips without truncation or sign extension.
    /// spec: Docs/RE/formats/sky.md §A.5 — u16 (not i16): HIGH.
    /// </summary>
    [Fact]
    public void Parse_U16Index_MaxValue_RoundTrips()
    {
        byte[] fixture = BuildSkyBox(
            textureNames: ["t"],
            verticesPerMesh: [[]],
            indicesPerMesh: [[0xFFFF]]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        Assert.Equal(0xFFFF, result.Meshes[0].Indices[0]);
    }

    // =========================================================================
    // Texture-name decode tests
    // =========================================================================

    /// <summary>
    /// Texture name is correctly trimmed at the null terminator within the 47-byte field.
    /// spec: Docs/RE/formats/sky.md §A.2 — char[47] fixed-width, null-terminated: HIGH.
    /// </summary>
    [Fact]
    public void Parse_TextureName_NullTerminatedWithinField()
    {
        byte[] fixture = BuildSkyBox(
            textureNames: ["sky_dome_01"],
            verticesPerMesh: [[]],
            indicesPerMesh: [[]]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        Assert.Equal("sky_dome_01", result.TextureNames[0]);
    }

    /// <summary>
    /// An empty (all-zero) texture name decodes as an empty string, not a null pointer.
    /// spec: Docs/RE/formats/sky.md §A.2 — char[47] fixed-width: HIGH.
    /// </summary>
    [Fact]
    public void Parse_TextureName_AllZeros_DecodesAsEmptyString()
    {
        byte[] fixture = BuildSkyBox(
            textureNames: [""],
            verticesPerMesh: [[]],
            indicesPerMesh: [[]]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        Assert.Equal(string.Empty, result.TextureNames[0]);
    }

    // =========================================================================
    // Section interleave tests (all vertex arrays then all index arrays)
    // =========================================================================

    /// <summary>
    /// Verifies that the two-pass read order (all vertex arrays, then all index arrays) is
    /// correct for a two-texture box — the second mesh's vertices must appear before the first
    /// mesh's indices in the byte stream.
    /// spec: Docs/RE/formats/sky.md §A.6 — all vertex arrays precede all index arrays.
    /// </summary>
    [Fact]
    public void Parse_TwoTextures_VertexArraysThenIndexArrays_CorrectSections()
    {
        (float, float, float, float, float)[] verts0 =
            [(100f, 0f, 0f, 0f, 0f), (101f, 0f, 0f, 0f, 0f)];
        (float, float, float, float, float)[] verts1 =
            [(200f, 0f, 0f, 0f, 0f)];
        ushort[] idx0 = [0, 1];
        ushort[] idx1 = [0];

        byte[] fixture = BuildSkyBox(
            textureNames: ["a", "b"],
            verticesPerMesh: [verts0, verts1],
            indicesPerMesh: [idx0, idx1]);

        SkyBoxData result = SkyBoxParser.Parse(fixture.AsSpan());

        // Mesh 0: 2 vertices, 2 indices.
        Assert.Equal(2, result.Meshes[0].Vertices.Length);
        Assert.Equal(100f, result.Meshes[0].Vertices[0].X);
        Assert.Equal(101f, result.Meshes[0].Vertices[1].X);
        Assert.Equal<ushort>(0, result.Meshes[0].Indices[0]);
        Assert.Equal<ushort>(1, result.Meshes[0].Indices[1]);

        // Mesh 1: 1 vertex, 1 index.
        Assert.Single(result.Meshes[1].Vertices);
        Assert.Equal(200f, result.Meshes[1].Vertices[0].X);
        Assert.Equal<ushort>(0, result.Meshes[1].Indices[0]);
    }

    // =========================================================================
    // Cap validation tests
    // =========================================================================

    /// <summary>
    /// A vertex_count exceeding the cap (300) raises <see cref="InvalidDataException"/>.
    /// spec: Docs/RE/formats/sky.md §A.3 — "Cap: 300 (0x12C)": HIGH.
    /// </summary>
    [Fact]
    public void Parse_VertexCount_ExceedsCap_ThrowsInvalidDataException()
    {
        // Build a fixture that declares vertex_count = 301 (one over the cap).
        // spec: §A.3 — cap 300.
        // Manually assemble: header 4 + name 47 + vertex_count field 4 (= 55 bytes minimum).
        int n = 1;
        // Header.
        byte[] fixture = new byte[4 + 47 + 4]; // texture_count + name + vertex_count
        WriteU32LE(fixture, 0, (uint)n); // texture_count = 1
        // texture_name[0]: leave as zeros (empty name, valid).
        WriteU32LE(fixture, 4 + 47, 301); // vertex_count = 301 (exceeds cap 300)
        // No vertex data follows; the parser should reject before reading any.

        // spec: Docs/RE/formats/sky.md §A.3 — parser must reject vertex_count > 300.
        Assert.Throws<InvalidDataException>(() => SkyBoxParser.Parse(fixture.AsSpan()));
    }

    /// <summary>
    /// An index_count exceeding the cap (900) raises <see cref="InvalidDataException"/>.
    /// spec: Docs/RE/formats/sky.md §A.5 — "Cap: 900 (0x384)": HIGH.
    /// </summary>
    [Fact]
    public void Parse_IndexCount_ExceedsCap_ThrowsInvalidDataException()
    {
        // Build a fixture with zero vertices (to pass vertex phase), then an index_count of 901.
        // spec: §A.5 — cap 900.
        byte[] fixture = new byte[4 + 47 + 4 + 4]; // header + name + vertex_count(0) + index_count
        WriteU32LE(fixture, 0, 1); // texture_count = 1
        // texture_name[0]: zeros.
        WriteU32LE(fixture, 4 + 47, 0); // vertex_count = 0
        WriteU32LE(fixture, 4 + 47 + 4, 901); // index_count = 901 (exceeds cap 900)
        // No index data follows; the parser should reject before reading any.

        // spec: Docs/RE/formats/sky.md §A.5 — parser must reject index_count > 900.
        Assert.Throws<InvalidDataException>(() => SkyBoxParser.Parse(fixture.AsSpan()));
    }

    // =========================================================================
    // Truncation / corrupt-input guard tests
    // =========================================================================

    /// <summary>
    /// A completely empty buffer raises <see cref="InvalidDataException"/>.
    /// spec: Docs/RE/formats/sky.md §A.1 — needs at least 4 bytes for header.
    /// </summary>
    [Fact]
    public void Parse_EmptyBuffer_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => SkyBoxParser.Parse(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>
    /// A buffer truncated mid-vertex-block raises <see cref="InvalidDataException"/> without
    /// reading past the end.
    /// spec: Docs/RE/formats/sky.md §A.3 — buffer length check before reading vertex data.
    /// </summary>
    [Fact]
    public void Parse_TruncatedVertexBlock_ThrowsInvalidDataException()
    {
        // Full fixture with 2 vertices, then truncate by 5 bytes to cut into the second vertex.
        byte[] full = BuildSkyBox(
            textureNames: ["t"],
            verticesPerMesh: [[(0f, 0f, 0f, 0f, 0f), (1f, 1f, 1f, 1f, 1f)]],
            indicesPerMesh: [[]]);

        byte[] truncated = full[..^5]; // remove 5 bytes from end, cutting mid-vertex

        Assert.Throws<InvalidDataException>(() => SkyBoxParser.Parse(truncated.AsSpan()));
    }

    /// <summary>
    /// A buffer truncated mid-index-block raises <see cref="InvalidDataException"/>.
    /// spec: Docs/RE/formats/sky.md §A.5 — buffer length check before reading index data.
    /// </summary>
    [Fact]
    public void Parse_TruncatedIndexBlock_ThrowsInvalidDataException()
    {
        // Full fixture with 3 indices, then truncate by 1 byte to cut the last u16.
        byte[] full = BuildSkyBox(
            textureNames: ["t"],
            verticesPerMesh: [[]],
            indicesPerMesh: [[0, 1, 2]]);

        byte[] truncated = full[..^1]; // remove last byte, cutting the final u16

        Assert.Throws<InvalidDataException>(() => SkyBoxParser.Parse(truncated.AsSpan()));
    }

    /// <summary>
    /// A buffer truncated in the texture-name block raises <see cref="InvalidDataException"/>.
    /// spec: Docs/RE/formats/sky.md §A.2 — buffer length check before reading name block.
    /// </summary>
    [Fact]
    public void Parse_TruncatedNameBlock_ThrowsInvalidDataException()
    {
        // texture_count = 2 but only 47 bytes of name data (= only one name, not two).
        byte[] fixture = new byte[4 + 47]; // header + one name
        WriteU32LE(fixture, 0, 2); // texture_count claims 2

        Assert.Throws<InvalidDataException>(() => SkyBoxParser.Parse(fixture.AsSpan()));
    }

    // =========================================================================
    // ReadOnlyMemory<byte> overload
    // =========================================================================

    /// <summary>
    /// The <see cref="SkyBoxParser.Parse(ReadOnlyMemory{byte})"/> overload produces the same
    /// result as the span overload on the same data.
    /// </summary>
    [Fact]
    public void Parse_Memory_OverloadMatchesSpanResult()
    {
        byte[] fixture = BuildSkyBox(
            textureNames: ["sky0"],
            verticesPerMesh: [[(5f, 6f, 7f, 0.1f, 0.9f)]],
            indicesPerMesh: [[0]]);

        SkyBoxData fromSpan = SkyBoxParser.Parse(fixture.AsSpan());
        SkyBoxData fromMemory = SkyBoxParser.Parse(new ReadOnlyMemory<byte>(fixture));

        Assert.Equal(fromSpan.TextureNames[0], fromMemory.TextureNames[0]);
        Assert.Equal(fromSpan.Meshes[0].Vertices[0].X, fromMemory.Meshes[0].Vertices[0].X);
        Assert.Equal(fromSpan.Meshes[0].Indices[0], fromMemory.Meshes[0].Indices[0]);
    }
}