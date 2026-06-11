using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Converts <see cref="StaticMesh"/> (and optionally <see cref="SkinnedMesh"/> + <see cref="Skeleton"/>)
/// to a self-contained GLB (binary glTF 2.0) stream.
///
/// Coordinate-system conventions handled:
///   Legacy format uses a left-handed Y-up coordinate system (not formally confirmed by spec,
///   but matches the D3D9 default). glTF 2.0 mandates right-handed Y-up.
///   Conversion: negate the X component of every position and normal to flip handedness.
///   spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED positions.
///   glTF 2.0 spec §3.4 (coordinate system): right-handed, Y-up, -Z forward.
///
///   UV origin: the parser already stores V = 1.0 - v_on_disk so the UV origin is
///   bottom-left, which matches glTF / OpenGL convention. No further V-flip is needed.
///   spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v". CONFIRMED.
///
///   Index representation: parser carries u16 indices (max 65535 per mesh).
///   If any index exceeds 65535 the emitter upgrades to u32 automatically.
///   spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
///
/// Output is DETERMINISTIC: no timestamps, no GUIDs, no random ordering.
/// </summary>
public static class GltfConverter
{
    // -------------------------------------------------------------------------
    // glTF / GLB constants
    // glTF 2.0 spec §binary-gltf §chunks
    // -------------------------------------------------------------------------

    /// <summary>GLB magic = ASCII "glTF" (LE u32). glTF 2.0 spec §binary-gltf §Header.</summary>
    private const uint GlbMagic = 0x46546C67u; // 'glTF'

    /// <summary>GLB version = 2. glTF 2.0 spec §binary-gltf §Header.</summary>
    private const uint GlbVersion = 2u;

    /// <summary>GLB chunk type for JSON. glTF 2.0 spec §binary-gltf §Chunks.</summary>
    private const uint ChunkTypeJson = 0x4E4F534Au; // 'JSON'

    /// <summary>GLB chunk type for binary buffer. glTF 2.0 spec §binary-gltf §Chunks.</summary>
    private const uint ChunkTypeBin = 0x004E4942u; // 'BIN\0'

    // glTF accessor componentType constants — glTF 2.0 spec §Accessor.componentType
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;
    private const int ComponentTypeFloat = 5126;

    // glTF buffer view target constants — glTF 2.0 spec §BufferView.target
    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a GLB containing the given static mesh to <paramref name="output"/>.
    /// The stream does not need to be seekable.
    /// </summary>
    public static void WriteGlb(StaticMesh mesh, Stream output)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        bool use32BitIndices = mesh.Positions.Length > ushort.MaxValue;

        byte[] binaryBuffer = BuildBinaryBuffer(mesh, use32BitIndices,
            out int posOffset, out int posLength,
            out int uvOffset, out int uvLength,
            out int idxOffset, out int idxLength);

        string json = BuildJson(mesh, binaryBuffer.Length, use32BitIndices,
            posOffset, posLength, uvOffset, uvLength, idxOffset, idxLength);

