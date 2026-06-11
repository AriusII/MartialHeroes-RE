using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.bnd</c> binary bind-pose skeleton files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Format: .bnd — binary bind-pose skeleton
/// <para>
/// All fields little-endian.  No magic bytes at file start.
/// Layout: actor_id u32, actor_name LenStr, bone_count u8, then bone_count × 72-byte bone records.
/// </para>
/// <para>
/// Each bone record is 72 bytes: 36 bytes of identified fields followed by 36 bytes that are
/// entirely UNVERIFIED (kept verbatim as <see cref="BoneTrailingBytes"/>).
/// spec: Docs/RE/formats/mesh.md §BndBone record — "remaining 36 bytes are uncharacterized": UNVERIFIED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class BndParser
{
    // Bone record constants.
    // spec: Docs/RE/formats/mesh.md §Bone array — "72-byte on-disk record size is confirmed
    // by the loader's pointer arithmetic (stride of 18 dwords × 4 bytes = 72 bytes)": CONFIRMED.
    private const int BoneRecordStride = 72;
    private const int BoneKnownBytes   = 36; // first 36 bytes — identified fields
    private const int BoneUnknownBytes = 36; // last 36 bytes  — UNVERIFIED

    /// <summary>
    /// Parses the raw bytes of a <c>.bnd</c> file into a <see cref="Skeleton"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded skeleton.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun.
    /// </exception>
    public static Skeleton Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static Skeleton Parse(ReadOnlySpan<byte> data)
    {
        int offset = 0;

        // --- Header ---
        // spec: Docs/RE/formats/mesh.md §Header — actor_id @ +0 u32 LE: CONFIRMED.
        uint actorId = ReadU32LE(data, ref offset, "actor_id");

        // LenStr actor_name — variable length.
        // spec: Docs/RE/formats/mesh.md §Header — actor_name: CONFIRMED (presence); UNVERIFIED (encoding).
        string actorName = LenStrReader.Read(data, ref offset);

        // bone_count — 1 byte.
        // spec: Docs/RE/formats/mesh.md §Header — bone_count: "single unsigned byte; maximum 255". CONFIRMED.
        if (offset >= data.Length)
            throw new InvalidDataException(
                $".bnd parse error: buffer truncated reading 'bone_count' u8 at offset {offset} " +
                $"(buffer length {data.Length}).");
        byte boneCount = data[offset++];

        // Validate buffer length for bone records.
        long boneDataBytes = (long)boneCount * BoneRecordStride;
        if (offset + boneDataBytes > data.Length)
            throw new InvalidDataException(
                $".bnd bone array truncated: bone_count={boneCount} requires {boneDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        Bone[] bones = new Bone[boneCount];

        for (int b = 0; b < boneCount; b++)
        {
            int boneStart = offset;

            // sub-offset 0: self_id u32 LE
            // spec: Docs/RE/formats/mesh.md §BndBone record — self_id @ +0: MEDIUM confidence.
            // NOTE: only the low byte is observed to be consumed by the original client.
            uint selfId = ReadU32LE(data, ref offset, $"bone[{b}].self_id");

            // sub-offset 4: parent_id u32 LE
            // spec: Docs/RE/formats/mesh.md §BndBone record — parent_id @ +4: MEDIUM confidence.
            // NOTE: only the low byte is observed to be consumed. Root bone sentinel: UNVERIFIED.
            uint parentId = ReadU32LE(data, ref offset, $"bone[{b}].parent_id");

            // sub-offset 8: translation f32[3] LE
            // spec: Docs/RE/formats/mesh.md §BndBone record — translation @ +8: MEDIUM confidence.
            float tX = ReadF32LE(data, ref offset, $"bone[{b}].translation.x");
            float tY = ReadF32LE(data, ref offset, $"bone[{b}].translation.y");
            float tZ = ReadF32LE(data, ref offset, $"bone[{b}].translation.z");

            // sub-offset 20: rotation f32[4] LE
            // spec: Docs/RE/formats/mesh.md §BndBone record — rotation @ +20: MEDIUM confidence.
            // Component order (XYZW vs. WXYZ) is UNVERIFIED — no sample available.
            float rX = ReadF32LE(data, ref offset, $"bone[{b}].rotation.x");
            float rY = ReadF32LE(data, ref offset, $"bone[{b}].rotation.y");
            float rZ = ReadF32LE(data, ref offset, $"bone[{b}].rotation.z");
            float rW = ReadF32LE(data, ref offset, $"bone[{b}].rotation.w");

            // sub-offset 36: unknown 36 bytes — UNVERIFIED
            // spec: Docs/RE/formats/mesh.md §BndBone record — unknown_36 @ +36: UNVERIFIED.
            ReadOnlySpan<byte> trailingSlice = data.Slice(offset, BoneUnknownBytes);
            var trailing = new BoneTrailingBytes(trailingSlice);
            offset += BoneUnknownBytes;

            // Confirm we consumed exactly BoneRecordStride bytes for this bone.
            System.Diagnostics.Debug.Assert(
                offset - boneStart == BoneRecordStride,
                $"BndParser: bone[{b}] consumed {offset - boneStart} bytes, expected {BoneRecordStride}.");

            bones[b] = new Bone(
                selfId:      selfId,
                parentId:    parentId,
                translation: new Vec3(tX, tY, tZ),
                rotation:    new Quat(rX, rY, rZ, rW),
                unknown36:   trailing);
        }

        return new Skeleton
        {
            ActorId   = actorId,
            ActorName = actorName,
            Bones     = bones,
        };
    }

    // -------------------------------------------------------------------------
    // Private binary reader helpers (little-endian, bounds-checked)
    // -------------------------------------------------------------------------

    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".bnd parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }

    private static float ReadF32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".bnd parse error: buffer truncated reading '{fieldName}' f32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        float value = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}
