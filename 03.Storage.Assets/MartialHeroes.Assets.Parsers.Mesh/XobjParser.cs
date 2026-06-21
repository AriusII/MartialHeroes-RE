using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

/// <summary>
///     Parser for <c>.xobj</c> ASCII static mesh files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xobj.md §Part 2 — .xobj mesh body (ASCII TEXT)
///     spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh (supplementary corpus detail)
///     <para>
///         The file is whitespace-tokenized (no magic, no binary header).
///         Read order: marker (token 1, discarded), tri_count (token 2),
///         tri_count×3 index tokens (u16), vert_count, then per-vertex 8 float tokens
///         (pos_x, pos_y, pos_z, norm_x, norm_y, norm_z — discarded, u, v).
///         spec: Docs/RE/formats/xobj.md §Read order — "marker … discarded": parser-verified.
///     </para>
///     <para>
///         ZERO rendering/engine dependencies. Output is <see cref="StaticMesh" /> with plain float arrays.
///     </para>
/// </remarks>
public static class XobjParser
{
    /// <summary>
    ///     Parses the raw bytes of a <c>.xobj</c> file into a <see cref="StaticMesh" />.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded static mesh.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown on truncation, token under-run, or malformed float/integer tokens.
    /// </exception>
    public static StaticMesh Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>
    ///     Parses from a <see cref="ReadOnlySpan{byte}" />.
    /// </summary>
    public static StaticMesh Parse(ReadOnlySpan<byte> data)
    {
        // The file is pure ASCII text.
        // Convert to string once; token splitting is then allocation-based but unavoidable for
        // a text format. The spec does not guarantee the file fits in a single line.
        // spec: Docs/RE/formats/mesh.md §Format: .xobj — "pure ASCII with no binary header".
        var text = Encoding.ASCII.GetString(data);

        // Tokenize on whitespace.
        // Using StringSplitOptions.RemoveEmptyEntries handles any run of whitespace characters
        // (spaces, tabs, CR, LF) as the spec specifies whitespace-tokenized format.
        // spec: Docs/RE/formats/mesh.md §Read order: "tokenized by whitespace".
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var pos = 0;

        // --- Token 1: marker (discard) ---
        // spec: Docs/RE/formats/xobj.md §Read order step 1 —
        //   "First token. Read into a local and discarded — the parser keeps no copy.
        //   Likely a format/version or vertex-format tag; the consumer does not use it."
        //   parser-verified; observed value = 4 in the one real sample.
        _ = NextU32(tokens, ref pos, "marker");

        // --- Token 2: tri_count ---
        // spec: Docs/RE/formats/xobj.md §Read order step 2 — tri_count: parser-verified.
        var numTriangles = NextU32(tokens, ref pos, "tri_count");

        // --- Index list: tri_count × 3 tokens, parsed as int, narrowed to u16 on store ---
        // spec: Docs/RE/formats/xobj.md §Read order step 4 —
        //   "One index token per line; parsed as int, narrowed to u16 on store.
        //    Total 3 × tri_count tokens, in order." parser-verified + sample-verified.
        // spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
        var indexCount = checked((int)(numTriangles * 3));

        // Pre-loop bounds check for index block (includes vert_count token at the end).
        if (pos + indexCount + 1 > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: index block truncated — need {indexCount} index tokens + vert_count, " +
                $"but only {tokens.Length - pos} tokens remain. " +
                "spec: Docs/RE/formats/xobj.md §Read order step 3-4.");

        var indices = new ushort[indexCount];
        for (var i = 0; i < indexCount; i++)
            // Bounds checked above; read without per-element string allocation.
            // Narrowed to u16 on store.
            // spec: Docs/RE/formats/xobj.md §Read order step 4 — "narrowed to u16": parser-verified.
            indices[i] = (ushort)(NextU32Unchecked(tokens, ref pos) & 0xFFFF);

        // --- vert_count ---
        // spec: Docs/RE/formats/xobj.md §Read order step 5 — vert_count: parser-verified.
        // spec: Docs/RE/formats/mesh.md §Vertex count: CONFIRMED.
        var numVertices = NextU32(tokens, ref pos, "vert_count");

        var positions = new Vec3[numVertices];
        var uvs = new Vec2[numVertices];

        // Pre-loop bounds check: each vertex needs 8 tokens.
        // spec: Docs/RE/formats/xobj.md §Per-vertex line — "8 float tokens (one per line)": parser-verified.
        // spec: Docs/RE/formats/mesh.md §Vertex list — 8 tokens each: CONFIRMED.
        var tokensNeeded = pos + checked((int)numVertices * 8);
        if (tokensNeeded > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: vertex block truncated — need {numVertices} vertices × 8 tokens " +
                $"({tokensNeeded} total), but only {tokens.Length} tokens available. " +
                "spec: Docs/RE/formats/xobj.md §Per-vertex line.");

        for (uint v = 0; v < numVertices; v++)
        {
            // Tokens 1-3: position. Bounds checked above; read without per-element string allocation.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — token 1/2/3: pos.x/y/z @ vertex +0/4/8: parser-verified.
            // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x, pos_y, pos_z: CONFIRMED.
            var posX = NextF32Unchecked(tokens, ref pos);
            var posY = NextF32Unchecked(tokens, ref pos);
            var posZ = NextF32Unchecked(tokens, ref pos);

            // Tokens 4-6: normals — read and discard.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — tokens 4/5/6: n.x/y/z — DISCARDED.
            // spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z: "read then discarded; not kept in memory". CONFIRMED.
            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);

            // Tokens 7-8: UV coordinates.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — token 7: u @ vertex +0x10 (stored as-is).
            // spec: Docs/RE/formats/mesh.md §Vertex list — tex_u: CONFIRMED.
            var texU = NextF32Unchecked(tokens, ref pos);
            // V-flip: stored as 1.0 − v.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — token 8: v @ vertex +0x14 (stored as 1.0 − v).
            // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory". CONFIRMED.
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

    // ─── Mesh-particle overload ────────────────────────────────────────────────

    /// <summary>
    ///     Parses a <c>.xobj</c> file into a <see cref="XobjMeshData" /> representing the
    ///     24-byte per-vertex shared mesh table layout used by mesh-particle emitters
    ///     (<c>emitter_type == 1</c>, <c>resource_id &lt; 10000</c>).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>
    ///     Decoded mesh in the 24-byte stride layout:
    ///     POSITION12 (3 × f32) + DIFFUSE4 (0xFF000000 constructor default) + TEXCOORD8 (2 × f32).
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown on truncation, token under-run, or malformed float/integer tokens.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/xobj.md §Part 2 — .xobj mesh body and §Part 3 — Runtime structures.
    ///     spec: Docs/RE/formats/xobj.md §Named constants — XOBJ_VERTEX_DEFAULT_DIFFUSE = 0xFF000000
    ///     (ARGB: opaque, black RGB). Not read from file — set by the vertex constructor.
    ///     spec: Docs/RE/formats/xobj.md §Runtime Vertex @ +0x0C — "Set by the vertex constructor to
    ///     opaque-default: 0xFF000000 (ARGB: opaque, black RGB)": parser-verified.
    ///     Normals are read from disk and discarded (not in the 24-byte in-memory layout).
    ///     spec: Docs/RE/formats/xobj.md §Per-vertex line — "tokens 4/5/6: n.x/y/z — DISCARDED": parser-verified.
    ///     V-flip applied on store: tex_v = 1.0 − disk_v.
    ///     spec: Docs/RE/formats/xobj.md §Per-vertex line — "token 8: v — stored as 1.0 − v": parser-verified.
    /// </remarks>
    public static XobjMeshData ParseAsMeshParticle(ReadOnlyMemory<byte> data)
    {
        return ParseAsMeshParticle(data.Span);
    }

    /// <summary>
    ///     Parses from a <see cref="ReadOnlySpan{byte}" />.
    /// </summary>
    public static XobjMeshData ParseAsMeshParticle(ReadOnlySpan<byte> data)
    {
        // The file is pure ASCII text.
        // spec: Docs/RE/formats/xobj.md §Part 2 — "plain ASCII text file — one numeric token per line,
        //   whitespace/newline delimited": parser-verified.
        var text = Encoding.ASCII.GetString(data);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var pos = 0;

        // Token 1: marker (discard).
        // spec: Docs/RE/formats/xobj.md §Read order step 1 —
        //   "First token. Read into a local and discarded — the parser keeps no copy.
        //   Likely a format/version or vertex-format tag; the consumer does not use it."
        //   parser-verified; observed value = 4 in the one real sample.
        _ = NextU32(tokens, ref pos, "marker");

        // Token 2: tri_count.
        // spec: Docs/RE/formats/xobj.md §Read order step 2 — tri_count: parser-verified.
        var numTriangles = NextU32(tokens, ref pos, "tri_count");

        // Index list: tri_count × 3 tokens, narrowed to u16.
        // spec: Docs/RE/formats/xobj.md §Read order step 4 — index_count = 3 × tri_count: parser-verified.
        // spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
        var indexCount = checked((int)(numTriangles * 3));

        // Pre-loop bounds check for index block (includes vert_count token at end).
        if (pos + indexCount + 1 > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: index block truncated — need {indexCount} index tokens + vert_count, " +
                $"but only {tokens.Length - pos} tokens remain. " +
                "spec: Docs/RE/formats/xobj.md §Read order step 3-4.");

        var indices = new ushort[indexCount];
        for (var i = 0; i < indexCount; i++)
            // Bounds checked above; narrowed to u16 on store.
            // spec: Docs/RE/formats/xobj.md §Read order step 4 — "narrowed to u16": parser-verified.
            indices[i] = (ushort)(NextU32Unchecked(tokens, ref pos) & 0xFFFF);

        // vert_count.
        // spec: Docs/RE/formats/xobj.md §Read order step 5 — vert_count: parser-verified.
        // spec: Docs/RE/formats/mesh.md §Vertex count: CONFIRMED.
        var numVertices = NextU32(tokens, ref pos, "vert_count");

        var vertices = new XobjVertex[numVertices];

        // Pre-loop bounds check: each vertex needs 8 tokens.
        // spec: Docs/RE/formats/xobj.md §Per-vertex line — "8 float tokens (one per line)": parser-verified.
        // spec: Docs/RE/formats/mesh.md §Vertex list — 8 tokens each: CONFIRMED.
        var vertexTokensNeeded = pos + checked((int)numVertices * 8);
        if (vertexTokensNeeded > tokens.Length)
            throw new InvalidDataException(
                $".xobj parse error: vertex block truncated — need {numVertices} vertices × 8 tokens " +
                $"({vertexTokensNeeded} total), but only {tokens.Length} tokens available. " +
                "spec: Docs/RE/formats/xobj.md §Per-vertex line.");

        for (uint v = 0; v < numVertices; v++)
        {
            // Tokens 1-3: position. Bounds checked above; read without per-element string allocation.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — token 1/2/3: pos.x/y/z @ vertex +0/4/8: parser-verified.
            // spec: Docs/RE/formats/mesh.md §Vertex data rows — pos_x/y/z @ col 0/1/2: CONFIRMED.
            var px = NextF32Unchecked(tokens, ref pos);
            var py = NextF32Unchecked(tokens, ref pos);
            var pz = NextF32Unchecked(tokens, ref pos);

            // Tokens 4-6: normals — read and discard.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — "tokens 4/5/6: n.x/y/z — DISCARDED": parser-verified.
            // spec: Docs/RE/formats/mesh.md §Vertex data rows — norm_x/y/z @ col 3/4/5: "discarded": CONFIRMED.
            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);
            _ = NextF32Unchecked(tokens, ref pos);

            // Tokens 7-8: UV coordinates.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — token 7: u @ vertex +0x10 (stored as-is): parser-verified.
            // spec: Docs/RE/formats/mesh.md §Vertex data rows — tex_u @ col 6: CONFIRMED.
            var tu = NextF32Unchecked(tokens, ref pos);
            // V-flip: stored as 1.0 − v.
            // spec: Docs/RE/formats/xobj.md §Per-vertex line — token 8: v @ vertex +0x14 (stored as 1.0 − v): parser-verified.
            // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory": CONFIRMED.
            var tvMem = 1.0f - NextF32Unchecked(tokens, ref pos);

            // DIFFUSE4 at runtime vertex offset +0x0C: constructor-default opaque-black (ARGB).
            // NOT read from disk. Set by the vertex constructor to 0xFF000000.
            // spec: Docs/RE/formats/xobj.md §Runtime Vertex @ +0x0C —
            //   "Set by the vertex constructor to opaque-default: 0xFF000000 (ARGB: opaque, black RGB).
            //    Not read from file.": parser-verified.
            // spec: Docs/RE/formats/xobj.md §Named constants — XOBJ_VERTEX_DEFAULT_DIFFUSE = 0xFF000000.
            const uint diffuse = 0xFF000000u;

            vertices[v] = new XobjVertex(px, py, pz, diffuse, tu, tvMem);
        }

        return new XobjMeshData { Indices = indices, Vertices = vertices };
    }

    // -------------------------------------------------------------------------
    // Private token-reader helpers
    // -------------------------------------------------------------------------

    // Named helpers — used for header-level fields (cold path, small count).
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

    // Unchecked helpers — used inside per-element loops after a pre-loop bounds check.
    // These omit the bounds guard and the per-element name to avoid hot-path allocations.
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