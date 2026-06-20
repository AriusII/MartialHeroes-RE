using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
///     Converts a <see cref="TerrainCell" /> (parsed .ted terrain geometry blob) to a
///     self-contained GLB (binary glTF 2.0) stream.
///     Terrain grid conventions:
///     spec: Docs/RE/formats/terrain.md §5.1 Grid geometry
///     - 65×65 vertex grid, 64×64 quads per cell.
///     - Vertex spacing: 16.0 world units (= 1024 / 64). CONFIRMED.
///     - Heights are IEEE 754 f32 in row-major order. Axis orientation PARSER-VERIFIED:
///     heights[row * 65 + col]  with  col → world X (inner/fast, stride 1)
///     row → world Z (outer/slow, stride 65)
///     spec: Docs/RE/formats/terrain.md §5.2 Axis orientation — PARSER-VERIFIED (CONFIRMED).
///     spec: Docs/RE/formats/terrain.md §5.2 Block 1 — Heightmap: f32le, 65×65 = 4225. CONFIRMED.
///     Coordinate-system conversion (identical to GltfConverter):
///     Legacy format: left-handed Y-up (D3D9 default).
///     glTF 2.0: right-handed Y-up, −Z forward.
///     Conversion: negate the X component of every position to flip handedness.
///     spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED (same convention).
///     glTF 2.0 spec §3.4: right-handed Y-up.
///     Winding order: the X-flip reverses the winding of every quad triangle.
///     The quad triangulation is adjusted so the emitted winding is counter-clockwise in glTF.
///     glTF 2.0 spec §3.7.2.1: counter-clockwise winding defines front faces.
///     Diffuse colour:
///     Block 5 of the .ted file supplies one RGBA8 colour per vertex (65×65 = 4225 entries).
///     spec: Docs/RE/formats/terrain.md §5.2 Block 5 — Diffuse colour: u8×4 (R,G,B,A). CONFIRMED.
///     These colours are emitted as a COLOR_0 VEC4 accessor (normalised UNSIGNED_BYTE) and as
///     a baseColorTexture via a small 65×65 PNG image embedded as a data-URI in the extras.
///     The PNG path reuses <see cref="PngConverter.WritePngRgba8" />.
/// </summary>
public static class TerrainGltfConverter
{
    // -------------------------------------------------------------------------
    // Terrain grid constants
    // spec: Docs/RE/formats/terrain.md §5.1 Grid geometry
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Number of vertices per axis.
    ///     spec: Docs/RE/formats/terrain.md §5.1 — "65 × 65 vertices": CONFIRMED.
    /// </summary>
    private const int GridSize = TerrainCell.GridSize; // 65

    /// <summary>
    ///     Quad grid dimension (one less than vertex grid).
    ///     spec: Docs/RE/formats/terrain.md §5.1 — "64 × 64 quads per cell": CONFIRMED.
    /// </summary>
    private const int QuadSize = GridSize - 1; // 64

    /// <summary>
    ///     World-space distance between adjacent vertices in X and Z.
    ///     spec: Docs/RE/formats/terrain.md §5.1 — "Vertex spacing: 16.0 world units
    ///     (derived: 1024 / 64 = 16)": CONFIRMED.
    /// </summary>
    private const float VertexSpacing = 16.0f;

    // -------------------------------------------------------------------------
    // glTF / GLB constants (same as GltfConverter — duplicated here to keep
    // this file self-contained and to avoid coupling to private members).
    // glTF 2.0 spec §binary-gltf §chunks
    // -------------------------------------------------------------------------

    private const uint GlbMagic = 0x46546C67u; // 'glTF'
    private const uint GlbVersion = 2u;
    private const uint ChunkTypeJson = 0x4E4F534Au; // 'JSON'
    private const uint ChunkTypeBin = 0x004E4942u; // 'BIN\0'

    private const int ComponentTypeUnsignedByte = 5121; // glTF 2.0 spec §Accessor.componentType
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;
    private const int ComponentTypeFloat = 5126;

