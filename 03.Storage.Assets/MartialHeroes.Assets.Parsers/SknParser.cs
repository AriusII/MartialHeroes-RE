using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.skn</c> binary skinned mesh files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh
/// <para>
/// All fields little-endian.  No magic bytes at file start.
/// Layout: header (id_a u32, id_b u32, name LenStr) → face table → vertex table → weight table.
/// </para>
/// <para>
/// IMPORTANT: the on-disk vertex record is normal-first then position-second.  The parser
/// re-orders these fields when populating <see cref="SkinnedMesh.Positions"/> and
/// <see cref="SkinnedMesh.Normals"/>.
/// spec: Docs/RE/formats/mesh.md §Vertex record — "IMPORTANT: on-disk layout is normal first,
/// then position": CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class SknParser
{
    // Per-record sizes in bytes, all CONFIRMED by spec.
    // spec: Docs/RE/formats/mesh.md §Face record — "36 bytes (3 corners × 12 bytes each)": CONFIRMED.
    private const int CornerStride = 12; // u32 vertIdx + f32 u + f32 v = 12 bytes
    private const int FaceStride   = 36; // 3 corners × 12 bytes = 36 bytes

    // spec: Docs/RE/formats/mesh.md §Vertex record — "24 bytes (6 floats)": CONFIRMED.
    private const int VertexStride = 24; // 6 × f32 = 24 bytes

    // spec: Docs/RE/formats/mesh.md §Weight record — "12 bytes": CONFIRMED.
    private const int WeightStride = 12; // u32 vertIdx + u32 boneIdx + f32 weight = 12 bytes

    /// <summary>
    /// Parses the raw bytes of a <c>.skn</c> file into a <see cref="SkinnedMesh"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded skinned mesh.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun.
    /// </exception>
    public static SkinnedMesh Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static SkinnedMesh Parse(ReadOnlySpan<byte> data)
    {
        int offset = 0;

        // --- Header ---
        // spec: Docs/RE/formats/mesh.md §Header — id_a @ +0, id_b @ +4: CONFIRMED.

        uint idA = ReadU32LE(data, ref offset, "id_a");
        uint idB = ReadU32LE(data, ref offset, "id_b");

        // LenStr name — variable length.
        // spec: Docs/RE/formats/mesh.md §Header — name: CONFIRMED (presence); UNVERIFIED (encoding).
        string name = LenStrReader.Read(data, ref offset);

        // --- Face table ---
        // spec: Docs/RE/formats/mesh.md §Face table — face_count u32 LE: CONFIRMED.
        uint faceCount = ReadU32LE(data, ref offset, "face_count");

        // Validate buffer length for face data.
        long faceDataBytes = (long)faceCount * FaceStride;
        if (offset + faceDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn face table truncated: face_count={faceCount} requires {faceDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        // Each face has 3 corners.
        // spec: Docs/RE/formats/mesh.md §Face table — face_data: CONFIRMED.
        int cornerCount = checked((int)(faceCount * 3));
        SknCorner[] corners = new SknCorner[cornerCount];

        for (int f = 0; f < (int)faceCount; f++)
        {
            for (int c = 0; c < 3; c++)
            {
                // Corner sub-record: vertIdx u32 + uv_u f32 + uv_v f32 = 12 bytes.
                // spec: Docs/RE/formats/mesh.md §Face record — corner sub-record @ each 12 bytes: CONFIRMED.
                uint  vIdx = ReadU32LE(data, ref offset, $"face[{f}].corner[{c}].vertex_index");
                float uvU  = ReadF32LE(data, ref offset, $"face[{f}].corner[{c}].uv_u");
                // V-flip: engine applies 1.0 - uv_v when building the render vertex.
                // spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v". CONFIRMED.
                float uvVDisk = ReadF32LE(data, ref offset, $"face[{f}].corner[{c}].uv_v");
                float uvV     = 1.0f - uvVDisk;

                corners[f * 3 + c] = new SknCorner(vIdx, uvU, uvV);
            }
        }

        // --- Vertex table ---
        // spec: Docs/RE/formats/mesh.md §Vertex table — vertex_count u32 LE: CONFIRMED.
        uint vertexCount = ReadU32LE(data, ref offset, "vertex_count");

        long vertexDataBytes = (long)vertexCount * VertexStride;
        if (offset + vertexDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn vertex table truncated: vertex_count={vertexCount} requires {vertexDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        Vec3[] positions = new Vec3[vertexCount];
        Vec3[] normals   = new Vec3[vertexCount];

        for (int v = 0; v < (int)vertexCount; v++)
        {
            // On-disk layout: normal (sub-offsets 0–11) THEN position (sub-offsets 12–23).
            // IMPORTANT: re-order when building the output.
            // spec: Docs/RE/formats/mesh.md §Vertex record — "normal first, then position": CONFIRMED.
            float normX = ReadF32LE(data, ref offset, $"vertex[{v}].normal_x");  // sub-offset 0
            float normY = ReadF32LE(data, ref offset, $"vertex[{v}].normal_y");  // sub-offset 4
            float normZ = ReadF32LE(data, ref offset, $"vertex[{v}].normal_z");  // sub-offset 8
            float posX  = ReadF32LE(data, ref offset, $"vertex[{v}].pos_x");     // sub-offset 12
            float posY  = ReadF32LE(data, ref offset, $"vertex[{v}].pos_y");     // sub-offset 16
            float posZ  = ReadF32LE(data, ref offset, $"vertex[{v}].pos_z");     // sub-offset 20

            positions[v] = new Vec3(posX, posY, posZ);
            normals[v]   = new Vec3(normX, normY, normZ);
        }

        // --- Weight / skin table ---
        // spec: Docs/RE/formats/mesh.md §Weight / skin table — weight_count u32 LE: CONFIRMED.
        uint weightCount = ReadU32LE(data, ref offset, "weight_count");

        long weightDataBytes = (long)weightCount * WeightStride;
        if (offset + weightDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn weight table truncated: weight_count={weightCount} requires {weightDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        SknWeight[] weights = new SknWeight[weightCount];
        for (int w = 0; w < (int)weightCount; w++)
        {
            // spec: Docs/RE/formats/mesh.md §Weight record — vertIdx u32 + boneIdx u32 + weight f32 = 12 bytes: CONFIRMED.
            uint  wVertIdx  = ReadU32LE(data, ref offset, $"weight[{w}].vertex_index");
            uint  wBoneIdx  = ReadU32LE(data, ref offset, $"weight[{w}].bone_index");
            float wVal      = ReadF32LE(data, ref offset, $"weight[{w}].weight");
            weights[w] = new SknWeight(wVertIdx, wBoneIdx, wVal);
        }

        return new SkinnedMesh
        {
            IdA        = idA,
            IdB        = idB,
            Name       = name,
            FaceCount  = faceCount,
            Corners    = corners,
            Positions  = positions,
            Normals    = normals,
            Weights    = weights,
        };
    }

    // -------------------------------------------------------------------------
    // Private binary reader helpers (little-endian, bounds-checked)
    // -------------------------------------------------------------------------

    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".skn parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }

    private static float ReadF32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".skn parse error: buffer truncated reading '{fieldName}' f32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        float value = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}
