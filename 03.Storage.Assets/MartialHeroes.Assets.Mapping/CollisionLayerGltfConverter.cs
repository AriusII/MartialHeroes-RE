using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Converts a <see cref="CollisionTriangleList"/> (.up upper terrain or .exd extra terrain)
/// to a self-contained GLB (binary glTF 2.0) stream.
///
/// Both .up and .exd share the identical 40-byte triangle record; this converter handles both.
///
/// Coordinate-system conventions:
///   The .up / .exd formats use Y-up world space (same D3D9 convention as all other terrain).
///   spec: Docs/RE/formats/terrain_layers.md §Overview — "Coordinate system: Y-up world space": CONFIRMED.
///   glTF 2.0 mandates right-handed Y-up.
///   Conversion: negate X to flip from left-handed to right-handed.
///   spec: Docs/RE/formats/mesh.md §Vertex list — same D3D9 convention: CONFIRMED.
///   glTF 2.0 spec §3.4.
///
///   Winding order:
///   Each <see cref="CollisionTriangle"/> stores three vertices in on-disk order.
///   After negating X the winding reverses; we swap v1 and v2 to restore CCW in glTF.
///   spec: Docs/RE/formats/terrain_layers.md §2.1 Triangle record — v1/v2/v3: CONFIRMED.
///   glTF 2.0 spec §3.7.2.1.
///
///   The plane_height field at record offset +0x24 is a scalar companion (equals vertex Y
///   in all flat-triangle samples); it is NOT emitted as a vertex attribute (it carries
///   no geometry information beyond what the three vertex positions already provide).
///   spec: Docs/RE/formats/terrain_layers.md §2.1 — plane_height @ +0x24: CONFIRMED (flat-triangle case).
///
///   Normals are NOT present on disk for .up / .exd triangles.
///   The NORMAL accessor is omitted; the consumer must compute face normals if needed.
/// </summary>
public static class CollisionLayerGltfConverter
{
    // -------------------------------------------------------------------------
    // glTF / GLB constants
    // glTF 2.0 spec §binary-gltf §chunks
    // -------------------------------------------------------------------------

