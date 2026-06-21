using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Core;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

/// <summary>
///     Parser for <c>.skn</c> binary skinned mesh files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh
///     <para>
///         All fields little-endian.  No magic bytes at file start.
///         Layout: header (id_a u32, id_b u32, name LenStr) → face table → vertex table → weight table.
///     </para>
///     <para>
///         IMPORTANT: the on-disk vertex record is normal-first then position-second.  The parser
///         re-orders these fields when populating <see cref="SkinnedMesh.Positions" /> and
///         <see cref="SkinnedMesh.Normals" />.
///         spec: Docs/RE/formats/mesh.md §Vertex record — "IMPORTANT: on-disk layout is normal first,
///         then position": CONFIRMED.
///     </para>
///     <para>
///         ZERO rendering/engine dependencies.
///     </para>
/// </remarks>
public static class SknParser
{
    // Per-record sizes in bytes, all CONFIRMED by spec.
    // spec: Docs/RE/formats/mesh.md §Face record — "36 bytes (3 corners × 12 bytes each)": CONFIRMED.
    private const int CornerStride = 12; // u32 vertIdx + f32 u + f32 v = 12 bytes
    private const int FaceStride = 36; // 3 corners × 12 bytes = 36 bytes

    // spec: Docs/RE/formats/mesh.md §Vertex record — "24 bytes (6 floats)": CONFIRMED.
    private const int VertexStride = 24; // 6 × f32 = 24 bytes

    // spec: Docs/RE/formats/mesh.md §Weight record — "12 bytes": CONFIRMED.
    private const int WeightStride = 12; // u32 vertIdx + u32 boneIdx + f32 weight = 12 bytes

    /// <summary>
    ///     Parses the raw bytes of a <c>.skn</c> file into a <see cref="SkinnedMesh" />.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded skinned mesh.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown on truncation or buffer overrun.
    /// </exception>
    public static SkinnedMesh Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>
    ///     Parses from a <see cref="ReadOnlySpan{byte}" />.
    /// </summary>
    public static SkinnedMesh Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;

        // --- Header ---
        // spec: Docs/RE/formats/mesh.md §Header — id_a @ +0, id_b @ +4: CONFIRMED.

        var idA = ReadU32LE(data, ref offset, "id_a");
        var idB = ReadU32LE(data, ref offset, "id_b");

        // LenStr name — variable length.
        // spec: Docs/RE/formats/mesh.md §Header — name: CONFIRMED (presence); UNVERIFIED (encoding).
        var name = LenStrReader.Read(data, ref offset);

        // --- Face table ---
        // spec: Docs/RE/formats/mesh.md §Face table — face_count u32 LE: CONFIRMED.
        var faceCount = ReadU32LE(data, ref offset, "face_count");