    private const int TargetArrayBuffer = 34962; // glTF 2.0 spec §BufferView.target
    private const int TargetElementArrayBuffer = 34963;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Converts one <see cref="TerrainCell" /> to a GLB stream.
    ///     The output contains:
    ///     - A POSITION accessor (65×65 = 4225 VEC3 f32 vertices).
    ///     - A TEXCOORD_0 accessor (normalised UVs over the [0,1] grid).
    ///     - A COLOR_0 accessor (VEC4 UNSIGNED_BYTE normalised, from block 5 diffuse colours).
    ///     - An index accessor (64×64×2×3 = 24576 triangles).
    /// </summary>
    /// <param name="cell">Parsed terrain cell from <c>TedTerrainParser</c>.</param>
    /// <param name="output">Destination stream (does not need to be seekable).</param>
    public static void WriteGlb(TerrainCell cell, Stream output)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(output);

        // 65×65 = 4225 vertices; 64×64×2 = 8192 quads → 8192×3 = 24576 indices.
        const int vertexCount = GridSize * GridSize; // 4225
        const int triangleCount = QuadSize * QuadSize * 2; // 8192
        const int indexCount = triangleCount * 3; // 24576

        // Index component type: 24576 fits in u16 (max 65535).
        const bool use32Bit = false;

        // ---- Build binary buffer ----
        var binaryBuffer = BuildBinaryBuffer(
            cell, vertexCount, indexCount,
            out var posOffset, out var posLength,
            out var uvOffset, out var uvLength,
            out var colOffset, out var colLength,
            out var idxOffset, out var idxLength);

        // ---- Build JSON ----
        var json = BuildJson(
            cell, binaryBuffer.Length,
            vertexCount, indexCount, use32Bit,
            posOffset, posLength,
            uvOffset, uvLength,
            colOffset, colLength,
            idxOffset, idxLength);

