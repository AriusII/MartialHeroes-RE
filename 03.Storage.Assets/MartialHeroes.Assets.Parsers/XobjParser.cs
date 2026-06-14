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

    // ─── Mesh-particle overload ────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>.xobj</c> file into a <see cref="XobjMeshData"/> representing the
    /// 24-byte per-vertex shared mesh table layout used by mesh-particle emitters
    /// (<c>emitter_type == 1</c>, <c>resource_id &lt; 10000</c>).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>
    /// Decoded mesh in the 24-byte stride layout:
    /// POSITION12 (3 × f32) + DIFFUSE4 (uninitialised, always 0) + TEXCOORD8 (2 × f32).
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation, token under-run, or malformed float/integer tokens.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/effects.md §A.11 — .xobj files feed the shared mesh table at 24-byte stride
    ///   for emitter_type == 1 mesh-particle objects (resource_id &lt; 10000): CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex
    ///   (pos_x/y/z @ +0/4/8, uninitialised @ +12, tex_u @ +16, tex_v as 1.0−disk @ +20): CONFIRMED.
    /// Normals are read from disk and discarded (not in the 24-byte in-memory layout).
    /// spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z: "read then discarded": CONFIRMED.
    /// DIFFUSE4 (@+12) is always zero (uninitialised by the loader per spec).
    /// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
    /// </remarks>
    public static XobjMeshData ParseAsMeshParticle(ReadOnlyMemory<byte> data) =>
        ParseAsMeshParticle(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static XobjMeshData ParseAsMeshParticle(ReadOnlySpan<byte> data)
    {
        // The file is pure ASCII text.
        // spec: Docs/RE/formats/mesh.md — "pure ASCII with no binary header": CONFIRMED.
        string text = Encoding.ASCII.GetString(data);
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int pos = 0;

        // Token 1: slot_id (discard).
        // spec: Docs/RE/formats/mesh.md §Preamble — slot_id u32 @ token 1: CONFIRMED (discard confirmed).
        _ = NextU32(tokens, ref pos, "slot_id");

        // Token 2: face_count (num_triangles).
        // spec: Docs/RE/formats/mesh.md §Preamble — face_count u32 @ token 2: CONFIRMED.
        uint numTriangles = NextU32(tokens, ref pos, "face_count");

        // Index list: face_count × 3 tokens.
        // spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
        int indexCount = checked((int)(numTriangles * 3));
        ushort[] indices = new ushort[indexCount];
        for (int i = 0; i < indexCount; i++)
        {
            uint raw = NextU32(tokens, ref pos, $"index[{i}]");
            indices[i] = (ushort)(raw & 0xFFFF);
        }

        // vertex_count.
        // spec: Docs/RE/formats/mesh.md §Vertex count: CONFIRMED.
        uint numVertices = NextU32(tokens, ref pos, "vertex_count");

        var vertices = new XobjVertex[numVertices];
        for (uint v = 0; v < numVertices; v++)
        {
            // Tokens 1-3: position.
            // spec: Docs/RE/formats/mesh.md §Vertex data rows — pos_x/y/z @ col 0/1/2: CONFIRMED.
            float px = NextF32(tokens, ref pos, $"vertex[{v}].pos_x");
            float py = NextF32(tokens, ref pos, $"vertex[{v}].pos_y");
            float pz = NextF32(tokens, ref pos, $"vertex[{v}].pos_z");

            // Tokens 4-6: normals — read and discard.
            // spec: Docs/RE/formats/mesh.md §Vertex data rows — norm_x/y/z @ col 3/4/5: "discarded": CONFIRMED.
            // spec: Docs/RE/formats/effects.md §A.11 — normals not in the shared mesh table: CONFIRMED.
            _ = NextF32(tokens, ref pos, $"vertex[{v}].norm_x");
            _ = NextF32(tokens, ref pos, $"vertex[{v}].norm_y");
            _ = NextF32(tokens, ref pos, $"vertex[{v}].norm_z");

            // Tokens 7-8: UV coordinates.
            // spec: Docs/RE/formats/mesh.md §Vertex data rows — tex_u @ col 6, tex_v @ col 7: CONFIRMED.
            float tu = NextF32(tokens, ref pos, $"vertex[{v}].tex_u");
            float tvDisk = NextF32(tokens, ref pos, $"vertex[{v}].tex_v");
            // V-flip: in-memory tex_v = 1.0 − disk_tex_v.
            // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory": CONFIRMED.
            float tvMem = 1.0f - tvDisk;

            // DIFFUSE4 at in-memory offset +12 is uninitialised (always 0 per spec).
            // spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
            const uint diffuse = 0u;

            vertices[v] = new XobjVertex(px, py, pz, diffuse, tu, tvMem);
        }

        return new XobjMeshData { Indices = indices, Vertices = vertices };
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