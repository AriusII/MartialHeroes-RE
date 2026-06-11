using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="XobjParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh
/// </summary>
public sealed class XobjParserTests
{
    // -----------------------------------------------------------------------
    // Fixture builder
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal synthetic .xobj byte buffer.
    /// Layout: unused_token  num_triangles  (indices...)  num_vertices  (per-vertex 8 tokens...)
    /// spec: Docs/RE/formats/mesh.md §Read order.
    /// </summary>
    private static byte[] BuildXobj(
        uint unusedToken,
        (uint a, uint b, uint c)[] triangles,
        (float px, float py, float pz, float nx, float ny, float nz, float u, float v)[] vertices)
    {
        var sb = new StringBuilder();
        sb.Append(unusedToken);
        sb.Append(' ');
        sb.Append(triangles.Length);
        sb.Append(' ');
        foreach (var (a, b, c) in triangles)
        {
            sb.Append(a);
            sb.Append(' ');
            sb.Append(b);
            sb.Append(' ');
            sb.Append(c);
            sb.Append(' ');
        }

        sb.Append(vertices.Length);
        sb.Append(' ');
        foreach (var (px, py, pz, nx, ny, nz, u, v) in vertices)
        {
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0} {1} {2} {3} {4} {5} {6} {7} ", px, py, pz, nx, ny, nz, u, v);
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_TwoTriangles_CorrectIndexCount()
    {
        // spec: Docs/RE/formats/mesh.md §Index list — num_triangles × 3 indices: CONFIRMED.
        byte[] data = BuildXobj(
            unusedToken: 42,
            triangles: [(0, 1, 2), (0, 2, 3)],
            vertices:
            [
                (1f, 2f, 3f, 0f, 1f, 0f, 0.25f, 0.75f),
                (4f, 5f, 6f, 0f, 1f, 0f, 0.50f, 0.25f),
                (7f, 8f, 9f, 0f, 1f, 0f, 0.75f, 0.50f),
                (1f, 0f, 9f, 0f, 1f, 0f, 0.10f, 0.90f),
            ]);

        StaticMesh mesh = XobjParser.Parse(data.AsSpan());

        Assert.Equal(6, mesh.Indices.Length); // 2 triangles × 3
        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(4, mesh.Uvs.Length);
    }

    [Fact]
    public void Parse_VertexPositions_AreDecoded()
    {
        // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED.
        byte[] data = BuildXobj(
            unusedToken: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (10f, 20f, 30f, 0f, 1f, 0f, 0f, 0f),
                (11f, 21f, 31f, 0f, 1f, 0f, 0f, 0f),
                (12f, 22f, 32f, 0f, 1f, 0f, 0f, 0f),
            ]);

        StaticMesh mesh = XobjParser.Parse(data.AsSpan());

        Assert.Equal(new Vec3(10f, 20f, 30f), mesh.Positions[0]);
        Assert.Equal(new Vec3(11f, 21f, 31f), mesh.Positions[1]);
        Assert.Equal(new Vec3(12f, 22f, 32f), mesh.Positions[2]);
    }

    [Fact]
    public void Parse_UvVCoordinate_IsVFlipped()
    {
        // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v:
        //   "engine transforms it to 1.0 - tex_v in-memory". CONFIRMED.
        float vOnDisk = 0.3f;
        float expectedV = 1.0f - vOnDisk;

        byte[] data = BuildXobj(
            unusedToken: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (0f, 0f, 0f, 0f, 1f, 0f, 0.5f, vOnDisk),
                (1f, 0f, 0f, 0f, 1f, 0f, 0.0f, 0.0f),
                (0f, 1f, 0f, 0f, 1f, 0f, 0.0f, 0.0f),
            ]);

        StaticMesh mesh = XobjParser.Parse(data.AsSpan());

