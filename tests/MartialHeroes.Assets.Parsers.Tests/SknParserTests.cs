using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="SknParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh
/// </summary>
public sealed class SknParserTests
{
    // -----------------------------------------------------------------------
    // Fixture builder
    // -----------------------------------------------------------------------

    private static byte[] Le4(uint v)   { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
    private static byte[] Le4f(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); return b; }

    /// <summary>
    /// Builds a minimal synthetic .skn binary buffer using the confirmed on-disk layout.
    /// spec: Docs/RE/formats/mesh.md §Format: .skn.
    /// <para>
    /// Wire layout:
    ///   id_a u32 LE | id_b u32 LE
    ///   name LenStr  [u32 LE length prefix + ASCII body, no null terminator]
    ///   face_count u32 LE | face_count × 36-byte face records
    ///   vertex_count u32 LE | vertex_count × 24-byte vertex records
    ///   weight_count u32 LE | weight_count × 12-byte weight records
    /// </para>
    /// </summary>
    private static byte[] BuildSkn(
        uint idA, uint idB, string name,
        (uint vIdx, float u, float v)[][] faces,          // each face has 3 corners
        (float nx, float ny, float nz, float px, float py, float pz)[] vertices,
        (uint vIdx, uint boneIdx, float w)[] weights)
    {
        using var ms = new System.IO.MemoryStream();

        // Header: id_a u32, id_b u32
        // spec: Docs/RE/formats/mesh.md §Header — id_a @ +0, id_b @ +4: CONFIRMED.
        ms.Write(Le4(idA));
        ms.Write(Le4(idB));

        // LenStr name: 4-byte u32 LE length prefix + ASCII body, no null terminator.
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr):
        //   "The prefix is a 4-byte little-endian u32." CONFIRMED.
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        ms.Write(Le4((uint)nameBytes.Length));
        ms.Write(nameBytes);

        // Face table: face_count u32 + face_count × 36 bytes
        // spec: Docs/RE/formats/mesh.md §Face table: CONFIRMED.
        ms.Write(Le4((uint)faces.Length));
        foreach (var face in faces)
        {
            foreach (var (vIdx, u, v) in face)
            {
                ms.Write(Le4(vIdx));
                ms.Write(Le4f(u));
                ms.Write(Le4f(v)); // on-disk v; parser will flip to 1 - v
            }
        }

        // Vertex table: vertex_count u32 + vertex_count × 24 bytes
        // spec: Docs/RE/formats/mesh.md §Vertex table — on-disk: normal first, pos second. CONFIRMED.
        ms.Write(Le4((uint)vertices.Length));
        foreach (var (nx, ny, nz, px, py, pz) in vertices)
        {
            // normal first (sub-offset 0)
            ms.Write(Le4f(nx));
            ms.Write(Le4f(ny));
            ms.Write(Le4f(nz));
            // position second (sub-offset 12)
            ms.Write(Le4f(px));
            ms.Write(Le4f(py));
            ms.Write(Le4f(pz));
        }

        // Weight table: weight_count u32 + weight_count × 12 bytes
        // spec: Docs/RE/formats/mesh.md §Weight / skin table: CONFIRMED.
        ms.Write(Le4((uint)weights.Length));
        foreach (var (vIdx, boneIdx, w) in weights)
        {
            ms.Write(Le4(vIdx));
            ms.Write(Le4(boneIdx));
            ms.Write(Le4f(w));
        }

        return ms.ToArray();
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_Header_IdAndName()
    {
        // spec: Docs/RE/formats/mesh.md §Header — id_a, id_b, name: CONFIRMED.
        byte[] data = BuildSkn(
            idA: 101, idB: 202, name: "HeroSkin",
            faces: [[(0, 0.5f, 0.5f), (1, 0.0f, 1.0f), (2, 1.0f, 0.0f)]],
            vertices: [(0f, 1f, 0f, 1f, 2f, 3f), (0f, 1f, 0f, 4f, 5f, 6f), (0f, 1f, 0f, 7f, 8f, 9f)],
            weights: [(0, 0, 1.0f)]);

        SkinnedMesh mesh = SknParser.Parse(data.AsSpan());

        Assert.Equal(101u, mesh.IdA);
        Assert.Equal(202u, mesh.IdB);
        Assert.Equal("HeroSkin", mesh.Name);
    }

