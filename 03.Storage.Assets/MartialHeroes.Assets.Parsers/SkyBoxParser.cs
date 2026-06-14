using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Decoder for <c>sky%d.box</c> sky-dome geometry files.
/// Logical path pattern: <c>data/sky/dat/sky{area_id}.box</c> (VFS-only).
/// </summary>
/// <remarks>
/// No magic number, no version field. File identity comes from the VFS path.
/// Little-endian throughout. No rendering dependency.
/// spec: Docs/RE/formats/sky.md §A — Section A sky%d.box.
/// spec: Docs/RE/formats/sky.md §Identification — no magic, no version, little-endian.
/// </remarks>
public static class SkyBoxParser
{
    // Vertex stride: 20 bytes (5 × f32).
    // spec: Docs/RE/formats/sky.md §A.3 — "Vertex stride: 20 bytes": HIGH
    private const int VertexStride = SkyBoxMesh.VertexStride; // 20

    // Index width: 2 bytes (u16).
    // spec: Docs/RE/formats/sky.md §A.5 — "Index width: 16-bit (u16)": HIGH
    private const int IndexWidth = SkyBoxMesh.IndexWidth; // 2

    // Vertex cap per mesh.
    // spec: Docs/RE/formats/sky.md §A.3 — "Cap: 300 (0x12C)": HIGH
    private const int MaxVertices = SkyBoxMesh.MaxVertices; // 300

    // Index cap per mesh.
    // spec: Docs/RE/formats/sky.md §A.5 — "Cap: 900 (0x384)": HIGH
    private const int MaxIndices = SkyBoxMesh.MaxIndices; // 900

    // Texture-name record stride: 47 bytes.
    // spec: Docs/RE/formats/sky.md §A.2 — "Record stride: 47 bytes": HIGH
    private const int TextureNameStride = SkyBoxData.TextureNameStride; // 47