        Assert.Equal(0.5f, mesh.Uvs[0].X, precision: 6);
        Assert.Equal(expectedV, mesh.Uvs[0].Y, precision: 6);
    }

    [Fact]
    public void Parse_IndexTruncatedToU16()
    {
        // spec: Docs/RE/formats/mesh.md §Index list:
        //   "in-memory representation stores each index as a u16 (the parser truncates from the parsed u32)". CONFIRMED.
        // Use index value 65535 (max u16) to verify truncation.
        byte[] data = BuildXobj(
            unusedToken: 0,
            triangles: [(0u, 1u, 65535u)],
            vertices: Enumerable.Range(0, 65536)
                .Select(i => ((float)i, 0f, 0f, 0f, 1f, 0f, 0f, 0f))
                .ToArray());

        StaticMesh mesh = XobjParser.Parse(data.AsSpan());

        Assert.Equal((ushort)0, mesh.Indices[0]);
        Assert.Equal((ushort)1, mesh.Indices[1]);
        Assert.Equal((ushort)65535, mesh.Indices[2]);
    }

    [Fact]
    public void Parse_UnusedTokenIsDiscarded_NoEffect()
    {
        // spec: Docs/RE/formats/mesh.md §Preamble — unused_token: "Read and silently discarded." CONFIRMED.
        byte[] data1 = BuildXobj(unusedToken: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (1f, 2f, 3f, 0f, 1f, 0f, 0.1f, 0.2f),
                (4f, 5f, 6f, 0f, 1f, 0f, 0.3f, 0.4f),
                (7f, 8f, 9f, 0f, 1f, 0f, 0.5f, 0.6f),
            ]);
        byte[] data2 = BuildXobj(unusedToken: 999,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (1f, 2f, 3f, 0f, 1f, 0f, 0.1f, 0.2f),
                (4f, 5f, 6f, 0f, 1f, 0f, 0.3f, 0.4f),
                (7f, 8f, 9f, 0f, 1f, 0f, 0.5f, 0.6f),
            ]);

        StaticMesh m1 = XobjParser.Parse(data1.AsSpan());
        StaticMesh m2 = XobjParser.Parse(data2.AsSpan());

        Assert.Equal(m1.Positions[0], m2.Positions[0]);
        Assert.Equal(m1.Uvs[0], m2.Uvs[0]);
        Assert.Equal(m1.Indices[0], m2.Indices[0]);
    }

    [Fact]
    public void Parse_NormalsNotInOutput()
    {
        // spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z:
        //   "read then discarded; not kept in memory". CONFIRMED.
        // StaticMesh has no Normals property — verify by compile-time shape only.
        byte[] data = BuildXobj(
            unusedToken: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (1f, 0f, 0f, 99f, 98f, 97f, 0f, 0f),
                (0f, 1f, 0f, 88f, 87f, 86f, 0f, 0f),
                (0f, 0f, 1f, 77f, 76f, 75f, 0f, 0f),
            ]);

        StaticMesh mesh = XobjParser.Parse(data.AsSpan());

        // StaticMesh only has Positions, Uvs, Indices — no Normals field.
        Assert.Equal(3, mesh.Positions.Length);
    }

    [Fact]
    public void Parse_EmptyMesh_ZeroTriangles()
    {
        // Edge case: 0 triangles and 0 vertices (valid per spec structure).
        byte[] data = BuildXobj(unusedToken: 0, triangles: [], vertices: []);

        StaticMesh mesh = XobjParser.Parse(data.AsSpan());

        Assert.Empty(mesh.Indices);
        Assert.Empty(mesh.Positions);
    }

    [Fact]
    public void Parse_ReadOnlyMemory_Overload()
    {
        byte[] data = BuildXobj(
            unusedToken: 0,
            triangles: [(0, 1, 2)],
            vertices:
            [
                (5f, 6f, 7f, 0f, 1f, 0f, 0.1f, 0.9f),
                (8f, 9f, 0f, 0f, 1f, 0f, 0.2f, 0.8f),
                (1f, 2f, 3f, 0f, 1f, 0f, 0.3f, 0.7f),
            ]);

        StaticMesh mesh = XobjParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(3, mesh.Positions.Length);
    }
}