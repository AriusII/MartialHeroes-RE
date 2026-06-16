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

    // BANI magic: ASCII "BANI" = 0x42 0x41 0x4E 0x49.
    // spec: Docs/RE/formats/animation.md §BANI variant — magic "BANI" (42 41 4E 49): SAMPLE-VERIFIED.
    // 11 of 3,891 .mot files begin with this magic. The shipping client loader has no magic-check branch
    // and produces a parse error on all 11. A faithful Assets.Parsers decoder must detect and skip them
    // (return null / throw gracefully), not crash. spec: Docs/RE/formats/animation.md §BANI variant — loader rejection: CONFIRMED.
    private static ReadOnlySpan<byte> BaniMagic => "BANI"u8;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="data"/> begins with the BANI magic bytes.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/animation.md §BANI variant — magic ASCII "BANI" (42 41 4E 49): SAMPLE-VERIFIED.
    /// </remarks>
    public static bool IsBaniVariant(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[..4].SequenceEqual(BaniMagic);

    /// <summary>
    /// Parses the raw bytes of a <c>.mot</c> file into an <see cref="AnimationClip"/>.
    /// Returns <see langword="null"/> for the BANI variant (11 files in the corpus), which the
    /// shipping client loader also cannot parse.
    /// Tolerates the single oversized standard clip (trailing bytes after the track array are ignored).
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>
    /// Decoded animation clip, or <see langword="null"/> if the file is the BANI variant.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun (standard variant only).
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/animation.md §BANI variant — "parser MUST sniff the first 4 bytes
    ///   and route BANI files separately": CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §Oversized standard clip — "tolerate a positive residual": CONFIRMED.
    /// </remarks>
    public static AnimationClip? Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// Returns <see langword="null"/> for the BANI variant.
    /// </summary>
    public static AnimationClip? Parse(ReadOnlySpan<byte> data)
    {
        // Guard: detect BANI magic and skip gracefully.
        // spec: Docs/RE/formats/animation.md §BANI variant — "A parser MUST sniff the first 4 bytes
        //   and route BANI files separately — the standard loader ... does NOT detect the magic,
        //   causing a parse failure on all 11 files": SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/animation.md §BANI variant — loader rejection: CONFIRMED.
        if (IsBaniVariant(data))
            return null; // BANI variant: not loadable by the shipping client; skip gracefully.

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
        //   "CONFIRMED (loader + sample): 4-byte u32 LE length prefix, then exactly length body bytes,
        //    no null terminator on disk. Verified independently via loader and real sample — they agree."
        // spec: Docs/RE/formats/animation.md — "LenStr prefix width: CONFIRMED 4-byte u32 LE, no on-disk terminator."
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
            // track_descriptor u32 LE: low byte = bone_id; upper 3 bytes CONFIRMED reserved/unused padding.
            // All three candidate interpretations (key-count, channel-mask, interp-flag) have been REFUTED.
            // spec: Docs/RE/formats/animation.md §Per-track record —
            //   "track_descriptor low byte = bone_id": CONFIRMED.
            //   "upper three bytes: CONFIRMED reserved/unused padding; all three candidate
            //    interpretations REFUTED (not key-count, not channel-mask, not interp-flag)."
            uint trackDescriptor = ReadU32LE(data, ref offset, $"track[{t}].track_descriptor");
            byte boneId = (byte)(trackDescriptor & 0xFF);
            uint trackDescHigh24 = trackDescriptor >> 8;

            // key_count u32 LE
            // spec: Docs/RE/formats/animation.md §Per-track record — key_count u32 LE: CONFIRMED.
            uint keyCount = ReadU32LE(data, ref offset, $"track[{t}].key_count");

            // Validate buffer before reading keyframes (single pre-loop bounds check).
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
                // Bounds already checked above; read 7 × f32 without per-element string allocation.

                // translation_x f32 @ sub-offset +0
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x @ +0: CONFIRMED.
                float tx = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                // translation_y f32 @ sub-offset +4
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_y @ +4: CONFIRMED.
                float ty = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                // translation_z f32 @ sub-offset +8
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_z @ +8: CONFIRMED.
                float tz = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                // rotation_x f32 @ sub-offset +12
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_x @ +12: CONFIRMED.
                float rx = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                // rotation_y f32 @ sub-offset +16
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_y @ +16: CONFIRMED.
                float ry = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                // rotation_z f32 @ sub-offset +20
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_z @ +20: CONFIRMED.
                float rz = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                // rotation_w f32 @ sub-offset +24 (scalar W last — XYZW order)
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_w @ +24: CONFIRMED.
                float rw = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

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
    // Used for header-level fields only (not the hot per-keyframe loop).
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
}