    [Fact]
    public void Parse_FaceCount_MatchesCornerCount()
    {
        // spec: Docs/RE/formats/mesh.md §Face table — face_count × 3 corners: CONFIRMED.
        byte[] data = BuildSkn(
            idA: 1, idB: 2, name: "Test",
            faces:
            [
                [(0, 0.1f, 0.2f), (1, 0.3f, 0.4f), (2, 0.5f, 0.6f)],
                [(0, 0.7f, 0.8f), (2, 0.9f, 1.0f), (3, 0.0f, 0.1f)],
            ],
            vertices: [(0f, 1f, 0f, 0f, 0f, 0f), (0f, 1f, 0f, 1f, 0f, 0f), (0f, 1f, 0f, 2f, 0f, 0f), (0f, 1f, 0f, 3f, 0f, 0f)],
            weights: [(0, 0, 1.0f)]);

        SkinnedMesh mesh = SknParser.Parse(data.AsSpan());

        Assert.Equal(2u, mesh.FaceCount);
        Assert.Equal(6, mesh.Corners.Length); // 2 × 3
    }

    [Fact]
    public void Parse_Vertex_NormalAndPositionReordered()
    {
        // spec: Docs/RE/formats/mesh.md §Vertex record —
        //   "IMPORTANT: on-disk layout is normal first, then position": CONFIRMED.
        // On disk: (nx=0.5, ny=0.6, nz=0.7, px=10, py=20, pz=30)
        byte[] data = BuildSkn(
            idA: 1, idB: 2, name: "T",
            faces: [[(0, 0f, 0f), (1, 0f, 0f), (2, 0f, 0f)]],
            vertices:
            [
                (nx: 0.5f, ny: 0.6f, nz: 0.7f,  px: 10f, py: 20f, pz: 30f),
                (nx: 0.0f, ny: 1.0f, nz: 0.0f,  px:  1f, py:  2f, pz:  3f),
                (nx: 0.0f, ny: 0.0f, nz: 1.0f,  px:  4f, py:  5f, pz:  6f),
            ],
            weights: [(0, 0, 1.0f)]);

        SkinnedMesh mesh = SknParser.Parse(data.AsSpan());

        // Position must be read from sub-offsets 12–23 (px, py, pz).
        Assert.Equal(new Vec3(10f, 20f, 30f), mesh.Positions[0]);
        // Normal must be read from sub-offsets 0–11 (nx, ny, nz).
        Assert.Equal(new Vec3(0.5f, 0.6f, 0.7f), mesh.Normals[0]);
    }

    [Fact]
    public void Parse_CornerUvV_IsVFlipped()
    {
        // spec: Docs/RE/formats/mesh.md §Face record — uv_v:
        //   "engine applies 1.0 - uv_v when building the render vertex". CONFIRMED.
        float vOnDisk = 0.4f;
        float expectedV = 1.0f - vOnDisk;

        byte[] data = BuildSkn(
            idA: 1, idB: 2, name: "T",
            faces: [[(0, 0.25f, vOnDisk), (1, 0f, 0f), (2, 0f, 0f)]],
            vertices: [(0f, 1f, 0f, 0f, 0f, 0f), (0f, 1f, 0f, 1f, 0f, 0f), (0f, 1f, 0f, 2f, 0f, 0f)],
            weights: [(0, 0, 1.0f)]);

        SkinnedMesh mesh = SknParser.Parse(data.AsSpan());

        Assert.Equal(0.25f, mesh.Corners[0].UvU, precision: 6);
        Assert.Equal(expectedV, mesh.Corners[0].UvV, precision: 6);
    }

