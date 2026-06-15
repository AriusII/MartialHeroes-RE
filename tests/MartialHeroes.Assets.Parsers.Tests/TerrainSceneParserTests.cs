using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="TerrainSceneParser"/> (.bud files).
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/terrain_scene.md
/// </summary>
public sealed class TerrainSceneParserTests
{
    // ─── Fixture helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal .bud byte buffer containing <paramref name="objectCount"/> objects.
    /// Each object has exactly <paramref name="vertexCount"/> vertices and <paramref name="indexCount"/> indices.
    /// The vertex data is filled with zero floats; indices are filled with zero ushorts.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32: CONFIRMED.
    /// spec: Docs/RE/formats/terrain_scene.md §Object header (9 bytes): CONFIRMED.
    /// spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
    /// spec: Docs/RE/formats/terrain_scene.md §Index array — u16 triangle list: CONFIRMED.
    /// </remarks>
    private static byte[] BuildBud(int objectCount, uint vertexCount = 1u, uint indexCount = 3u)
    {
        // File layout:
        //   objectCount u32le                              @ 0
        //   Per object:
        //     type_byte u8                                 @ obj+0
        //     tex_id u32le                                 @ obj+1
        //     vertex_count u32le                           @ obj+5
        //     vertex_count × 32 bytes (zero-filled)
        //     index_count u32le
        //     index_count × 2 bytes (zero-filled)
        // spec: Docs/RE/formats/terrain_scene.md §File-level header and §Object header: CONFIRMED.
        int perObjectSize = 9 + (int)vertexCount * 32 + 4 + (int)indexCount * 2;
        int totalSize = 4 + objectCount * perObjectSize;
        byte[] buf = new byte[totalSize];

        int pos = 0;

        // objectCount u32le.
        // spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32 @ 0x00: CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), (uint)objectCount);
        pos += 4;

        for (int i = 0; i < objectCount; i++)
        {
            // type_byte u8 @ object+0x00.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — type_byte u8 @ +0x00: PARTIAL.
            buf[pos] = (byte)(i % 3); // rotate 0/1/2 to exercise variable type_byte
            pos += 1;

            // tex_id u32le @ object+0x01.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id u32 @ +0x01: PARTIAL.
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), (uint)(100 + i));
            pos += 4;

