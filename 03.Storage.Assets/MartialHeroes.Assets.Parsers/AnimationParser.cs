using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.mot</c> binary skeletal animation clip files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/animation.md §Header layout
/// <para>
/// All fields little-endian. No magic bytes at file start.
/// Layout: id_a u32 | id_b u32 | name LenStr | frame_count u32 | track_count u32 |
///         track_count × variable-length track records.
/// </para>
/// <para>
/// Fixed frame rate: 10 fps.
/// spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps." CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class AnimationParser
{
    // Each keyframe record is exactly 28 bytes: 3 × f32 translation + 4 × f32 rotation.
    // spec: Docs/RE/formats/animation.md §Keyframe record — "28 bytes, little-endian": CONFIRMED.
    private const int KeyframeStride = 28;

    // Each track preamble is 8 bytes: track_descriptor u32 + key_count u32.
    // spec: Docs/RE/formats/animation.md §Per-track record — "fixed 8-byte preamble": CONFIRMED.
    private const int TrackPreambleStride = 8;

    /// <summary>
    /// Parses the raw bytes of a <c>.mot</c> file into an <see cref="AnimationClip"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded animation clip.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun.
    /// </exception>
    public static AnimationClip Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static AnimationClip Parse(ReadOnlySpan<byte> data)
    {
        int offset = 0;

        // --- Header (Stage 1 + Stage 2 combined — single sequential read) ---
        // spec: Docs/RE/formats/animation.md §Two-stage loading:
        //   "A parser implementing Assets.Parsers should perform the equivalent of both stages
        //    in a single sequential read." CONFIRMED.

        // id_a u32 LE @ offset 0
        // spec: Docs/RE/formats/animation.md §Header layout — id_a @ +0, u32 LE: CONFIRMED.
        uint idA = ReadU32LE(data, ref offset, "id_a");

        // id_b u32 LE @ offset 4
        // spec: Docs/RE/formats/animation.md §Header layout — id_b @ +4, u32 LE: CONFIRMED.
        uint idB = ReadU32LE(data, ref offset, "id_b");

        // name LenStr — 4-byte u32 LE prefix + body (no null terminator on disk).
        // spec: Docs/RE/formats/animation.md §LenStr encoding:
        //   "The .mot LenStr wire format is UNVERIFIED by sample; implementors should apply
        //    the same 4-byte prefix convention documented in formats/mesh.md."
        // spec: Docs/RE/formats/animation.md §Known unknowns:
        //   "LenStr wire format in .mot (1-byte vs 4-byte prefix) — UNVERIFIED — assumed 4-byte
        //    by analogy with .skn/.bnd; confirm with a sample."
        // hypothesis — LenStr width unverified, see formats/animation.md §LenStr encoding.
        string name = LenStrReader.Read(data, ref offset);

        // frame_count u32 LE — follows immediately after the name string.
        // spec: Docs/RE/formats/animation.md §Header layout — frame_count u32 LE: CONFIRMED.
        // Duration in seconds = frame_count × 0.1 (10 fps fixed).
        // spec: Docs/RE/formats/animation.md §Timing — "Clip duration = frame_count × 0.1": CONFIRMED.
        uint frameCount = ReadU32LE(data, ref offset, "frame_count");

        // --- Track array ---

        // track_count u32 LE — immediately after frame_count.
        // spec: Docs/RE/formats/animation.md §Track count — "u32 LE track_count": CONFIRMED.
        uint trackCount = ReadU32LE(data, ref offset, "track_count");

        AnimationTrack[] tracks = new AnimationTrack[trackCount];

        for (int t = 0; t < (int)trackCount; t++)
        {
            // track_descriptor u32 LE: low byte = bone_id; upper 3 bytes purpose UNVERIFIED.
            // spec: Docs/RE/formats/animation.md §Per-track record —
            //   "track_descriptor low byte = bone_id": CONFIRMED.
            //   "upper three bytes purpose UNVERIFIED": UNVERIFIED.
            uint trackDescriptor = ReadU32LE(data, ref offset, $"track[{t}].track_descriptor");
            byte boneId = (byte)(trackDescriptor & 0xFF);
            uint trackDescHigh24 = trackDescriptor >> 8;

            // key_count u32 LE
            // spec: Docs/RE/formats/animation.md §Per-track record — key_count u32 LE: CONFIRMED.
            uint keyCount = ReadU32LE(data, ref offset, $"track[{t}].key_count");

            // Validate buffer before reading keyframes.
            // spec: Docs/RE/formats/animation.md §Per-track record — "key_count × 28 bytes": CONFIRMED.
            long keyframeBytesNeeded = (long)keyCount * KeyframeStride;
            if (offset + keyframeBytesNeeded > data.Length)
                throw new InvalidDataException(
                    $".mot parse error: track[{t}] keyframe block truncated — " +
                    $"key_count={keyCount} requires {keyframeBytesNeeded} bytes at offset {offset}, " +
                    $"but buffer length is {data.Length}.");

            Keyframe[] keyframes = new Keyframe[(int)keyCount];

            for (int k = 0; k < (int)keyCount; k++)
            {
                // Keyframe layout: translation XYZ (f32[3]) + rotation XYZW (f32[4]) = 28 bytes.
                // spec: Docs/RE/formats/animation.md §Keyframe record — 28 bytes LE.

                // translation_x f32 @ sub-offset +0
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x @ +0: CONFIRMED.
                float tx = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].translation_x");

                // translation_y f32 @ sub-offset +4
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_y @ +4: CONFIRMED.
                float ty = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].translation_y");

                // translation_z f32 @ sub-offset +8
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_z @ +8: CONFIRMED.
                float tz = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].translation_z");

                // rotation_x f32 @ sub-offset +12
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_x @ +12: CONFIRMED.
                float rx = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].rotation_x");

                // rotation_y f32 @ sub-offset +16
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_y @ +16: CONFIRMED.
                float ry = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].rotation_y");

                // rotation_z f32 @ sub-offset +20
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_z @ +20: CONFIRMED.
                float rz = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].rotation_z");

                // rotation_w f32 @ sub-offset +24 (scalar W last — XYZW order)
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_w @ +24: CONFIRMED.
                float rw = ReadF32LE(data, ref offset, $"track[{t}].key[{k}].rotation_w");

                keyframes[k] = new Keyframe
                {
                    Translation = new Vec3(tx, ty, tz),
                    Rotation = new Quat(rx, ry, rz, rw),
                };
            }

            tracks[t] = new AnimationTrack
            {
                BoneId = boneId,
                TrackDescriptorHigh24 = trackDescHigh24,
                Keyframes = keyframes,
            };
        }

        return new AnimationClip
        {
            IdA = idA,
            IdB = idB,
            Name = name,
            FrameCount = frameCount,
            Tracks = tracks,
        };
    }

    // -------------------------------------------------------------------------
    // Private binary reader helpers (little-endian, bounds-checked)
    // -------------------------------------------------------------------------

    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".mot parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }

    private static float ReadF32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".mot parse error: buffer truncated reading '{fieldName}' f32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        float value = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}