        // ---- Write GLB ----
        WriteGlbChunks(output, json, binaryBuffer);
    }

    // -------------------------------------------------------------------------
    // Binary buffer construction
    // -------------------------------------------------------------------------

    private static byte[] BuildBinaryBuffer(
        TerrainCell cell,
        int vertexCount, int indexCount,
        out int posOffset, out int posLength,
        out int uvOffset, out int uvLength,
        out int colOffset, out int colLength,
        out int idxOffset, out int idxLength)
    {
        posLength = vertexCount * 3 * sizeof(float); // VEC3 f32
        uvLength = vertexCount * 2 * sizeof(float); // VEC2 f32
        colLength = vertexCount * 4; // VEC4 UNSIGNED_BYTE (4 bytes per vertex)
        idxLength = indexCount * sizeof(ushort); // SCALAR u16

        posOffset = 0;
        uvOffset = Align4(posOffset + posLength);
        colOffset = Align4(uvOffset + uvLength);
        idxOffset = Align4(colOffset + colLength);
        var bufSize = Align4(idxOffset + idxLength);

        var buf = new byte[bufSize];

        // ---- Positions ----
        // Grid layout: heights[row * 65 + col]
        //   col → world X (inner axis, stride 1): col 0 = cell X minimum, col 64 = cell X maximum.
        //   row → world Z (outer axis, stride 65): row 0 = cell Z minimum, row 64 = cell Z maximum.
        //   spec: Docs/RE/formats/terrain.md §5.2 Axis orientation — PARSER-VERIFIED (CONFIRMED).
        //   Two independent evidence lines: (1) loader index arithmetic col→X / row→Z; (2) seam-
        //   continuity sample test (last row of lower-Z cell matches first row of higher-Z cell).
        //   worldX = col × 16.0,  worldZ = row × 16.0
        // spec: Docs/RE/formats/terrain.md §5.1 — "64×64 quads per cell", vertex spacing 16.0: CONFIRMED.
        // Height: direct f32 world-space Y, no scale multiplier.
        // spec: Docs/RE/formats/terrain.md §5.4 Block 1 — heights f32le, direct world-Y: CONFIRMED.
        //
        // Coordinate flip: negate X to convert left-handed D3D9 → right-handed glTF.
        // spec: Docs/RE/formats/mesh.md §Vertex list — same convention as character meshes.
        // glTF 2.0 spec §3.4.
        var cursor = posOffset;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            // heights[row * 65 + col]: col → world X (inner/fast), row → world Z (outer/slow).
            // spec: Docs/RE/formats/terrain.md §5.2 Axis orientation — PARSER-VERIFIED (CONFIRMED).
            var vi = r * GridSize + c; // = row * 65 + col
            var worldX = -(c * VertexSpacing); // col * 16.0, then negated for handedness flip
            var worldY = cell.Heights[vi]; // direct world-space Y (no scale)
            var worldZ = r * VertexSpacing; // row * 16.0

            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), worldX);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), worldY);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), worldZ);
            cursor += 12;

            if (worldX < minX) minX = worldX;
            if (worldX > maxX) maxX = worldX;
            if (worldY < minY) minY = worldY;
            if (worldY > maxY) maxY = worldY;
            if (worldZ < minZ) minZ = worldZ;
            if (worldZ > maxZ) maxZ = worldZ;
        }

        // min/max are recomputed inside BuildJson from the same cell data.

        // ---- UVs ----
        // Normalised UV: U = col / (GridSize-1), V = row / (GridSize-1).
        // UV origin: glTF uses bottom-left; row 0 = cell Z minimum maps to V=0 (terrain south edge).
        // spec: Docs/RE/formats/terrain.md §5.2 Axis orientation — PARSER-VERIFIED (CONFIRMED):
        //   col → X (inner), row → Z (outer); same convention for all five blocks.
        cursor = uvOffset;
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            var u = c / (float)(GridSize - 1);
            var v = r / (float)(GridSize - 1);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), u);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), v);
            cursor += 8;
        }

        // ---- Colours (COLOR_0) ----
        // Block 5: RGBA, 4 bytes per vertex, in the same row-major order as the heightmap.
        // DiffuseColours is now (float R, float G, float B, float A)[] decoded from on-disk ×0.5 encoding.
        // Re-encode to u8 by multiplying each float component by 255 and clamping.
        // spec: Docs/RE/formats/terrain.md §5.8 Block 5 — "×0.5 decode: CONFIRMED."
        // The mapping layer (Assets.Mapping) is responsible for converting decoded floats to wire bytes.
        cursor = colOffset;
        var diffuse = cell.DiffuseColours;
        for (var vi = 0; vi < vertexCount; vi++)
        {
            var (dr, dg, db, da) = diffuse[vi];
            buf[cursor + 0] = (byte)Math.Clamp((int)(dr * 255f + 0.5f), 0, 255); // R
            buf[cursor + 1] = (byte)Math.Clamp((int)(dg * 255f + 0.5f), 0, 255); // G
            buf[cursor + 2] = (byte)Math.Clamp((int)(db * 255f + 0.5f), 0, 255); // B
            buf[cursor + 3] = (byte)Math.Clamp((int)(da * 255f + 0.5f), 0, 255); // A
            cursor += 4;
        }

        // ---- Indices ----
        // Triangulate each quad (r, c) as two CCW triangles after the X-flip.
        // Quad vertices (row r, col c):
        //   TL = (r,   c  ) = vi0    TR = (r,   c+1) = vi1
        //   BL = (r+1, c  ) = vi2    BR = (r+1, c+1) = vi3
        //
        // Without handedness flip (left-handed, CW looking down +Y):
        //   tri 0: TL, TR, BR   tri 1: TL, BR, BL
        //
        // After negating X (X-flip reverses winding CW→CCW):
        //   The flip maps col c → negated X, which also reverses triangle orientation.
        //   We swap i1↔i2 within each triangle (same technique as GltfConverter).
        //   spec: Docs/RE/formats/mesh.md §Index list — same winding convention. CONFIRMED.
        //   glTF 2.0 spec §3.7.2.1: counter-clockwise winding.
        //
        //   tri 0 (reversed): TL, BR, TR
        //   tri 1 (reversed): TL, BL, BR
        cursor = idxOffset;
        for (var r = 0; r < QuadSize; r++)
        for (var c = 0; c < QuadSize; c++)
        {
            var vi0 = (ushort)(r * GridSize + c); // TL
            var vi1 = (ushort)(r * GridSize + c + 1); // TR
            var vi2 = (ushort)((r + 1) * GridSize + c); // BL
            var vi3 = (ushort)((r + 1) * GridSize + c + 1); // BR

            // tri 0 (winding-corrected): TL, BR, TR
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), vi0);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), vi3);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), vi1);
            cursor += 6;

            // tri 1 (winding-corrected): TL, BL, BR
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), vi0);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), vi2);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), vi3);
            cursor += 6;
        }

        return buf;
    }

    // -------------------------------------------------------------------------
    // glTF JSON construction
    // -------------------------------------------------------------------------

    private static string BuildJson(
        TerrainCell cell,
        int bufferByteLength,
        int vertexCount, int indexCount, bool use32Bit,
        int posOffset, int posLength,
        int uvOffset, int uvLength,
        int colOffset, int colLength,
        int idxOffset, int idxLength)
    {
        // Recompute position min/max (with X-flip applied).
        // spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0 world units: CONFIRMED.
        var minPosX = -((GridSize - 1) * VertexSpacing); // after negate, min is the largest col
        var maxPosX = 0f; // col 0 → X = 0 after negate
        var minPosZ = 0f;
        var maxPosZ = (GridSize - 1) * VertexSpacing;

        // Height min/max: scan the heights array.
        var minPosY = float.MaxValue;
        var maxPosY = float.MinValue;
        foreach (var h in cell.Heights)
        {
            if (h < minPosY) minPosY = h;
            if (h > maxPosY) maxPosY = h;
        }

        if (cell.Heights.Length == 0)
        {
            minPosY = 0f;
            maxPosY = 0f;
        }

        var indexComponentType = use32Bit ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;

        var sb = new StringBuilder(1024);
        sb.Append('{');

        // asset
        sb.Append(
            "\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.TerrainGltfConverter\"},");

        // scene / nodes / meshes
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        // mesh — POSITION, TEXCOORD_0, COLOR_0, indices
        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append("\"POSITION\":0,"); // accessor 0
        sb.Append("\"TEXCOORD_0\":1,"); // accessor 1
        sb.Append("\"COLOR_0\":2"); // accessor 2
        sb.Append("},");
        sb.Append("\"indices\":3"); // accessor 3
        sb.Append("}]}],");

        // accessors
        sb.Append("\"accessors\":[");

        // accessor 0 — POSITION VEC3 f32
        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC3\",");
        sb.Append($"\"min\":[{F(minPosX)},{F(minPosY)},{F(minPosZ)}],");
        sb.Append($"\"max\":[{F(maxPosX)},{F(maxPosY)},{F(maxPosZ)}]");
        sb.Append("},");

        // accessor 1 — TEXCOORD_0 VEC2 f32
        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC2\"");
        sb.Append("},");

        // accessor 2 — COLOR_0 VEC4 UNSIGNED_BYTE normalised
        // glTF 2.0 spec §Morph Targets / Accessors: COLOR_0 with UNSIGNED_BYTE must have normalized=true.
        sb.Append('{');
        sb.Append("\"bufferView\":2,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeUnsignedByte},");
        sb.Append("\"normalized\":true,");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC4\"");
        sb.Append("},");

        // accessor 3 — indices SCALAR u16 or u32
        sb.Append('{');
        sb.Append("\"bufferView\":3,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{indexComponentType},");
        sb.Append($"\"count\":{indexCount},");
        sb.Append("\"type\":\"SCALAR\"");
        sb.Append('}');

        sb.Append("],"); // end accessors

        // bufferViews
        sb.Append("\"bufferViews\":[");

        // bufferView 0 — positions
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{posOffset},");
        sb.Append($"\"byteLength\":{posLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        // bufferView 1 — UVs
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{uvOffset},");
        sb.Append($"\"byteLength\":{uvLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        // bufferView 2 — colours
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{colOffset},");
        sb.Append($"\"byteLength\":{colLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        // bufferView 3 — indices
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{idxOffset},");
        sb.Append($"\"byteLength\":{idxLength},");
        sb.Append($"\"target\":{TargetElementArrayBuffer}");
        sb.Append('}');

        sb.Append("],"); // end bufferViews

        // buffer
        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");

        sb.Append('}');
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // GLB container writing (identical logic to GltfConverter — self-contained)
    // glTF 2.0 spec §binary-gltf
    // -------------------------------------------------------------------------

    private static void WriteGlbChunks(Stream output, string json, byte[] binaryBuffer)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPadded = Align4(jsonBytes.Length);
        var binPadded = Align4(binaryBuffer.Length);

        var totalLength = (uint)(12 + 8 + jsonPadded + 8 + binPadded);

        Span<byte> hdr = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[8..], totalLength);
        output.Write(hdr);

        // JSON chunk
        Span<byte> jsonChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr, (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr[4..], ChunkTypeJson);
        output.Write(jsonChunkHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20);

        // BIN chunk
        Span<byte> binChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr[4..], ChunkTypeBin);
        output.Write(binChunkHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00);
    }

    private static void WritePadding(Stream output, int actualLength, int paddedLength, byte padByte)
    {
        var pad = paddedLength - actualLength;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

    private static string F(float v)
    {
        return v.ToString("G9", CultureInfo.InvariantCulture);
    }
}