            // vertex_count u32le @ object+0x05.
            // spec: Docs/RE/formats/terrain_scene.md §3.2.1 vertex_count u32 @ +0x05: CONFIRMED.
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), vertexCount);
            pos += 4;

            // vertex data: vertexCount × 32 bytes (all zero — positions/normals/UVs are all 0.0f).
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
            pos += (int)vertexCount * 32;

            // index_count u32le.
            // spec: Docs/RE/formats/terrain_scene.md §Index header — index_count u32: CONFIRMED.
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), indexCount);
            pos += 4;

            // index data: indexCount × 2 bytes (all zero).
            // spec: Docs/RE/formats/terrain_scene.md §Index array — u16 triangle list: CONFIRMED.
            pos += (int)indexCount * 2;
        }

        return buf;
    }

    // ─── Basic parsing tests ──────────────────────────────────────────────────

    [Fact]
    public void Bud_Parse_ZeroObjects_EmptyScene()
    {
        // spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32: CONFIRMED.
        byte[] data = BuildBud(objectCount: 0);
        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());
        Assert.Empty(scene.Objects);
    }

    [Fact]
    public void Bud_Parse_SingleObject_Counts()
    {
        // A single object with 6 vertices and 9 indices.
        // spec: Docs/RE/formats/terrain_scene.md §Object header: CONFIRMED.
        byte[] data = BuildBud(objectCount: 1, vertexCount: 6u, indexCount: 9u);
        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Single(scene.Objects);
        Assert.Equal(6, scene.Objects[0].Vertices.Length);
        Assert.Equal(9, scene.Objects[0].Indices.Length);
    }

    [Fact]
    public void Bud_Parse_MultipleObjects_AllDecoded()
    {
        // spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32: CONFIRMED.
        byte[] data = BuildBud(objectCount: 3, vertexCount: 4u, indexCount: 6u);
        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Equal(3, scene.Objects.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(4, scene.Objects[i].Vertices.Length);
            Assert.Equal(6, scene.Objects[i].Indices.Length);
        }
    }

    [Fact]
    public void Bud_Parse_TexId_RoundTrip()
    {
        // tex_id written as 100 + i per object; verify decode.
        // spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id u32 @ +0x01: PARTIAL.
        byte[] data = BuildBud(objectCount: 2, vertexCount: 1u, indexCount: 0u);
        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Equal(100u, scene.Objects[0].TexId);
        Assert.Equal(101u, scene.Objects[1].TexId);
    }

    [Fact]
    public void Bud_Parse_TypeByte_VariableValues()
    {
        // type_byte cycles 0/1/2 per object in the fixture.
        // spec: Docs/RE/formats/terrain_scene.md §Object header — type_byte u8 @ +0x00: PARTIAL.
        // type_byte ∈ {0,1,2} is CONFIRMED-variable; no consumer branches on it.
        byte[] data = BuildBud(objectCount: 3, vertexCount: 1u, indexCount: 0u);
        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Equal((byte)0, scene.Objects[0].TypeByte);
        Assert.Equal((byte)1, scene.Objects[1].TypeByte);
        Assert.Equal((byte)2, scene.Objects[2].TypeByte);
    }

    // ─── Vertex layout test ───────────────────────────────────────────────────

    [Fact]
    public void Bud_Parse_VertexFields_RoundTrip()
    {
        // Write a single vertex with known float values; verify all 8 fields decode correctly.
        // spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
        //   pos_x @ +0x00, pos_y @ +0x04, pos_z @ +0x08: CONFIRMED.
        //   normal_x @ +0x0C, normal_y @ +0x10, normal_z @ +0x14: CONFIRMED.
        //   uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
        byte[] data = BuildBud(objectCount: 1, vertexCount: 1u, indexCount: 0u);

        // The first vertex is at: 4 (objectCount) + 1 (type_byte) + 4 (tex_id) + 4 (vertex_count) = offset 13.
        // spec: Docs/RE/formats/terrain_scene.md §File-level header and §Object header: CONFIRMED.
        int vertexOffset = 4 + 1 + 4 + 4; // = 13

        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 0, 4), 1.0f); // pos_x
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 4, 4), 2.0f); // pos_y
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 8, 4), 3.0f); // pos_z
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 12, 4), 0.5f); // normal_x
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 16, 4), 0.75f); // normal_y
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 20, 4), 0.25f); // normal_z
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 24, 4), 0.1f); // uv_u
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(vertexOffset + 28, 4), 0.9f); // uv_v

        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());
        BudVertex v = scene.Objects[0].Vertices[0];

        Assert.Equal(1.0f, v.PosX, precision: 5);
        Assert.Equal(2.0f, v.PosY, precision: 5);
        Assert.Equal(3.0f, v.PosZ, precision: 5);
        Assert.Equal(0.5f, v.NormalX, precision: 5);
        Assert.Equal(0.75f, v.NormalY, precision: 5);
        Assert.Equal(0.25f, v.NormalZ, precision: 5);
        Assert.Equal(0.1f, v.UvU, precision: 5);
        Assert.Equal(0.9f, v.UvV, precision: 5);
    }

    // ─── vertex_count warn-and-continue (the campaign correction) ─────────────

    [Fact]
    public void Bud_Parse_VertexCountAbove3072_DoesNotThrow()
    {
        // CAMPAIGN VFS-MASTERY correction: the legacy loader reads the FULL vertex_count
        // even when it exceeds 3072. Its cap check is log-only and runs AFTER the full read.
        // Four real VFS files exceed 3072; the parser MUST NOT throw on them.
        // spec: Docs/RE/formats/terrain_scene.md §3.2.1 vertex_count — warn-and-continue: CONFIRMED (loader-resolved).
        // spec: Docs/RE/formats/terrain_scene.md §9 implementor checklist point 2.
        const uint bigVertexCount = 3073u; // one past the legacy warn threshold
        byte[] data = BuildBud(objectCount: 1, vertexCount: bigVertexCount, indexCount: 3u);

        // Must NOT throw InvalidDataException — warn-and-continue is the correct behaviour.
        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Single(scene.Objects);
        // All vertices decoded — the FULL count was read, not clamped.
        Assert.Equal((int)bigVertexCount, scene.Objects[0].Vertices.Length);
    }

    [Fact]
    public void Bud_Parse_VertexCountFarAbove3072_DoesNotThrow()
    {
        // A larger exceedance to ensure no hidden clamping path exists.
        // spec: Docs/RE/formats/terrain_scene.md §vertex_count — "log-only and runs AFTER the full-count read": CONFIRMED.
        const uint bigVertexCount = 5000u;
        byte[] data = BuildBud(objectCount: 1, vertexCount: bigVertexCount, indexCount: 0u);

        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Equal((int)bigVertexCount, scene.Objects[0].Vertices.Length);
    }

    [Fact]
    public void Bud_Parse_VertexCountExactly3072_DoesNotThrow()
    {
        // Exactly at the threshold is also valid — only above it gets the log message.
        // spec: Docs/RE/formats/terrain_scene.md §vertex_count — VertexCountWarnThreshold = 3072: CONFIRMED.
        const uint threshold = 3072u;
        byte[] data = BuildBud(objectCount: 1, vertexCount: threshold, indexCount: 0u);

        BudScene scene = TerrainSceneParser.Parse(data.AsSpan());

        Assert.Equal((int)threshold, scene.Objects[0].Vertices.Length);
    }

    // ─── Truncation / corruption tests ────────────────────────────────────────

    [Fact]
    public void Bud_Parse_TruncatedAtObjectHeader_ThrowsInvalidData()
    {
        // Buffer ends mid-object header.
        // spec: Docs/RE/formats/terrain_scene.md §Object header (9 bytes): CONFIRMED.
        byte[] data = BuildBud(objectCount: 1, vertexCount: 1u, indexCount: 0u);
        byte[] truncated = data[..(4 + 5)]; // objectCount(4) + partial header(5 < 9)

        Assert.Throws<InvalidDataException>(() => TerrainSceneParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Bud_Parse_TruncatedAtVertexData_ThrowsInvalidData()
    {
        // Buffer ends mid-vertex array.
        // spec: Docs/RE/formats/terrain_scene.md §Vertex array: CONFIRMED.
        byte[] data = BuildBud(objectCount: 1, vertexCount: 10u, indexCount: 0u);
        byte[] truncated = data[..(4 + 9 + 5)]; // objectCount(4) + full header(9) + 5 of 320 vertex bytes

        Assert.Throws<InvalidDataException>(() => TerrainSceneParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Bud_Parse_TruncatedAtIndexData_ThrowsInvalidData()
    {
        // Buffer ends mid-index array.
        // spec: Docs/RE/formats/terrain_scene.md §Index array — u16 triangle list: CONFIRMED.
        byte[] data = BuildBud(objectCount: 1, vertexCount: 1u, indexCount: 6u);
        byte[] truncated = data[..(data.Length - 4)]; // cut last 4 of 12 index bytes

        Assert.Throws<InvalidDataException>(() => TerrainSceneParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Bud_Parse_EmptyBuffer_ThrowsInvalidData()
    {
        // Zero-byte buffer cannot even hold objectCount.
        // spec: Docs/RE/formats/terrain_scene.md §File-level header: CONFIRMED.
        Assert.Throws<InvalidDataException>(() => TerrainSceneParser.Parse(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Bud_Parse_ObjectCountBeyondBuffer_ThrowsInvalidData()
    {
        // Declares 99 objects but only has one.
        // spec: Docs/RE/formats/terrain_scene.md — "declared counts vs. buffer length": CONFIRMED.
        byte[] single = BuildBud(objectCount: 1, vertexCount: 1u, indexCount: 0u);
        // Overwrite objectCount with 99.
        BinaryPrimitives.WriteUInt32LittleEndian(single.AsSpan(0, 4), 99u);

        Assert.Throws<InvalidDataException>(() => TerrainSceneParser.Parse(single.AsSpan()));
    }
}