    /// <summary>
    /// Parses a <c>sky%d.box</c> blob into a <see cref="SkyBoxData"/> value.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Decoded sky-dome geometry.</returns>
    /// <exception cref="InvalidDataException">
    /// Buffer is too short, a declared count exceeds its cap, or the buffer is truncated
    /// relative to declared counts.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/sky.md §A.6 Overall structure — all vertex arrays precede all
    /// index arrays (two passes over texture_count).
    /// </remarks>
    public static SkyBoxData Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static SkyBoxData Parse(ReadOnlySpan<byte> span)
    {
        int cursor = 0;

        // ── Header ────────────────────────────────────────────────────────────
        // texture_count u32 @ 0x00.
        // spec: Docs/RE/formats/sky.md §A.1 — texture_count u32 @ 0x00: HIGH
        if (span.Length < 4)
            throw new InvalidDataException(
                "sky*.box parse error: buffer too short for header (need 4 bytes). " +
                "spec: Docs/RE/formats/sky.md §A.1.");

        uint textureCount = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
        cursor += 4;

        // Sanity cap: no explicit limit in spec for texture_count, but apply a conservative
        // bound to prevent pathological allocation on corrupt data.
        if (textureCount > SkyBoxData.MaxTextures)
            throw new InvalidDataException(
                $"sky*.box parse error: texture_count {textureCount} exceeds sanity cap " +
                $"{SkyBoxData.MaxTextures}. " +
                "spec: Docs/RE/formats/sky.md §A.1.");

        int n = (int)textureCount;

        // ── Texture-name records (× texture_count) ────────────────────────────
        // Each record is exactly 47 bytes — null-terminated ASCII within the field.
        // spec: Docs/RE/formats/sky.md §A.2 — texture_name char[47], stride 47 bytes: HIGH
        int nameBlockSize = n * TextureNameStride;
        if (cursor + nameBlockSize > span.Length)
            throw new InvalidDataException(
                $"sky*.box parse error: buffer truncated in texture-name block " +
                $"(cursor={cursor}, need {nameBlockSize} bytes, have {span.Length - cursor}). " +
                "spec: Docs/RE/formats/sky.md §A.2.");

        var textureNames = new string[n];
        for (int i = 0; i < n; i++)
        {
            // Read the 47-byte name field and decode as null-terminated ASCII.
            // spec: Docs/RE/formats/sky.md §A.2 — fixed-width texture name, 47 bytes: HIGH
            ReadOnlySpan<byte> nameSpan = span.Slice(cursor, TextureNameStride);
            int nullPos = nameSpan.IndexOf((byte)0);
            int nameLen = nullPos >= 0 ? nullPos : TextureNameStride;
            textureNames[i] = Encoding.ASCII.GetString(nameSpan[..nameLen]);
            cursor += TextureNameStride;
        }

        // ── Per-mesh vertex arrays (× texture_count) ─────────────────────────
        // First pass: all vertex arrays, in texture order.
        // spec: Docs/RE/formats/sky.md §A.6 — all vertex arrays precede all index arrays.
        // spec: Docs/RE/formats/sky.md §A.3 — for each mesh: u32 vertex_count, then vertex_count × 20 bytes.
        var meshVertices = new SkyBoxVertex[n][];
        for (int i = 0; i < n; i++)
        {
            // vertex_count u32 prefix.
            // spec: Docs/RE/formats/sky.md §A.3 — u32 vertex_count, cap 300 (0x12C): HIGH
            if (cursor + 4 > span.Length)
                throw new InvalidDataException(
                    $"sky*.box parse error: buffer truncated reading vertex_count for mesh {i} " +
                    $"(cursor={cursor}). " +
                    "spec: Docs/RE/formats/sky.md §A.3.");

            uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
            cursor += 4;

            if (vertexCount > MaxVertices)
                throw new InvalidDataException(
                    $"sky*.box parse error: mesh {i} vertex_count {vertexCount} exceeds cap " +
                    $"{MaxVertices} (0x{MaxVertices:X3}). " +
                    "spec: Docs/RE/formats/sky.md §A.3.");

            int vc = (int)vertexCount;
            int vertexBlockBytes = vc * VertexStride;

            // Check buffer before reading the vertex array.
            if (cursor + vertexBlockBytes > span.Length)
                throw new InvalidDataException(
                    $"sky*.box parse error: buffer truncated in vertex array for mesh {i} " +
                    $"(cursor={cursor}, need {vertexBlockBytes} bytes, have {span.Length - cursor}). " +
                    "spec: Docs/RE/formats/sky.md §A.3.");

            var vertices = new SkyBoxVertex[vc];
            for (int v = 0; v < vc; v++)
            {
                int vBase = cursor + v * VertexStride;

                // position (x, y, z) — f32[3] @ sub-offset 0x00.
                // spec: Docs/RE/formats/sky.md §A.4 — position f32[3] @ sub-offset 0x00: MED
                float x = BinaryPrimitives.ReadSingleLittleEndian(span[(vBase + 0x00)..]);
                float y = BinaryPrimitives.ReadSingleLittleEndian(span[(vBase + 0x04)..]);
                float z = BinaryPrimitives.ReadSingleLittleEndian(span[(vBase + 0x08)..]);

                // uv (u, v) — f32[2] @ sub-offset 0x0C.
                // spec: Docs/RE/formats/sky.md §A.4 — uv f32[2] @ sub-offset 0x0C: MED
                float u = BinaryPrimitives.ReadSingleLittleEndian(span[(vBase + 0x0C)..]);
                float vt = BinaryPrimitives.ReadSingleLittleEndian(span[(vBase + 0x10)..]);

                vertices[v] = new SkyBoxVertex(x, y, z, u, vt);
            }

            cursor += vertexBlockBytes;
            meshVertices[i] = vertices;
        }

        // ── Per-mesh index arrays (× texture_count) ──────────────────────────
        // Second pass: all index arrays, in texture order.
        // spec: Docs/RE/formats/sky.md §A.6 — all vertex arrays precede all index arrays.
        // spec: Docs/RE/formats/sky.md §A.5 — for each mesh: u32 index_count, then index_count × u16.
        var meshIndices = new ushort[n][];
        for (int i = 0; i < n; i++)
        {
            // index_count u32 prefix.
            // spec: Docs/RE/formats/sky.md §A.5 — u32 index_count, cap 900 (0x384): HIGH
            if (cursor + 4 > span.Length)
                throw new InvalidDataException(
                    $"sky*.box parse error: buffer truncated reading index_count for mesh {i} " +
                    $"(cursor={cursor}). " +
                    "spec: Docs/RE/formats/sky.md §A.5.");

            uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
            cursor += 4;

            if (indexCount > MaxIndices)
                throw new InvalidDataException(
                    $"sky*.box parse error: mesh {i} index_count {indexCount} exceeds cap " +
                    $"{MaxIndices} (0x{MaxIndices:X3}). " +
                    "spec: Docs/RE/formats/sky.md §A.5.");

            int ic = (int)indexCount;
            int indexBlockBytes = ic * IndexWidth;

            // Check buffer before reading the index array.
            if (cursor + indexBlockBytes > span.Length)
                throw new InvalidDataException(
                    $"sky*.box parse error: buffer truncated in index array for mesh {i} " +
                    $"(cursor={cursor}, need {indexBlockBytes} bytes, have {span.Length - cursor}). " +
                    "spec: Docs/RE/formats/sky.md §A.5.");

            var indices = new ushort[ic];
            for (int k = 0; k < ic; k++)
            {
                // u16 index, little-endian.
                // spec: Docs/RE/formats/sky.md §A.5 — u16 index, LE: HIGH
                indices[k] = BinaryPrimitives.ReadUInt16LittleEndian(span[(cursor + k * IndexWidth)..]);
            }

            cursor += indexBlockBytes;
            meshIndices[i] = indices;
        }

        // ── Assemble result ───────────────────────────────────────────────────
        var meshes = new SkyBoxMesh[n];
        for (int i = 0; i < n; i++)
        {
            meshes[i] = new SkyBoxMesh
            {
                Vertices = meshVertices[i],
                Indices = meshIndices[i],
            };
        }

        return new SkyBoxData
        {
            TextureNames = textureNames,
            Meshes = meshes,
        };
    }
}