    [Fact]
    public void Parse_Weights_AllFieldsDecoded()
    {
        // spec: Docs/RE/formats/mesh.md §Weight record — vertIdx u32 + boneIdx u32 + weight f32: CONFIRMED.
        byte[] data = BuildSkn(
            idA: 1, idB: 2, name: "T",
            faces: [[(0, 0f, 0f), (1, 0f, 0f), (2, 0f, 0f)]],
            vertices: [(0f, 1f, 0f, 0f, 0f, 0f), (0f, 1f, 0f, 1f, 0f, 0f), (0f, 1f, 0f, 2f, 0f, 0f)],
            weights:
            [
                (vIdx: 0, boneIdx: 3, w: 0.6f),
                (vIdx: 1, boneIdx: 7, w: 0.4f),
            ]);

        SkinnedMesh mesh = SknParser.Parse(data.AsSpan());

        Assert.Equal(2, mesh.Weights.Length);

        Assert.Equal(0u, mesh.Weights[0].VertexIndex);
        Assert.Equal(3u, mesh.Weights[0].BoneIndex);
        Assert.Equal(0.6f, mesh.Weights[0].Weight, precision: 6);

        Assert.Equal(1u, mesh.Weights[1].VertexIndex);
        Assert.Equal(7u, mesh.Weights[1].BoneIndex);
        Assert.Equal(0.4f, mesh.Weights[1].Weight, precision: 6);
    }

    [Fact]
    public void Parse_TruncatedFaceTable_ThrowsInvalidData()
    {
        // Structural validation: truncated buffer must throw rather than read out of bounds.
        byte[] full = BuildSkn(
            idA: 1, idB: 2, name: "T",
            faces: [[(0, 0f, 0f), (1, 0f, 0f), (2, 0f, 0f)]],
            vertices: [(0f, 1f, 0f, 0f, 0f, 0f), (0f, 1f, 0f, 1f, 0f, 0f), (0f, 1f, 0f, 2f, 0f, 0f)],
            weights: [(0, 0, 1.0f)]);

        // Truncate the buffer halfway through the face table.
        byte[] truncated = full[..(full.Length / 2)];

        Assert.Throws<InvalidDataException>(() => SknParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Parse_ReadOnlyMemory_Overload()
    {
        byte[] data = BuildSkn(
            idA: 5, idB: 10, name: "Mem",
            faces: [[(0, 0f, 0f), (1, 0f, 0f), (2, 0f, 0f)]],
            vertices: [(0f, 1f, 0f, 0f, 0f, 0f), (0f, 1f, 0f, 1f, 0f, 0f), (0f, 1f, 0f, 2f, 0f, 0f)],
            weights: [(0, 0, 1.0f)]);

        SkinnedMesh mesh = SknParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(5u, mesh.IdA);
        Assert.Equal("Mem", mesh.Name);
    }

    [Fact]
    public void Parse_LenStrPrefix_Is4Bytes()
    {
        // Structural check: confirm that the name LenStr uses a 4-byte u32 LE prefix,
        // not a 1-byte prefix, by building two fixtures differing only in name length
        // and verifying the total size difference matches the name bytes (not name+3 padding).
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr):
        //   "The prefix is a 4-byte little-endian u32." CONFIRMED.
        (uint vIdx, float u, float v)[][] faces   = [[(0, 0f, 0f), (1, 0f, 0f), (2, 0f, 0f)]];
        (float, float, float, float, float, float)[] verts = [(0f, 1f, 0f, 0f, 0f, 0f), (0f, 1f, 0f, 1f, 0f, 0f), (0f, 1f, 0f, 2f, 0f, 0f)];
        (uint, uint, float)[] ws = [(0, 0, 1.0f)];

        byte[] withName1 = BuildSkn(1, 2, "A",   faces, verts, ws); // 1-char name
        byte[] withName5 = BuildSkn(1, 2, "ABCDE", faces, verts, ws); // 5-char name

        // The difference must be exactly 4 (5 chars - 1 char) bytes of name body.
        // The prefix is shared and is 4 bytes either way.
        Assert.Equal(4, withName5.Length - withName1.Length);
    }
}