        WriteGlbChunks(output, json, binaryBuffer);
    }

    // -------------------------------------------------------------------------
    // Binary buffer construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Packs positions (VEC3 f32), UVs (VEC2 f32), and indices (u16 or u32) into one buffer.
    /// Each section is 4-byte aligned as required by glTF 2.0 spec §BufferView.byteOffset alignment.
    /// </summary>
    private static byte[] BuildBinaryBuffer(
        StaticMesh mesh, bool use32Bit,
        out int posOffset, out int posLength,
        out int uvOffset, out int uvLength,
        out int idxOffset, out int idxLength)
    {
        int vertexCount = mesh.Positions.Length;
        int indexCount = mesh.Indices.Length;

        posLength = vertexCount * 3 * sizeof(float); // VEC3 f32
        uvLength = vertexCount * 2 * sizeof(float); // VEC2 f32
        int indexStride = use32Bit ? sizeof(uint) : sizeof(ushort);
        idxLength = indexCount * indexStride;

        // Align each section to 4 bytes
        posOffset = 0;
        int posEnd = posOffset + posLength;
        uvOffset = Align4(posEnd);
        int uvEnd = uvOffset + uvLength;
        idxOffset = Align4(uvEnd);
        int bufSize = Align4(idxOffset + idxLength);

        byte[] buf = new byte[bufSize];

        // Write positions
        // Coordinate flip: negate X to convert left-handed D3D9 → right-handed glTF.
        // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED.
        // glTF 2.0 spec §3.4: right-handed Y-up.
        int cursor = posOffset;
        for (int i = 0; i < vertexCount; i++)
        {
            Vec3 p = mesh.Positions[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -p.X); // flip X
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), p.Z);
            cursor += 12;
        }

        // Write UVs
        // V is already bottom-left (parser applied 1.0 - v_on_disk).
        // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: CONFIRMED.
        cursor = uvOffset;
        for (int i = 0; i < vertexCount; i++)
        {
            Vec2 uv = mesh.Uvs[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), uv.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), uv.Y);
            cursor += 8;
        }

        // Write indices
        // Winding order: the handedness flip (negate X) also reverses winding order.
        // To maintain counter-clockwise front faces in glTF we swap index[1] and index[2]
        // in each triangle, reversing the winding.
        // spec: Docs/RE/formats/mesh.md §Index list — "winding order as stored; no winding reversal on load". CONFIRMED.
        // glTF 2.0 spec §3.7.2.1: counter-clockwise winding defines the front face.
        cursor = idxOffset;
        for (int tri = 0; tri < indexCount / 3; tri++)
        {
            ushort i0 = mesh.Indices[tri * 3 + 0];
            ushort i1 = mesh.Indices[tri * 3 + 1];
            ushort i2 = mesh.Indices[tri * 3 + 2];

            if (use32Bit)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor), i0);
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 4), i2); // swapped
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 8), i1); // swapped
                cursor += 12;
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), i0);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), i2); // swapped
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), i1); // swapped
                cursor += 6;
            }
        }

        return buf;
    }

    // -------------------------------------------------------------------------
    // glTF JSON construction
    // -------------------------------------------------------------------------

    private static string BuildJson(
        StaticMesh mesh, int bufferByteLength, bool use32Bit,
        int posOffset, int posLength,
        int uvOffset, int uvLength,
        int idxOffset, int idxLength)
    {
        int vertexCount = mesh.Positions.Length;
        int indexCount = mesh.Indices.Length;

        // Compute accessor min/max for positions (required by glTF spec §Accessor).
        ComputePositionMinMax(mesh.Positions,
            out float minX, out float minY, out float minZ,
            out float maxX, out float maxY, out float maxZ);

        var sb = new StringBuilder(512);
        sb.Append('{');

        // asset
        sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping\"},");

        // scene
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        // meshes — one primitive with POSITION, TEXCOORD_0, and indices
        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append("\"POSITION\":0,"); // accessor 0
        sb.Append("\"TEXCOORD_0\":1"); // accessor 1
        sb.Append("},");
        sb.Append("\"indices\":2"); // accessor 2
        sb.Append("}]}],");

        // accessors
        sb.Append("\"accessors\":[");

        // accessor 0 — positions VEC3 f32
        // X has been negated; update min/max accordingly.
        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC3\",");
        // min/max: X was negated so swap and negate
        sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],");
        sb.Append($"\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
        sb.Append("},");

        // accessor 1 — UVs VEC2 f32  (no min/max required for TEXCOORD)
        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC2\"");
        sb.Append("},");

        // accessor 2 — indices SCALAR u16 or u32
        int indexComponentType = use32Bit ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;
        sb.Append('{');
        sb.Append("\"bufferView\":2,");
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

        // bufferView 2 — indices
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
    // GLB container writing
    // glTF 2.0 spec §binary-gltf
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes the 12-byte GLB header followed by the JSON chunk and BIN chunk.
    /// Chunk data is padded to 4-byte boundaries (JSON with 0x20 spaces, BIN with 0x00 bytes).
    /// glTF 2.0 spec §binary-gltf §Padding.
    /// </summary>
    private static void WriteGlbChunks(Stream output, string json, byte[] binaryBuffer)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonPadded = Align4(jsonBytes.Length);
        int binPadded = Align4(binaryBuffer.Length);

        // Total file length:
        // 12 (header) + 8 (JSON chunk header) + jsonPadded + 8 (BIN chunk header) + binPadded
        uint totalLength = (uint)(12 + 8 + jsonPadded + 8 + binPadded);

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
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20); // space-padded

        // BIN chunk
        Span<byte> binChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr[4..], ChunkTypeBin);
        output.Write(binChunkHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00); // zero-padded
    }

    private static void WritePadding(Stream output, int actualLength, int paddedLength, byte padByte)
    {
        int pad = paddedLength - actualLength;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int value) => (value + 3) & ~3;

    /// <summary>
    /// Computes min/max of the original (non-X-flipped) positions.
    /// The caller negates X for the glTF accessor min/max.
    /// </summary>
    private static void ComputePositionMinMax(
        Vec3[] positions,
        out float minX, out float minY, out float minZ,
        out float maxX, out float maxY, out float maxZ)
    {
        if (positions.Length == 0)
        {
            minX = minY = minZ = 0f;
            maxX = maxY = maxZ = 0f;
            return;
        }

        minX = maxX = positions[0].X;
        minY = maxY = positions[0].Y;
        minZ = maxZ = positions[0].Z;

        for (int i = 1; i < positions.Length; i++)
        {
            Vec3 p = positions[i];
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }
    }

    /// <summary>Formats a float with enough precision to be round-trippable in JSON.</summary>
    private static string F(float v) =>
        v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);

    // -------------------------------------------------------------------------
    // SkinnedMesh overload — partial: base mesh only, skinning is TODO
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a GLB containing the base geometry of a skinned mesh.
    /// Positions, UVs, and triangle indices are emitted using the unique vertex set
    /// implied by the corner table.
    /// </summary>
    /// <remarks>
    /// TODO: skinning — JOINTS_0, WEIGHTS_0 accessors, skin node, and inverse-bind
    /// matrices are not yet emitted.  The <see cref="Skeleton"/> parameter is accepted
    /// but ignored pending the following spec gap being resolved:
    ///   - Bone quaternion component order (XYZW vs WXYZ) is UNVERIFIED.
    ///     spec: Docs/RE/formats/mesh.md §BndBone record — rotation: MEDIUM confidence.
    ///   - Bone record bytes 36–71 are entirely uncharacterized; inverse-bind matrices
    ///     may live there.
    ///     spec: Docs/RE/formats/mesh.md §BndBone record — unknown_36 @ +36: UNVERIFIED.
    ///   - Root bone sentinel value is UNVERIFIED.
    ///     spec: Docs/RE/formats/mesh.md §BndBone record — parent_id: MEDIUM confidence.
    /// When the spec gaps above are resolved and a parser type exposing inverse-bind
    /// matrices is available, implement full skinning here.
    /// </remarks>
    public static void WriteGlb(SkinnedMesh mesh, Skeleton? skeleton, Stream output)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        // Build a StaticMesh from the corner table (expand unique vertices).
        StaticMesh staticView = ExpandSkinnedMeshToStatic(mesh);
        WriteGlb(staticView, output);
    }

    /// <summary>
    /// Expands the face/corner table of a skinned mesh into a flat array of unique
    /// (position, UV) vertices suitable for static mesh emission.
    /// The corner table uses an index into the vertex array for position, while UV
    /// is stored per-corner; this produces a deduplication by (vertexIndex, u, v).
    /// </summary>
    private static StaticMesh ExpandSkinnedMeshToStatic(SkinnedMesh mesh)
    {
        // Each corner has a vertex index (position) and its own UV.
        // Multiple corners can reference the same vertex with different UVs.
        // Build a deduplicated (vertexIndex, u, v) → new index map.
        var vertexMap = new Dictionary<(uint vi, float u, float v), ushort>(mesh.Corners.Length);
        var positions = new List<Vec3>(mesh.Corners.Length);
        var uvs = new List<Vec2>(mesh.Corners.Length);
        var indices = new List<ushort>(mesh.Corners.Length);

        foreach (SknCorner corner in mesh.Corners)
        {
            var key = (corner.VertexIndex, corner.UvU, corner.UvV);
            if (!vertexMap.TryGetValue(key, out ushort newIdx))
            {
                newIdx = checked((ushort)positions.Count);
                positions.Add(mesh.Positions[(int)corner.VertexIndex]);
                uvs.Add(new Vec2(corner.UvU, corner.UvV));
                vertexMap[key] = newIdx;
            }

            indices.Add(newIdx);
        }

        return new StaticMesh
        {
            Positions = positions.ToArray(),
            Uvs = uvs.ToArray(),
            Indices = indices.ToArray(),
        };
    }
}