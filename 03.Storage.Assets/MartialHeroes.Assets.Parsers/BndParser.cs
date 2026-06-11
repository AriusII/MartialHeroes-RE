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
/// Layout: actor_id u32 | actor_name LenStr | bone_count u32 | bone_count × 36-byte bone records.
/// </para>
/// <para>
/// spec: Docs/RE/formats/mesh.md §Bone array — CORRECTION:
///   "The on-disk record is 36 bytes. There is no uncharacterized trailing region on disk.
///    Any parser code written against the 72-byte on-disk assumption will over-read by 36 bytes
///    per bone and must be corrected." CONFIRMED.
/// </para>
/// <para>
/// spec: Docs/RE/formats/mesh.md §Header — bone_count:
///   "On-disk representation is a full u32; only the low byte is stored to the in-memory count
///    field, giving an effective maximum of 255 bones." CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class BndParser
{
    // On-disk bone record is 36 bytes — all fields characterized.
    // spec: Docs/RE/formats/mesh.md §Bone array — "Total on-disk per bone: 36 bytes." CONFIRMED.
    private const int BoneRecordStride = 36;

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

        // actor_id u32 LE @ +0
        // spec: Docs/RE/formats/mesh.md §Header — actor_id @ +0 u32 LE: CONFIRMED.
        uint actorId = ReadU32LE(data, ref offset, "actor_id");

        // actor_name LenStr — 4-byte u32 LE prefix + N bytes of body, no null terminator.
        // spec: Docs/RE/formats/mesh.md §Header — actor_name LenStr: CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr):
        //   "The prefix is a 4-byte little-endian u32." CONFIRMED.
        string actorName = LenStrReader.Read(data, ref offset);

        // bone_count u32 LE — full 4-byte read; only the low byte is the effective count.
        // spec: Docs/RE/formats/mesh.md §Header — bone_count:
        //   "On-disk representation is a full u32; only the low byte is stored to the in-memory
        //    count field, giving an effective maximum of 255 bones." CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Header — NOTE:
        //   "A previous revision described bone_count as a 1-byte on-disk field. That was
        //    incorrect. The loader reads a full 4-byte u32 in binary mode." CONFIRMED.
        uint boneCountRaw = ReadU32LE(data, ref offset, "bone_count");
        int  boneCount    = (int)(boneCountRaw & 0xFF); // low byte only

        // Validate buffer length for bone records.
        // spec: Docs/RE/formats/mesh.md §Bone array — "36 bytes per record": CONFIRMED.
        long boneDataBytes = (long)boneCount * BoneRecordStride;
        if (offset + boneDataBytes > data.Length)
            throw new InvalidDataException(
                $".bnd bone array truncated: bone_count={boneCount} requires {boneDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        Bone[] bones = new Bone[boneCount];

        for (int b = 0; b < boneCount; b++)
        {
            int boneStart = offset;

            // sub-offset 0: self_id u32 LE (low byte is the effective bone ID)
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — self_id @ +0: CONFIRMED.
            uint selfId = ReadU32LE(data, ref offset, $"bone[{b}].self_id");

            // sub-offset 4: parent_id u32 LE (low byte is the effective parent ID)
            // Root bone: self_id == 0 && parent_id == 0 (both low bytes zero).
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — parent_id @ +4: CONFIRMED.
            // spec: Docs/RE/formats/mesh.md §Root bone sentinel: CONFIRMED.
            uint parentId = ReadU32LE(data, ref offset, $"bone[{b}].parent_id");

            // sub-offset 8: local_translation f32[3] LE — X at +8, Y at +12, Z at +16
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_translation @ +8: CONFIRMED.
            float tX = ReadF32LE(data, ref offset, $"bone[{b}].translation.x");
            float tY = ReadF32LE(data, ref offset, $"bone[{b}].translation.y");
            float tZ = ReadF32LE(data, ref offset, $"bone[{b}].translation.z");

            // sub-offset 20: local_rotation f32[4] LE — XYZW order (scalar W at +32)
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_rotation @ +20: CONFIRMED.
            // spec: Docs/RE/formats/mesh.md §Quaternion component order:
            //   "XYZW order: X at sub-offset +20, Y at +24, Z at +28, W (scalar) at +32." CONFIRMED.
            float rX = ReadF32LE(data, ref offset, $"bone[{b}].rotation.x");
            float rY = ReadF32LE(data, ref offset, $"bone[{b}].rotation.y");
            float rZ = ReadF32LE(data, ref offset, $"bone[{b}].rotation.z");
            float rW = ReadF32LE(data, ref offset, $"bone[{b}].rotation.w");

            // Confirm we consumed exactly BoneRecordStride bytes (36) for this bone.
            // spec: Docs/RE/formats/mesh.md §Bone array — "Total on-disk per bone: 36 bytes." CONFIRMED.
            System.Diagnostics.Debug.Assert(
                offset - boneStart == BoneRecordStride,
                $"BndParser: bone[{b}] consumed {offset - boneStart} bytes, expected {BoneRecordStride}.");

            bones[b] = new Bone(
                selfId:      selfId,
                parentId:    parentId,
                translation: new Vec3(tX, tY, tZ),
                rotation:    new Quat(rX, rY, rZ, rW));
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
