using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

public static class XobjParser
{
    public static StaticMesh Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static StaticMesh Parse(ReadOnlySpan<byte> data)
    {
        var text = Encoding.ASCII.GetString(data);

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var pos = 0;

        _ = NextU32(tokens, ref pos, "marker");

        var numTriangles = NextU32(tokens, ref pos, "tri_count");

        var indexCount = checked((int)(numTriangles * 3));

        if (pos + indexCount + 1 > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: index block truncated — need {indexCount} index tokens + vert_count, " +
                $"but only {tokens.Length - pos} tokens remain. " +
                "spec: Docs/RE/formats/xobj.md §Read order step 3-4.");

        var indices = new ushort[indexCount];
        for (var i = 0; i < indexCount; i++)
            indices[i] = (ushort)(NextU32Unchecked(tokens, ref pos) & 0xFFFF);

        var numVertices = NextU32(tokens, ref pos, "vert_count");

        var positions = new Vec3[numVertices];
        var uvs = new Vec2[numVertices];

        var tokensNeeded = pos + checked((int)numVertices * 8);
        if (tokensNeeded > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: vertex block truncated — need {numVertices} vertices × 8 tokens " +
                $"({tokensNeeded} total), but only {tokens.Length} tokens available. " +
                "spec: Docs/RE/formats/xobj.md §Per-vertex line.");

        for (uint v = 0; v < numVertices; v++)
        {
            var posX = NextF32Unchecked(tokens, ref pos);
            var posY = NextF32Unchecked(tokens, ref pos);
            var posZ = NextF32Unchecked(tokens, ref pos);

            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);

            var texU = NextF32Unchecked(tokens, ref pos);
            var texV = 1.0f - NextF32Unchecked(tokens, ref pos);

            positions[v] = new Vec3(posX, posY, posZ);
            uvs[v] = new Vec2(texU, texV);
        }

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
        var text = Encoding.ASCII.GetString(data);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var pos = 0;

        _ = NextU32(tokens, ref pos, "marker");

        var numTriangles = NextU32(tokens, ref pos, "tri_count");

        var indexCount = checked((int)(numTriangles * 3));

        if (pos + indexCount + 1 > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: index block truncated — need {indexCount} index tokens + vert_count, " +
                $"but only {tokens.Length - pos} tokens remain. " +
                "spec: Docs/RE/formats/xobj.md §Read order step 3-4.");

        var indices = new ushort[indexCount];
        for (var i = 0; i < indexCount; i++)
            indices[i] = (ushort)(NextU32Unchecked(tokens, ref pos) & 0xFFFF);

        var numVertices = NextU32(tokens, ref pos, "vert_count");

        var vertices = new XobjVertex[numVertices];

        var vertexTokensNeeded = pos + checked((int)numVertices * 8);
        if (vertexTokensNeeded > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: vertex block truncated — need {numVertices} vertices × 8 tokens " +
                $"({vertexTokensNeeded} total), but only {tokens.Length} tokens available. " +
                "spec: Docs/RE/formats/xobj.md §Per-vertex line.");

        for (uint v = 0; v < numVertices; v++)
        {
            var px = NextF32Unchecked(tokens, ref pos);
            var py = NextF32Unchecked(tokens, ref pos);
            var pz = NextF32Unchecked(tokens, ref pos);

            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);

            var tu = NextF32Unchecked(tokens, ref pos);
            var tvMem = 1.0f - NextF32Unchecked(tokens, ref pos);

            const uint diffuse = 0xFF000000u;

            vertices[v] = new XobjVertex(px, py, pz, diffuse, tu, tvMem);
        }

        return new XobjMeshData { Indices = indices, Vertices = vertices };
    }


    private static uint NextU32(string[] tokens, ref int pos, string fieldName)
    {
        if (pos >= tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: token under-run reading '{fieldName}' " +
                $"(expected token at position {pos}, file has {tokens.Length} tokens total).");

        var token = tokens[pos++];
        if (!uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $".xobj parse error: expected unsigned integer for '{fieldName}', " +
                $"got \"{token}\" at token position {pos - 1}.");

        return value;
    }

    private static uint NextU32Unchecked(string[] tokens, ref int pos)
    {
        var token = tokens[pos++];
        if (!uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $".xobj parse error: expected unsigned integer, got \"{token}\" at token position {pos - 1}. " +
                "spec: Docs/RE/formats/mesh.md §Index list.");
        return value;
    }

    private static float NextF32Unchecked(string[] tokens, ref int pos)
    {
        var token = tokens[pos++];
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $".xobj parse error: expected float, got \"{token}\" at token position {pos - 1}. " +
                "spec: Docs/RE/formats/mesh.md §Vertex list.");
        return value;
    }
}