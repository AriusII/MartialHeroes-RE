using System.Buffers.Text;
using System.Text;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

public static class XobjParser
{
    private const uint XobjVertexDefaultDiffuse = 0xFF000000u;

    public static StaticMesh Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static StaticMesh Parse(ReadOnlySpan<byte> data)
    {
        ParseCore(data, out var positions, out var uvs, out var indices);

        return new StaticMesh
        {
            Positions = positions,
            Uvs = uvs,
            Indices = indices
        };
    }

    public static XobjMeshData ParseAsMeshParticle(ReadOnlyMemory<byte> data)
    {
        return ParseAsMeshParticle(data.Span);
    }

    public static XobjMeshData ParseAsMeshParticle(ReadOnlySpan<byte> data)
    {
        ParseCore(data, out var positions, out var uvs, out var indices);

        var vertices = new XobjVertex[positions.Length];
        for (var v = 0; v < positions.Length; v++)
        {
            var p = positions[v];
            var uv = uvs[v];
            vertices[v] = new XobjVertex(p.X, p.Y, p.Z, XobjVertexDefaultDiffuse, uv.X, uv.Y);
        }

        return new XobjMeshData { Indices = indices, Vertices = vertices };
    }

    private static void ParseCore(
        ReadOnlySpan<byte> data,
        out Vec3[] positions,
        out Vec2[] uvs,
        out ushort[] indices)
    {
        var bytePos = 0;
        var tokenIndex = 0;

        _ = NextU32(data, ref bytePos, ref tokenIndex, "marker");

        var numTriangles = NextU32(data, ref bytePos, ref tokenIndex, "tri_count");
        var indexCount = checked((int)(numTriangles * 3));

        indices = new ushort[indexCount];
        for (var i = 0; i < indexCount; i++)
            indices[i] = (ushort)(NextU32(data, ref bytePos, ref tokenIndex, "index") & 0xFFFF);

        var numVertices = NextU32(data, ref bytePos, ref tokenIndex, "vert_count");
        var vertexCount = checked((int)numVertices);

        positions = new Vec3[vertexCount];
        uvs = new Vec2[vertexCount];

        for (var v = 0; v < vertexCount; v++)
        {
            var posX = NextF32(data, ref bytePos, ref tokenIndex, "vertex.position.x");
            var posY = NextF32(data, ref bytePos, ref tokenIndex, "vertex.position.y");
            var posZ = NextF32(data, ref bytePos, ref tokenIndex, "vertex.position.z");

            _ = NextF32(data, ref bytePos, ref tokenIndex, "vertex.normal.x");
            _ = NextF32(data, ref bytePos, ref tokenIndex, "vertex.normal.y");
            _ = NextF32(data, ref bytePos, ref tokenIndex, "vertex.normal.z");

            var texU = NextF32(data, ref bytePos, ref tokenIndex, "vertex.uv.u");
            var texV = 1.0f - NextF32(data, ref bytePos, ref tokenIndex, "vertex.uv.v");

            positions[v] = new Vec3(posX, posY, posZ);
            uvs[v] = new Vec2(texU, texV);
        }
    }

    private static bool IsWhitespace(byte b)
    {
        return b == 0x20 || (b >= 0x09 && b <= 0x0D);
    }

    private static bool TryNextToken(ReadOnlySpan<byte> data, ref int bytePos, out ReadOnlySpan<byte> token)
    {
        while (bytePos < data.Length && IsWhitespace(data[bytePos]))
            bytePos++;

        if (bytePos >= data.Length)
        {
            token = default;
            return false;
        }

        var start = bytePos;
        while (bytePos < data.Length && !IsWhitespace(data[bytePos]))
            bytePos++;

        token = data[start..bytePos];
        return true;
    }

    private static uint NextU32(ReadOnlySpan<byte> data, ref int bytePos, ref int tokenIndex, string fieldName)
    {
        if (!TryNextToken(data, ref bytePos, out var token))
            throw new InvalidDataException(
                $".xobj parse error: token under-run reading '{fieldName}' at token position {tokenIndex}.");

        var position = tokenIndex++;
        if (!Utf8Parser.TryParse(token, out uint value, out var consumed) || consumed != token.Length)
            throw new InvalidDataException(
                $".xobj parse error: expected unsigned integer for '{fieldName}', got " +
                $"\"{Encoding.ASCII.GetString(token)}\" at token position {position}.");

        return value;
    }

    private static float NextF32(ReadOnlySpan<byte> data, ref int bytePos, ref int tokenIndex, string fieldName)
    {
        if (!TryNextToken(data, ref bytePos, out var token))
            throw new InvalidDataException(
                $".xobj parse error: token under-run reading '{fieldName}' at token position {tokenIndex}.");

        var position = tokenIndex++;
        if (!Utf8Parser.TryParse(token, out float value, out var consumed) || consumed != token.Length)
            throw new InvalidDataException(
                $".xobj parse error: expected float for '{fieldName}', got " +
                $"\"{Encoding.ASCII.GetString(token)}\" at token position {position}.");

        return value;
    }
}