        // Validate buffer length for face data (single pre-loop bounds check).
        var faceDataBytes = (long)faceCount * FaceStride;
        if (offset + faceDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn face table truncated: face_count={faceCount} requires {faceDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        // Each face has 3 corners.
        // spec: Docs/RE/formats/mesh.md §Face table — face_data: CONFIRMED.
        var cornerCount = checked((int)(faceCount * 3));
        var corners = new SknCorner[cornerCount];

        for (var f = 0; f < (int)faceCount; f++)
        for (var c = 0; c < 3; c++)
        {
            // Corner sub-record: vertex_index u32 + uv_u f32 + uv_v f32 = 12 bytes.
            // spec: Docs/RE/formats/skn.md §Face section — "3 corners × {u32 vertex_index, f32 uv_u, f32 uv_v}": CONFIRMED.
            // spec: Docs/RE/formats/mesh.md §Face record — corner sub-record @ each 12 bytes: CONFIRMED.
            // Bounds already checked above; read without per-element string allocation.

            // +0: vertex_index u32 — plain u32 index (binary-won: face0 sample = 2,3,4; small integers, NOT position floats).
            // spec: Docs/RE/formats/skn.md §Face section corner — "vertex_index: plain u32 index": CONFIRMED (sample-verified).
            var vIdx = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            var uvU = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            // V-flip: 1.0 − uv_v applied here so the output UV uses a bottom-left origin.
            // spec: Docs/RE/formats/skn.md §Face section corner — uv_v: "the engine applies 1.0 − uv_v
            //   when building the render vertex — the v-flip lives in the parser's render-vertex
            //   assembly, not in the bytes". CONFIRMED.
            // Architecture note: the original "render-vertex assembly" (combined face/vertex expand)
            // is the role of Assets.Mapping (GltfConverter.ExpandSkinnedVertices). However the V-flip
            // is applied here in the parser so that the SknCorner model carries bottom-left UVs that
            // the Mapping consumer (GltfConverter) can pass through without a further flip.
            // This is a deliberate cross-layer convention: both SknParser and GltfConverter must
            // agree on which layer applies the flip. The GltfConverter currently documents
            // "V already bottom-left from parser" (see GltfConverter.cs §UVs comment).
            var uvV = 1.0f - BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;

            corners[f * 3 + c] = new SknCorner(vIdx, uvU, uvV);
        }

        // --- Vertex table ---
        // spec: Docs/RE/formats/skn.md §Vertex section — Nvtx u32 LE + Nvtx × 24-byte records: CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Vertex table — vertex_count u32 LE: CONFIRMED.
        var vertexCount = ReadU32LE(data, ref offset, "vertex_count");

        var vertexDataBytes = (long)vertexCount * VertexStride;
        if (offset + vertexDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn vertex table truncated: vertex_count={vertexCount} requires {vertexDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var positions = new Vec3[vertexCount];
        var normals = new Vec3[vertexCount];

        for (var v = 0; v < (int)vertexCount; v++)
        {
            // On-disk layout: normal (sub-offsets 0–11) THEN position (sub-offsets 12–23).
            // IMPORTANT: re-order when building the output.
            // spec: Docs/RE/formats/mesh.md §Vertex record — "normal first, then position": CONFIRMED.
            // Bounds already checked above; read 6×f32 without per-element string allocation.
            var normX = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); // sub-offset 0
            offset += 4;
            var normY = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); // sub-offset 4
            offset += 4;
            var normZ = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); // sub-offset 8
            offset += 4;
            var posX = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); // sub-offset 12
            offset += 4;
            var posY = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); // sub-offset 16
            offset += 4;
            var posZ = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); // sub-offset 20
            offset += 4;

            positions[v] = new Vec3(posX, posY, posZ);
            normals[v] = new Vec3(normX, normY, normZ);
        }

        // --- Influence (weight) section ---
        // spec: Docs/RE/formats/skn.md §Influence (weight) section — Nweight u32 LE + Nweight × 12-byte records: CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Weight / skin table — weight_count u32 LE: CONFIRMED.
        var weightCount = ReadU32LE(data, ref offset, "weight_count");

        var weightDataBytes = (long)weightCount * WeightStride;
        if (offset + weightDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn weight table truncated: weight_count={weightCount} requires {weightDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var weights = new SknWeight[weightCount];
        for (var w = 0; w < (int)weightCount; w++)
        {
            // Influence record: vertex_index u32 + bone_id u32 + weight f32 = 12 bytes.
            // spec: Docs/RE/formats/skn.md §Influence record — 12 bytes: CONFIRMED (sample-verified).
            // spec: Docs/RE/formats/mesh.md §Weight record — 12 bytes, little-endian: CONFIRMED.
            // Bounds already checked above; read without per-element string allocation.

            // +0x00: vertex_index u32 — plain vertex index (binary-won: NOT a position key / float-bit compare).
            // spec: Docs/RE/formats/skn.md §Influence record +0x00 — "plain u32 vertex index": CONFIRMED (sample-verified).
            var wVertIdx = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            // +0x04: bone_id u32 — bone ID resolved base-relative (id − base_id) against the bound skeleton.
            // NOT a dense array subscript. spec: Docs/RE/formats/skn.md §Influence record +0x04 — "bone_id": CONFIRMED.
            // spec: Docs/RE/formats/mesh.md §Weight record — "bone_index is a bone ID resolved by id − base_id": CONFIRMED.
            var wBoneId = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            // +0x08: weight f32 — the character loader drops records below 0.01 and normalizes survivors to 1.0.
            // spec: Docs/RE/formats/skn.md §Per-vertex influence packing — "drop threshold 0.01, normalize": CONFIRMED.
            var wVal = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            weights[w] = new SknWeight(wVertIdx, wBoneId, wVal);
        }

        return new SkinnedMesh
        {
            IdA = idA,
            IdB = idB,
            Name = name,
            FaceCount = faceCount,
            Corners = corners,
            Positions = positions,
            Normals = normals,
            Weights = weights
        };
    }

    // -------------------------------------------------------------------------
    // Private binary reader helpers (little-endian, bounds-checked)
    // Used for header-level fields only (not hot per-element loops).
    // -------------------------------------------------------------------------

    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".skn parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        var value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}