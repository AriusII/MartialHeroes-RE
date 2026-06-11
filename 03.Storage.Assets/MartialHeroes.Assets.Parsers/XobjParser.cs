using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.xobj</c> ASCII static mesh files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh
/// <para>
/// The file is whitespace-tokenized (no magic, no binary header).
/// Read order: unused_token (discarded), num_triangles, (num_triangles×3) index tokens,
/// num_vertices, then per-vertex 8 tokens (pos_x, pos_y, pos_z, norm_x, norm_y, norm_z — discarded,
/// tex_u, tex_v).
/// </para>
/// <para>
/// ZERO rendering/engine dependencies. Output is <see cref="StaticMesh"/> with plain float arrays.
/// </para>
/// </remarks>
public static class XobjParser
{
    /// <summary>
    /// Parses the raw bytes of a <c>.xobj</c> file into a <see cref="StaticMesh"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded static mesh.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation, token under-run, or malformed float/integer tokens.
    /// </exception>
    public static StaticMesh Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static StaticMesh Parse(ReadOnlySpan<byte> data)
    {
        // The file is pure ASCII text.
        // Convert to string once; token splitting is then allocation-based but unavoidable for
        // a text format. The spec does not guarantee the file fits in a single line.
        // spec: Docs/RE/formats/mesh.md §Format: .xobj — "pure ASCII with no binary header".
        string text = Encoding.ASCII.GetString(data);

        // Tokenize on whitespace.
        // Using StringSplitOptions.RemoveEmptyEntries handles any run of whitespace characters
        // (spaces, tabs, CR, LF) as the spec specifies whitespace-tokenized format.
        // spec: Docs/RE/formats/mesh.md §Read order: "tokenized by whitespace".
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int pos = 0;

        // --- Token 1: unused_token (discard) ---
        // spec: Docs/RE/formats/mesh.md §Preamble — unused_token: "Read and silently discarded."
        // CONFIRMED (discard confirmed; meaning UNVERIFIED).
        _ = NextU32(tokens, ref pos, "unused_token");

        // --- Token 2: num_triangles ---
        // spec: Docs/RE/formats/mesh.md §Preamble — num_triangles: CONFIRMED.
        uint numTriangles = NextU32(tokens, ref pos, "num_triangles");

        // --- Index list: num_triangles × 3 tokens, u32 → u16 ---
        // spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
        int indexCount = checked((int)(numTriangles * 3));
        ushort[] indices = new ushort[indexCount];
        for (int i = 0; i < indexCount; i++)
        {
            uint raw = NextU32(tokens, ref pos, $"index[{i}]");
            // Truncate from u32 to u16 as specified.
            // spec: Docs/RE/formats/mesh.md §Index list: "in-memory representation stores each index as a u16".
            indices[i] = (ushort)(raw & 0xFFFF);
        }

        // --- num_vertices ---
        // spec: Docs/RE/formats/mesh.md §Vertex count: CONFIRMED.
        uint numVertices = NextU32(tokens, ref pos, "num_vertices");

        Vec3[] positions = new Vec3[numVertices];
        Vec2[] uvs = new Vec2[numVertices];

        // --- Vertex list: 8 tokens per vertex ---
        // spec: Docs/RE/formats/mesh.md §Vertex list — 8 tokens each.
        for (uint v = 0; v < numVertices; v++)
        {
            // Tokens 1-3: position
            // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x, pos_y, pos_z: CONFIRMED.
            float posX = NextF32(tokens, ref pos, $"vertex[{v}].pos_x");
            float posY = NextF32(tokens, ref pos, $"vertex[{v}].pos_y");
            float posZ = NextF32(tokens, ref pos, $"vertex[{v}].pos_z");

            // Tokens 4-6: normals — read and discard.
            // spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z: "read then discarded; not kept in memory". CONFIRMED.
            _ = NextF32(tokens, ref pos, $"vertex[{v}].norm_x");
            _ = NextF32(tokens, ref pos, $"vertex[{v}].norm_y");
            _ = NextF32(tokens, ref pos, $"vertex[{v}].norm_z");

            // Tokens 7-8: UV coordinates.
            // spec: Docs/RE/formats/mesh.md §Vertex list — tex_u: CONFIRMED.
            float texU = NextF32(tokens, ref pos, $"vertex[{v}].tex_u");
            // V-flip: engine stores 1.0 - tex_v in memory.
            // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory". CONFIRMED.
            float texVDisk = NextF32(tokens, ref pos, $"vertex[{v}].tex_v");
            float texV = 1.0f - texVDisk;

            positions[v] = new Vec3(posX, posY, posZ);
            uvs[v] = new Vec2(texU, texV);
        }

        return new StaticMesh
        {
            Positions = positions,
            Uvs = uvs,
            Indices = indices,
        };
    }

    // -------------------------------------------------------------------------
    // Private token-reader helpers
    // -------------------------------------------------------------------------

    private static uint NextU32(string[] tokens, ref int pos, string fieldName)
    {
        if (pos >= tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: token under-run reading '{fieldName}' " +
                $"(expected token at position {pos}, file has {tokens.Length} tokens total).");

        string token = tokens[pos++];
        if (!uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out uint value))
            throw new InvalidDataException(
                $".xobj parse error: expected unsigned integer for '{fieldName}', " +
                $"got \"{token}\" at token position {pos - 1}.");

        return value;
    }

    private static float NextF32(string[] tokens, ref int pos, string fieldName)
    {
        if (pos >= tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: token under-run reading '{fieldName}' " +
                $"(expected token at position {pos}, file has {tokens.Length} tokens total).");

        string token = tokens[pos++];
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            throw new InvalidDataException(
                $".xobj parse error: expected float for '{fieldName}', " +
                $"got \"{token}\" at token position {pos - 1}.");

        return value;
    }
}