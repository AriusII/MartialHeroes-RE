using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="XobjParser.ParseAsMeshParticle"/>.
/// All fixtures are built in-memory — no real game files required.
///
/// Format under test: <c>.xobj</c> ASCII mesh files from <c>data/effect/xobj/</c>,
/// decoded into the 24-byte shared mesh table layout for mesh-particle emitters
/// (emitter_type == 1, resource_id &lt; 10000).
///
/// Layout (all CONFIRMED per spec):
///   Plain text, CRLF line endings, whitespace-tokenized.
///   Read order: slot_id (discard), face_count, face_count×3 indices,
///               vertex_count, vertex_count×8 tokens per vertex.
///   Per-vertex: pos_x, pos_y, pos_z (kept), norm_x, norm_y, norm_z (discarded),
///               tex_u (kept), tex_v → stored as 1.0 − tex_v (V-flip).
///   In-memory vertex: POSITION12 (3 × f32) + DIFFUSE4 (u32 = 0, uninitialised)
///                     + TEXCOORD8 (2 × f32) = 24 bytes.
///
/// spec: Docs/RE/formats/effects.md §A.11 — .xobj in data/effect/xobj/: CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §Format: .xobj — read order + 8 tokens per vertex: CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes: CONFIRMED.
/// </summary>
public sealed class XobjMeshParticleTests
{
    // ── Fixture builder ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal .xobj byte buffer in ASCII.
    /// spec: Docs/RE/formats/mesh.md §Preamble + §Index list + §Vertex count + §Vertex data rows.
    /// </summary>
    private static byte[] BuildXobj(
        uint slotId,
        (uint a, uint b, uint c)[] triangles,
        (float px, float py, float pz, float nx, float ny, float nz, float u, float v)[] vertices)
    {
        var sb = new StringBuilder();
        // slot_id (discard). spec: §Preamble — slot_id u32: CONFIRMED discard.
        sb.Append(slotId);
        sb.Append(' ');
        // face_count. spec: §Preamble — face_count u32: CONFIRMED.
        sb.Append(triangles.Length);
        sb.Append(' ');
        // indices. spec: §Index list — vertex_index[n]: CONFIRMED.
        foreach (var (a, b, c) in triangles)
        {
            sb.Append(a);
            sb.Append(' ');
            sb.Append(b);
            sb.Append(' ');
            sb.Append(c);
            sb.Append(' ');
        }

        // vertex_count. spec: §Vertex count: CONFIRMED.
        sb.Append(vertices.Length);
        sb.Append(' ');
        // vertex data: 8 tokens each. spec: §Vertex data rows — 8 tokens: CONFIRMED.
        foreach (var (px, py, pz, nx, ny, nz, u, v) in vertices)
        {
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0} {1} {2} {3} {4} {5} {6} {7} ", px, py, pz, nx, ny, nz, u, v);
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void ParseAsMeshParticle_OneTriangle_IndexAndVertexCount()
    {
        // spec: Docs/RE/formats/effects.md §A.11 — shared mesh table 24-byte stride: CONFIRMED.
        byte[] data = BuildXobj(
            slotId: 2,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (1f, 2f, 3f, 0f, 1f, 0f, 0.25f, 0.75f),
                (4f, 5f, 6f, 0f, 1f, 0f, 0.50f, 0.25f),
                (7f, 8f, 9f, 0f, 1f, 0f, 0.75f, 0.50f),
            ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        Assert.Equal(3, mesh.Indices.Length); // 1 triangle × 3
        Assert.Equal(3, mesh.Vertices.Length);
    }

    [Fact]
    public void ParseAsMeshParticle_VertexPosition_Decoded()
    {
        // pos_x, pos_y, pos_z are kept in the 24-byte in-memory vertex.
        // spec: Docs/RE/formats/mesh.md §In-memory vertex layout — pos_x @ +0, pos_y @ +4, pos_z @ +8: CONFIRMED.
        byte[] data = BuildXobj(
            slotId: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (10f, 20f, 30f, 0f, 1f, 0f, 0f, 0f),
                (11f, 21f, 31f, 0f, 1f, 0f, 0f, 0f),
                (12f, 22f, 32f, 0f, 1f, 0f, 0f, 0f),
            ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        Assert.Equal(10f, mesh.Vertices[0].PosX, precision: 5);
        Assert.Equal(20f, mesh.Vertices[0].PosY, precision: 5);
        Assert.Equal(30f, mesh.Vertices[0].PosZ, precision: 5);
    }

    [Fact]
    public void ParseAsMeshParticle_TexV_IsVFlipped()
    {
        // V-flip: in-memory tex_v = 1.0 − disk_tex_v.
        // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory": CONFIRMED.
        float vOnDisk = 0.3f;
        float expectedV = 1.0f - vOnDisk;

        byte[] data = BuildXobj(
            slotId: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (0f, 0f, 0f, 0f, 1f, 0f, 0.5f, vOnDisk),
                (1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f),
                (0f, 1f, 0f, 0f, 1f, 0f, 0f, 0f),
            ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        Assert.Equal(0.5f, mesh.Vertices[0].TexU, precision: 5);
        Assert.Equal(expectedV, mesh.Vertices[0].TexV, precision: 5);
    }

    [Fact]
    public void ParseAsMeshParticle_Diffuse_IsAlwaysZero()
    {
        // DIFFUSE4 at in-memory offset +12 is uninitialised (always 0 per spec).
        // spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
        byte[] data = BuildXobj(
            slotId: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (1f, 2f, 3f, 0f, 1f, 0f, 0.1f, 0.2f),
                (4f, 5f, 6f, 0f, 1f, 0f, 0.3f, 0.4f),
                (7f, 8f, 9f, 0f, 1f, 0f, 0.5f, 0.6f),
            ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        // All DIFFUSE4 fields must be 0.
        Assert.All(mesh.Vertices, v => Assert.Equal(0u, v.Diffuse));
    }

    [Fact]
    public void ParseAsMeshParticle_SlotIdIsDiscarded_NoEffect()
    {
        // slot_id is always discarded; different slot_id values must produce identical meshes.
        // spec: Docs/RE/formats/mesh.md §Preamble — slot_id u32: "Read and silently discarded": CONFIRMED.
        byte[] data1 = BuildXobj(0, [(0, 1, 2)],
        [
            (1f, 0f, 0f, 0f, 1f, 0f, 0.1f, 0.9f),
            (0f, 1f, 0f, 0f, 1f, 0f, 0.2f, 0.8f),
            (0f, 0f, 1f, 0f, 1f, 0f, 0.3f, 0.7f)
        ]);
        byte[] data2 = BuildXobj(999, [(0, 1, 2)],
        [
            (1f, 0f, 0f, 0f, 1f, 0f, 0.1f, 0.9f),
            (0f, 1f, 0f, 0f, 1f, 0f, 0.2f, 0.8f),
            (0f, 0f, 1f, 0f, 1f, 0f, 0.3f, 0.7f)
        ]);

        XobjMeshData m1 = XobjParser.ParseAsMeshParticle(data1.AsSpan());
        XobjMeshData m2 = XobjParser.ParseAsMeshParticle(data2.AsSpan());

        Assert.Equal(m1.Vertices[0].PosX, m2.Vertices[0].PosX, precision: 5);
        Assert.Equal(m1.Vertices[0].TexU, m2.Vertices[0].TexU, precision: 5);
        Assert.Equal(m1.Indices[0], m2.Indices[0]);
    }

    [Fact]
    public void ParseAsMeshParticle_NormalsNotInModel()
    {
        // Normals are read and discarded. XobjMeshData has no Normals field.
        // spec: Docs/RE/formats/effects.md §A.11 — normals not in the shared mesh table: CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z: "read then discarded": CONFIRMED.
        byte[] data = BuildXobj(0, [(0, 1, 2)],
        [
            (0f, 0f, 0f, 99f, 98f, 97f, 0f, 0f),
            (1f, 0f, 0f, 88f, 87f, 86f, 0f, 0f),
            (0f, 1f, 0f, 77f, 76f, 75f, 0f, 0f)
        ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        // XobjMeshData only has Vertices and Indices (no Normals).
        Assert.Equal(3, mesh.Vertices.Length);
        // The XobjVertex record has no normal fields — verified by compile-time shape.
    }

    [Fact]
    public void ParseAsMeshParticle_TwoTriangles_CorrectIndexCount()
    {
        // spec: Docs/RE/formats/mesh.md §Index list — face_count × 3: CONFIRMED.
        byte[] data = BuildXobj(
            slotId: 1,
            triangles: [(0, 1, 2), (0, 2, 3)],
            vertices:
            [
                (0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f),
                (1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f),
                (1f, 1f, 0f, 0f, 1f, 0f, 0f, 0f),
                (0f, 1f, 0f, 0f, 1f, 0f, 0f, 0f),
            ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        Assert.Equal(6, mesh.Indices.Length); // 2 triangles × 3
        Assert.Equal(4, mesh.Vertices.Length);
    }

    [Fact]
    public void ParseAsMeshParticle_EmptyMesh_ZeroVerticesAndIndices()
    {
        // Edge case: 0 triangles, 0 vertices.
        byte[] data = BuildXobj(0, [], []);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        Assert.Empty(mesh.Indices);
        Assert.Empty(mesh.Vertices);
    }

    [Fact]
    public void ParseAsMeshParticle_IndexTruncatedToU16()
    {
        // Indices stored as u16 (truncated from on-disk u32).
        // spec: Docs/RE/formats/mesh.md §Index list — "in-memory: u16": CONFIRMED.
        byte[] data = BuildXobj(
            slotId: 0,
            triangles: [(0u, 1u, 65535u)],
            vertices: Enumerable.Range(0, 65536)
                .Select(i => ((float)i, 0f, 0f, 0f, 1f, 0f, 0f, 0f))
                .ToArray());

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(data.AsSpan());

        Assert.Equal((ushort)65535, mesh.Indices[2]);
    }

    [Fact]
    public void ParseAsMeshParticle_ReadOnlyMemoryOverload_Works()
    {
        byte[] data = BuildXobj(
            slotId: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (5f, 6f, 7f, 0f, 1f, 0f, 0.1f, 0.9f),
                (8f, 9f, 0f, 0f, 1f, 0f, 0.2f, 0.8f),
                (1f, 2f, 3f, 0f, 1f, 0f, 0.3f, 0.7f),
            ]);

        XobjMeshData mesh = XobjParser.ParseAsMeshParticle(new ReadOnlyMemory<byte>(data));

        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal(5f, mesh.Vertices[0].PosX, precision: 5);
    }
}