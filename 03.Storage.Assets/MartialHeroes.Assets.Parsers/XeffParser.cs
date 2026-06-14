using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.xeff</c> particle-effect descriptor files
/// and <c>.eff</c> effect-object shape geometry files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §Section A (.xeff) and §Section B (.eff)
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class XeffParser
{
    // Anti-magic sentinel: if effect_id == 0x46464558, the client treats the file as corrupt.
    // spec: Docs/RE/formats/effects.md §A.1 — XEFF_INVALID_MAGIC = 0x46464558 ("XEFF" LE): CONFIRMED.
    private const uint XeffInvalidMagic = 0x46464558;

    // .xeff header size: 32 bytes (0x20).
    // CORRECTED from prior 8-byte description — spec conflict resolved 2026-06-12.
    // spec: Docs/RE/formats/effects.md §A.2 XEFF_HEADER_SIZE = 32 (0x20): VERIFIED.
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_HEADER_SIZE = 32.
    private const int XeffHeaderSize = 32; // 0x20

    // Texture name field width per name-table entry: 64 bytes.
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_TEX_NAME_LEN = 64 (0x40): CONFIRMED.
    private const int TexNameLen = 64; // 0x40

    // Track header size: 13 bytes (1 + 4 + 4 + 4).
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 13: CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.4.3 Track header — anim_loop u8 @ +0, unknown_constant u32 @ +1, anim_stride u32 @ +5, anim_base_time u32 @ +9.
    private const int TrackHeaderSize = 13;

    // Reserved padding in file header: 16 bytes.
    // spec: Docs/RE/formats/effects.md §A.2 — reserved u8[16] @ 0x0C: SAMPLE-VERIFIED.
    private const int HeaderReservedLen = 16;

    /// <summary>
    /// Parses a <c>.xeff</c> particle-effect descriptor file.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded effect data.</returns>
    /// <exception cref="InvalidDataException">Thrown when the anti-magic sentinel is found or buffer is truncated.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes, CORRECTED): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED by sample byte-walkthrough.
    /// File-size formula (single sub-effect, N entries):
    ///   32 (header) + N×64 (name table) + (4+N×4) (alpha curve) + (4+c2×4) (scaleX) + (4+c3×4) (scaleY) + (4+c4×4) (scaleZ)
    ///   + 13 (track header) + 9×4 (frame 0, no index prefix) + (N−1)×(4+9×4) (frames 1..N-1).
    /// </remarks>
    public static XeffData ParseXeff(ReadOnlyMemory<byte> data) =>
        ParseXeff(data.Span);

    /// <inheritdoc cref="ParseXeff(ReadOnlyMemory{byte})"/>
    public static XeffData ParseXeff(ReadOnlySpan<byte> span)
    {
        // ─── 32-byte file header ─────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.2 — header is 32 bytes (0x20): VERIFIED.
        if (span.Length < XeffHeaderSize)
            throw new InvalidDataException(
                $".xeff parse error: buffer too short for {XeffHeaderSize}-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §A.2.");

        // effect_id u32le @ 0x00. Must not equal 0x46464558.
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 @ 0x00: VERIFIED.
        uint effectId = BinaryPrimitives.ReadUInt32LittleEndian(span[0x00..]);
        if (effectId == XeffInvalidMagic)
            throw new InvalidDataException(
                $".xeff parse error: effect_id == 0x{XeffInvalidMagic:X8} (anti-magic sentinel). " +
                "File is corrupt. spec: Docs/RE/formats/effects.md §A.1.");

        // sub_effect_count u32le @ 0x04. Zero is valid (stub/empty effect).
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count u32 @ 0x04: VERIFIED.
        uint subEffectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);

        // type_flag u32le @ 0x08. Observed: 1 or 2. Semantics UNRESOLVED.
        // spec: Docs/RE/formats/effects.md §A.2 — type_flag u32 @ 0x08: SAMPLE-VERIFIED.
        uint typeFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);

        // reserved u8[16] @ 0x0C. Zero in all samples.
        // spec: Docs/RE/formats/effects.md §A.2 — reserved u8[16] @ 0x0C: SAMPLE-VERIFIED.
        var reserved = span[0x0C..(0x0C + HeaderReservedLen)].ToArray();

        // first_entry_count u32le @ 0x1C. Convenience copy of sub-effect[0]'s entry_count.
        // spec: Docs/RE/formats/effects.md §A.2 — first_entry_count u32 @ 0x1C: SAMPLE-VERIFIED.
        uint firstEntryCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0x1C..]);

        // Immediately after the 32-byte header, sub_effect_count sub-effect blocks follow.
        // Block 0 has NO entry_count prefix — its count comes from first_entry_count @ 0x1C above.
        // Blocks 1..N-1 each carry a 24-byte prefix: u32 sub_id + u32[4] zeros + u32 entry_count.
        // spec: Docs/RE/formats/effects.md §A.4 — block[0] prefix-free (entry_count from header): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free confirmed; blocks 1..N-1 = 24-byte prefix: CONFIRMED.
        int offset = XeffHeaderSize; // 0x20
        var subEffects = new XeffSubEffect[(int)subEffectCount];

        for (int s = 0; s < (int)subEffectCount; s++)
        {
            bool isFirstBlock = s == 0;
            subEffects[s] = ReadSubEffect(span, ref offset, s, isFirstBlock, firstEntryCount);
        }

        return new XeffData
        {
            EffectId = effectId,
            SubEffectCount = subEffectCount,
            TypeFlag = typeFlag,
            Reserved = reserved,
            FirstEntryCount = firstEntryCount,
            SubEffects = subEffects,
        };
    }

    private static XeffSubEffect ReadSubEffect(
        ReadOnlySpan<byte> span, ref int offset, int subIndex,
        bool isFirstBlock, uint firstEntryCount)
    {
        // ─── Block prefix / entry_count ───────────────────────────────────────
        // Block 0 (isFirstBlock == true):
        //   NO prefix bytes on disk. entry_count comes from the file header's first_entry_count field.
        //   spec: Docs/RE/formats/effects.md §A.4 — block[0] prefix-free; first_entry_count from header: CONFIRMED.
        //   spec: Docs/RE/formats/effects.md §A.15 — "block[0] has NO entry_count prefix": CONFIRMED.
        //
        // Blocks 1..N-1 (isFirstBlock == false):
        //   24-byte prefix at start of block:
        //     u32 sub_id       @ prefix+0   (observed: sequential 1..N-1 ordinal-ish)
        //     u32[4] zeros     @ prefix+4   (four zero u32s, padding/reserved)
        //     u32 entry_count  @ prefix+20  (the block's own entry count)
        //   Total prefix: 6 × u32 = 24 bytes.
        //   spec: Docs/RE/formats/effects.md §A.4 §A.15 — blocks[1..N-1] 24-byte prefix: CONFIRMED.
        uint subId;
        uint entryCount;
        if (isFirstBlock)
        {
            // Block 0: no prefix; use first_entry_count from file header.
            subId = 0;
            entryCount = firstEntryCount;
        }
        else
        {
            // Blocks 1..N-1: read 24-byte prefix.
            // spec: Docs/RE/formats/effects.md §A.4 — blocks[1..N-1] prefix: u32 sub_id + u32[4] zeros + u32 entry_count = 24 bytes: CONFIRMED.
            const int BlockPrefixSize = 24; // 6 × u32 = 24 bytes
            EnsureBytes(span, offset, BlockPrefixSize, $"sub_effect[{subIndex}] 24-byte prefix");
            subId = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);         // prefix+0
            // prefix+4..+16: four zero u32s (reserved/padding) — skip without reading.
            entryCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 20)..]); // prefix+20
            offset += BlockPrefixSize;
        }

        // ─── A.4.1 Name table — entryCount × 64 bytes ────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.1 Name table — entry_count × 64 bytes: CONFIRMED.
        long nameTableBytes = (long)entryCount * TexNameLen;
        EnsureBytes(span, offset, nameTableBytes, $"sub_effect[{subIndex}] name table");

        var texNames = new string[(int)entryCount];
        for (int t = 0; t < (int)entryCount; t++)
        {
            // 64-byte null-padded ASCII/CP949 base name.
            // spec: Docs/RE/formats/effects.md §A.4.1 — tex_name char[64] null-padded ASCII (CP949 for Korean): CONFIRMED.
            ReadOnlySpan<byte> nameBytes = span.Slice(offset + t * TexNameLen, TexNameLen);
            int nullIdx = nameBytes.IndexOf((byte)0);
            texNames[t] = System.Text.Encoding.ASCII.GetString(
                nullIdx >= 0 ? nameBytes[..nullIdx] : nameBytes);
        }

        offset += (int)nameTableBytes;

        // ─── A.4.2 Curve section — four consecutive passes ────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.2 Curve section — exactly four consecutive float-curve arrays: CONFIRMED.

        // Pass 1: alpha channel. Values in [0,1]; stored as 1.0 − opacity.
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 1 alpha — own u32 prefix, stored as 1.0−opacity: CONFIRMED.
        float[] alphaKeys = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] alpha curve");

        // Pass 2: scale X channel.
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 2 scale X — own u32 prefix: CONFIRMED.
        float[] scaleX = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] scaleX curve");

        // Pass 3: scale Y channel.
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 3 scale Y — own u32 prefix: CONFIRMED.
        float[] scaleY = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] scaleY curve");

        // Pass 4: scale Z channel.
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 4 scale Z — own u32 prefix: CONFIRMED.
        float[] scaleZ = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] scaleZ curve");

        // ─── A.4.3 Track header — 13 bytes (fixed) ───────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (13 bytes, fixed): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 13.
        EnsureBytes(span, offset, TrackHeaderSize, $"sub_effect[{subIndex}] track header");

        // anim_loop u8 @ +0. Non-zero enables animated path.
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_loop u8 @ +0: CONFIRMED.
        byte animLoop = span[offset];

        // unknown_constant u32le @ +1. Observed value: 67 (0x43). Purpose UNRESOLVED.
        // spec: Docs/RE/formats/effects.md §A.4.3 — unknown_constant u32 @ +1: SAMPLE-VERIFIED (value), semantics UNRESOLVED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_UNKNOWN_CONSTANT = 67.
        uint unknownConstant = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 1)..]);

        // anim_stride u32le @ +5. Duration of one animation frame in milliseconds.
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_stride u32 @ +5 (ms): CONFIRMED.
        uint animStride = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 5)..]);

        // anim_base_time u32le @ +9. Base time offset in milliseconds.
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_base_time u32 @ +9 (ms): CONFIRMED.
        uint animBaseTime = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 9)..]);
        offset += TrackHeaderSize; // consume all 13 bytes

        // ─── A.4.4 Keyframe array — entryCount entries ────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array: CONFIRMED.
        // Frame 0: NO index prefix — only 9 × f32 = 36 bytes.
        // Frames 1..entryCount-1: u32 kf_index + 9 × f32 = 40 bytes each.
        // spec: Docs/RE/formats/effects.md §A.4.4 — "Frame 0 is a special case: it has NO index prefix": CONFIRMED.
        var keyframes = new XeffKeyframe[(int)entryCount];
        for (int k = 0; k < (int)entryCount; k++)
        {
            uint kfIndex;
            if (k == 0)
            {
                // Frame 0: no index prefix. 9 × f32 = 36 bytes.
                // spec: Docs/RE/formats/effects.md §A.4.4 — frame 0: 9 × f32 (36 bytes), no index: CONFIRMED.
                EnsureBytes(span, offset, 36, $"sub_effect[{subIndex}] keyframe[0] (no-index frame)");
                kfIndex = 0;
            }
            else
            {
                // Frames 1..N-1: u32 kf_index + 9 × f32 = 40 bytes.
                // spec: Docs/RE/formats/effects.md §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32 = 40 bytes: CONFIRMED.
                EnsureBytes(span, offset, 40, $"sub_effect[{subIndex}] keyframe[{k}]");
                kfIndex = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
                offset += 4;
            }

            // 9-float layout (in file order):
            // 1: velocity_x, 2: velocity_y, 3: velocity_z
            // 4: size_x,     5: size_y,     6: size_z
            // 7: kf_rot_x_deg, 8: kf_rot_y_deg, 9: kf_rot_z_deg
            // spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
            float vx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]); // position 1
            float vy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]); // position 2
            float vz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]); // position 3
            float sx = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]); // position 4
            float sy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]); // position 5
            float sz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]); // position 6
            float rxd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 24)..]); // position 7 — degrees
            float ryd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 28)..]); // position 8 — degrees
            float rzd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 32)..]); // position 9 — degrees
            offset += 36; // always advance by 9 × f32 regardless of frame index

            keyframes[k] = new XeffKeyframe
            {
                KfIndex = kfIndex,
                VelocityX = vx,
                VelocityY = vy,
                VelocityZ = vz,
                SizeX = sx,
                SizeY = sy,
                SizeZ = sz,
                RotXDeg = rxd,
                RotYDeg = ryd,
                RotZDeg = rzd,
            };
        }

        return new XeffSubEffect
        {
            SubId = subId,
            EntryCount = entryCount,
            TextureNames = texNames,
            AlphaKeys = alphaKeys,
            ScaleX = scaleX,
            ScaleY = scaleY,
            ScaleZ = scaleZ,
            AnimLoop = animLoop,
            UnknownConstant = unknownConstant,
            AnimStride = animStride,
            AnimBaseTime = animBaseTime,
            Keyframes = keyframes,
        };
    }

    /// <summary>
    /// Reads one float-curve pass: u32 count prefix + count × f32 values.
    /// spec: Docs/RE/formats/effects.md §A.4.2 — each pass: u32 curve_count + curve_count × f32: CONFIRMED.
    /// </summary>
    private static float[] ReadFloatCurve(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        EnsureBytes(span, offset, 4, $"{fieldName} count");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        EnsureBytes(span, offset, (long)count * 4, $"{fieldName} values");
        var arr = new float[(int)count];
        for (int i = 0; i < (int)count; i++)
            arr[i] = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + i * 4)..]);
        offset += (int)count * 4;
        return arr;
    }

    // ─── .eff effect-object shape ──────────────────────────────────────────────

    // .eff vertex stride: 32 bytes.
    // spec: Docs/RE/formats/effects.md §B.4.1 VertexRecord (32 bytes, stride = 0x20): VERIFIED.
    private const int EffVertexStride = 32;

    /// <summary>
    /// Parses an <c>.eff</c> effect-object shape geometry file.
    /// Layout: index_count u32 + u16[] indices + vert_count u32 + 32B vertices.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded effect-object shape.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/effects.md §B.2 File Layout: VERIFIED (3 samples, zero residual bytes).
    /// spec: Docs/RE/formats/effects.md §B.3 Index Section: VERIFIED.
    /// spec: Docs/RE/formats/effects.md §B.4 Vertex Section: VERIFIED.
    /// File-size formula: 4 + (index_count × 2) + 4 + (vert_count × 32).
    /// Dispatch by directory: data/effect/obj/*.eff ONLY — not sound nor sky paths.
    /// spec: Docs/RE/formats/effects.md §Disambiguation.
    /// </remarks>
    public static EffObjectShape ParseEff(ReadOnlyMemory<byte> data) =>
        ParseEff(data.Span);

    /// <inheritdoc cref="ParseEff(ReadOnlyMemory{byte})"/>
    public static EffObjectShape ParseEff(ReadOnlySpan<byte> span)
    {
        // index_count u32le @ offset 0x00.
        // spec: Docs/RE/formats/effects.md §B.3 — index_count u32 @ 0x00: VERIFIED.
        if (span.Length < 4)
            throw new InvalidDataException(
                $".eff parse error: buffer too short for 4-byte index_count (got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §B.3.");

        uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        long indexBytes = (long)indexCount * 2;

        if (span.Length < 4 + indexBytes + 4)
            throw new InvalidDataException(
                $".eff parse error: truncated at index array or vert_count (need {4 + indexBytes + 4}, got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §B.3.");

        // u16 indices[index_count] @ offset 0x04.
        // spec: Docs/RE/formats/effects.md §B.3 — indices u16[] @ 0x04: VERIFIED.
        var indices = new ushort[(int)indexCount];
        for (int i = 0; i < (int)indexCount; i++)
            indices[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(4 + i * 2)..]);

        // vert_count u32le at offset 4 + index_count*2. May be non-4-byte-aligned.
        // spec: Docs/RE/formats/effects.md §B.4 — vert_count u32 at 4 + index_count*2 (possibly unaligned): VERIFIED.
        int vertCountOffset = (int)(4 + indexBytes);
        uint vertCount = BinaryPrimitives.ReadUInt32LittleEndian(span[vertCountOffset..]);

        long vertBytes = (long)vertCount * EffVertexStride;
        long expectedTotal = vertCountOffset + 4 + vertBytes;
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $".eff parse error: truncated at vertex array (need {expectedTotal}, got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §B.4.");

        // Vertex records at offset vertCountOffset + 4.
        // spec: Docs/RE/formats/effects.md §B.4.1 VertexRecord (32 bytes): VERIFIED.
        int vertBase = vertCountOffset + 4;
        var vertices = new EffVertex[(int)vertCount];
        for (int v = 0; v < (int)vertCount; v++)
        {
            int vOff = vertBase + v * EffVertexStride;
            vertices[v] = new EffVertex(
                BinaryPrimitives.ReadSingleLittleEndian(span[vOff..]), // pos_x @ +0
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 4)..]), // pos_y @ +4
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 8)..]), // pos_z @ +8
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 12)..]), // normal_x @ +12
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 16)..]), // normal_y @ +16
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 20)..]), // normal_z @ +20
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 24)..]), // tex_u @ +24
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 28)..]) // tex_v @ +28
            );
        }

        return new EffObjectShape { Indices = indices, Vertices = vertices };
    }

    private static void EnsureBytes(ReadOnlySpan<byte> span, int offset, long needed, string fieldName)
    {
        if (offset + needed > span.Length)
            throw new InvalidDataException(
                $".xeff parse error: truncated reading '{fieldName}' — " +
                $"need {needed} bytes at offset {offset}, buffer length {span.Length}. " +
                "spec: Docs/RE/formats/effects.md §A.4.");
    }
}