    private const uint GlbMagic    = 0x46546C67u; // 'glTF'
    private const uint GlbVersion  = 2u;
    private const uint ChunkTypeJson = 0x4E4F534Au; // 'JSON'
    private const uint ChunkTypeBin  = 0x004E4942u; // 'BIN\0'

    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeFloat         = 5126;
    private const int TargetArrayBuffer          = 34962;
    private const int TargetElementArrayBuffer   = 34963;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a GLB containing one mesh primitive with the collision triangle geometry
    /// from <paramref name="triangleList"/> (.up or .exd source).
    /// The stream does not need to be seekable.
    /// </summary>
    /// <param name="triangleList">
    /// Parsed .up or .exd data from <c>TerrainLayerParsers</c>.
    /// </param>
    /// <param name="output">Destination stream.</param>
    public static void WriteGlb(CollisionTriangleList triangleList, Stream output)
    {
        ArgumentNullException.ThrowIfNull(triangleList);
        ArgumentNullException.ThrowIfNull(output);

        CollisionTriangle[] tris = triangleList.Triangles;
        int triCount   = tris.Length;
        int vertCount  = triCount * 3; // one unique vertex per triangle corner (no index sharing needed)
        int indexCount = triCount * 3; // indices 0,1,2, 3,4,5, …

        // For up to 65535 triangles × 3 vertices the count is at most 65535 * 3 = 196605,
        // which exceeds u16. We check and upgrade to u32 if required.
        // In practice observed triangle_count values are small (12 for .up, 2 for .exd).
        bool use32 = vertCount > ushort.MaxValue;

        // ---- Binary buffer layout: [positions VEC3 f32] [indices u16/u32] ----
        int posLen = vertCount * 3 * sizeof(float);
        int idxStride = use32 ? sizeof(uint) : sizeof(ushort);
        int idxLen = indexCount * idxStride;

        int posOff = 0;
        int idxOff = Align4(posOff + posLen);
        int bufSize = Align4(idxOff + idxLen);

        byte[] buf = new byte[bufSize];

        // ---- Fill positions ----
        // Each triangle contributes 3 vertices in on-disk order: v1, v2, v3.
        // X is negated for the left-handed → right-handed conversion.
        // spec: Docs/RE/formats/terrain_layers.md §2.1 Triangle record — v1/v2/v3 xyz: CONFIRMED.
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        int cursor = posOff;
        for (int t = 0; t < triCount; t++)
        {
            CollisionTriangle tri = tris[t];

            WriteVertex(buf, ref cursor, -tri.V1X, tri.V1Y, tri.V1Z, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            WriteVertex(buf, ref cursor, -tri.V2X, tri.V2Y, tri.V2Z, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            WriteVertex(buf, ref cursor, -tri.V3X, tri.V3Y, tri.V3Z, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        }

        // ---- Fill indices ----
        // Each triangle i → vertex indices [3i, 3i+1, 3i+2].
        // After X-flip the winding reverses; swap v1↔v2 (indices 1↔2) to restore CCW.
        // glTF 2.0 spec §3.7.2.1.
        cursor = idxOff;
        for (int t = 0; t < triCount; t++)
        {
            int base3 = t * 3;
            // Winding-swapped: [base3+0, base3+2, base3+1]
            if (use32)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor),     (uint)(base3 + 0));
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 4), (uint)(base3 + 2)); // swapped
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 8), (uint)(base3 + 1)); // swapped
                cursor += 12;
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor),     (ushort)(base3 + 0));
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), (ushort)(base3 + 2)); // swapped
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), (ushort)(base3 + 1)); // swapped
                cursor += 6;
            }
        }

        // ---- Build JSON ----
        string json = BuildJson(bufSize, vertCount, indexCount, use32,
            posOff, posLen, idxOff, idxLen,
            minX, minY, minZ, maxX, maxY, maxZ);

        // ---- Write GLB ----
        WriteGlbChunks(output, json, buf);
    }

    // -------------------------------------------------------------------------
    // JSON
    // -------------------------------------------------------------------------

    private static string BuildJson(
        int bufferByteLength,
        int vertCount, int indexCount, bool use32,
        int posOff, int posLen,
        int idxOff, int idxLen,
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ)
    {
        int indexComponentType = use32 ? 5125 /*UNSIGNED_INT*/ : ComponentTypeUnsignedShort;

        var sb = new StringBuilder(512);
        sb.Append('{');
        sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.CollisionLayerGltfConverter\"},");
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        // mesh — single primitive with POSITION and indices
        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{\"POSITION\":0},");
        sb.Append("\"indices\":1");
        sb.Append("}]}],");

        // accessors
        sb.Append("\"accessors\":[");

        // accessor 0 — POSITION VEC3 f32
        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertCount},");
        sb.Append("\"type\":\"VEC3\",");
        sb.Append($"\"min\":[{F(minX)},{F(minY)},{F(minZ)}],");
        sb.Append($"\"max\":[{F(maxX)},{F(maxY)},{F(maxZ)}]");
        sb.Append("},");

        // accessor 1 — indices SCALAR u16/u32
        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{indexComponentType},");
        sb.Append($"\"count\":{indexCount},");
        sb.Append("\"type\":\"SCALAR\"");
        sb.Append('}');

        sb.Append("],");

        // bufferViews
        sb.Append("\"bufferViews\":[");

        // bufferView 0 — positions
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{posOff},");
        sb.Append($"\"byteLength\":{posLen},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        // bufferView 1 — indices
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{idxOff},");
        sb.Append($"\"byteLength\":{idxLen},");
        sb.Append($"\"target\":{TargetElementArrayBuffer}");
        sb.Append('}');

        sb.Append("],");
        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");
        sb.Append('}');
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // GLB container writing
    // glTF 2.0 spec §binary-gltf
    // -------------------------------------------------------------------------

    private static void WriteGlbChunks(Stream output, string json, byte[] binaryBuffer)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonPadded = Align4(jsonBytes.Length);
        int binPadded  = Align4(binaryBuffer.Length);

        uint totalLength = (uint)(12 + 8 + jsonPadded + 8 + binPadded);

        Span<byte> hdr = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr,       GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..],  GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[8..],  totalLength);
        output.Write(hdr);

        Span<byte> jsonHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHdr,       (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHdr[4..],  ChunkTypeJson);
        output.Write(jsonHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20);

        Span<byte> binHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binHdr,      (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binHdr[4..], ChunkTypeBin);
        output.Write(binHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteVertex(
        byte[] buf, ref int cursor,
        float x, float y, float z,
        ref float minX, ref float minY, ref float minZ,
        ref float maxX, ref float maxY, ref float maxZ)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor),     x);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), z);
        cursor += 12;

        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
        if (z < minZ) minZ = z;
        if (z > maxZ) maxZ = z;
    }

    private static void WritePadding(Stream output, int actual, int padded, byte padByte)
    {
        int pad = padded - actual;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int v) => (v + 3) & ~3;

    private static string F(float v) =>
        v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
}
