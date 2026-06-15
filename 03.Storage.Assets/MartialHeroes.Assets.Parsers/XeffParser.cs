using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.xeff</c> particle-effect descriptor files
/// and <c>.eff</c> effect-object shape geometry files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §Section A (.xeff) and §Section B (.eff)
/// <para>
/// HEADER CORRECTED 2026-06-14: The .xeff file header is exactly 8 bytes
/// (effect_id u32 + sub_effect_count u32). Every sub-effect block — including block 0 —
/// is parsed by the same element read sequence: a 24-byte fixed head (A.4.0) then the variable body.
/// The value at file offset 0x08 is element 0's emitter_type (NOT a header type_flag).
/// spec: Docs/RE/formats/effects.md §A.2 XEFF_HEADER_SIZE = 8 (0x08): VERIFIED.
/// spec: Docs/RE/formats/effects.md §A.14 XEFF_ELEMENT_FIXED_HEAD = 24 (0x18): per-element on-disk fixed head.
/// spec: Docs/RE/formats/effects.md §A.17 Correction history.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class XeffParser
{
    // Anti-magic sentinel: if effect_id == 0x46464558, the client treats the file as corrupt.
    // spec: Docs/RE/formats/effects.md §A.1 — XEFF_INVALID_MAGIC = 0x46464558 ("XEFF" LE): CONFIRMED.
    private const uint XeffInvalidMagic = 0x46464558;

    // .xeff header size: 8 bytes (0x08).
    // CORRECTED 2026-06-14: header is effect_id u32 + sub_effect_count u32 only.
    // spec: Docs/RE/formats/effects.md §A.2 XEFF_HEADER_SIZE = 8 (0x08): VERIFIED.
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_HEADER_SIZE = 8.
    private const int XeffHeaderSize = 8; // 0x08

    // Per-element on-disk fixed head size: 24 bytes (6 × u32).
    // spec: Docs/RE/formats/effects.md §A.4.0 Element fixed head (24 bytes): CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_ELEMENT_FIXED_HEAD = 24 (0x18).
    private const int ElementFixedHeadSize = 24; // 0x18

    // Texture name field width per name-table entry: 64 bytes.
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_TEX_NAME_LEN = 64 (0x40): CONFIRMED.
    private const int TexNameLen = 64; // 0x40

    // Track header size: 9 bytes (1 + 4 + 4).
    // CORRECTED CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):
    //   The prior 13-byte header (with a phantom "unknown_constant" u32 at +1) is REFUTED.
    //   No read-site consumes a 4-byte constant at +1. Those four bytes ARE the first keyframe's
    //   u32 index prefix (A.4.4), which begins immediately after this 9-byte header.
    //   Frame 0 is a NORMAL 40-byte animated entry (u32 kf_index + 9 × f32), not a special case.
    // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED (CAMPAIGN VFS-MASTERY).
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9.
    private const int TrackHeaderSize = 9;

    // emitter_type values.
    // spec: Docs/RE/formats/effects.md §A.12 emitter_type enum: CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.14 XEFF_EMITTER_BILLBOARD=0, XEFF_EMITTER_MESH=1, XEFF_EMITTER_DIRECTIONAL=2.
    private const uint EmitterBillboard = 0;
    private const uint EmitterMesh = 1;
    private const uint EmitterDirectional = 2;

    /// <summary>
    /// Parses a <c>.xeff</c> particle-effect descriptor file.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded effect data.</returns>
    /// <exception cref="InvalidDataException">Thrown when the anti-magic sentinel is found or buffer is truncated.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (8 bytes): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED by sample byte-walkthrough.
    /// spec: Docs/RE/formats/effects.md §A.4.5 — every block (block 0 included) uses the same read sequence: CONFIRMED.
    /// File-size formula (single sub-effect, N entries, animated path, scale curves empty):
    ///   8                            header (effect_id + sub_effect_count)
    ///   + 24                         element fixed head (A.4.0)
    ///   + N×64                       name table (A.4.1)
    ///   + (4+N×4)                    curve 1 (alpha) (A.4.2)
    ///   + 3×4                        curves 2/3/4 (scale, count prefix only when count=0) (A.4.2)
    ///   + 13                         track header (A.4.3)
    ///   + 9×4                        frame 0 (no index prefix) (A.4.4)
    ///   + (N−1)×(4+9×4)             frames 1..N-1 (each has a u32 index prefix) (A.4.4)
    /// </remarks>
    public static XeffData ParseXeff(ReadOnlyMemory<byte> data) =>
        ParseXeff(data.Span);

    /// <inheritdoc cref="ParseXeff(ReadOnlyMemory{byte})"/>
    public static XeffData ParseXeff(ReadOnlySpan<byte> span)
    {
        // ─── 8-byte file header ───────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.2 — header is 8 bytes (0x08): VERIFIED.
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

        // Immediately after the 8-byte header, sub_effect_count sub-effect blocks follow sequentially.
        // Block 0 starts at file offset 0x08.
        // Every block — block 0 included — is parsed by the same element read sequence: A.4.0 fixed head + body.
        // spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially, no offset table: CONFIRMED.
        int offset = XeffHeaderSize; // 0x08
        var subEffects = new XeffSubEffect[(int)subEffectCount];

        for (int s = 0; s < (int)subEffectCount; s++)
        {
            subEffects[s] = ReadSubEffect(span, ref offset, s);
        }

        return new XeffData
        {
            EffectId = effectId,
            SubEffectCount = subEffectCount,
            SubEffects = subEffects,
        };
    }

    private static XeffSubEffect ReadSubEffect(
        ReadOnlySpan<byte> span, ref int offset, int subIndex)
    {
        // ─── A.4.0 Element fixed head (24 bytes) ──────────────────────────────
        // Every block — including block 0 — starts with this 24-byte fixed head.
        // For block 0 these bytes begin at file offset 0x08 (immediately after the 8-byte header).
        // The value at file offset 0x08 (element+0x00) is emitter_type (NOT a header type_flag).
        // spec: Docs/RE/formats/effects.md §A.4.0 Element fixed head (24 bytes): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_ELEMENT_FIXED_HEAD = 24 (0x18): CONFIRMED.
        EnsureBytes(span, offset, ElementFixedHeadSize, $"sub_effect[{subIndex}] element fixed head");

        // emitter_type u32le @ element+0x00. 0=billboard, 1=mesh-particle, 2=directional-billboard.
        // spec: Docs/RE/formats/effects.md §A.4.0 — emitter_type u32 @ element+0x00: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.12 emitter_type enum: CONFIRMED.
        uint emitterType = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);

        // resource_id u32le @ element+0x04. <10000=shared mesh; >=10000=GPU particle id.
        // spec: Docs/RE/formats/effects.md §A.4.0 — resource_id u32 @ element+0x04: CONFIRMED.
        uint resourceId = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);

        // anim_flag u32le @ element+0x08. Consumed as boolean.
        // spec: Docs/RE/formats/effects.md §A.4.0 — anim_flag u32 @ element+0x08: CONFIRMED.
        uint animFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);

        // field_unknown_a u32le @ element+0x0C. Semantics UNRESOLVED.
        // spec: Docs/RE/formats/effects.md §A.4.0 — field_unknown_a u32 @ element+0x0C: UNRESOLVED.
        uint fieldUnknownA = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);

        // element_dword2 u32le @ element+0x10. Written ahead of tex_count in memory; role UNRESOLVED.
        // spec: Docs/RE/formats/effects.md §A.4.0 — element_dword2 u32 @ element+0x10: UNRESOLVED.
        uint elementDword2 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);

        // tex_count u32le @ element+0x14. This element's entry count — drives the name table, keyframe count.
        // For block 0 this is the value at file offset 0x1C (formerly mislabelled header first_entry_count).
        // spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.2 — "bytes formerly called reserved/first_entry_count are block 0's fixed head": CONFIRMED.
        uint texCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 20)..]);

        offset += ElementFixedHeadSize; // consume 24 bytes

        // ─── A.4.1 Name table — texCount × 64 bytes ──────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.1 Name table — tex_count × 64 bytes: CONFIRMED.
        long nameTableBytes = (long)texCount * TexNameLen;
        EnsureBytes(span, offset, nameTableBytes, $"sub_effect[{subIndex}] name table");

        var texNames = new string[(int)texCount];
        for (int t = 0; t < (int)texCount; t++)
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

        // ─── A.4.3 Track header — 9 bytes (fixed) ────────────────────────────
        // CORRECTED CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):
        //   Header is 9 bytes on BOTH the static and animated branch.
        //   The "unknown_constant" field at +1 IS DELETED — it is REFUTED (no read-site).
        //   The four bytes previously mislabelled "unknown_constant" are the first keyframe's
        //   u32 index prefix, which starts immediately after this 9-byte header.
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9.
        EnsureBytes(span, offset, TrackHeaderSize, $"sub_effect[{subIndex}] track header");

        // anim_loop u8 @ +0. Non-zero enables animated path.
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_loop u8 @ +0: CONFIRMED.
        byte animLoop = span[offset];

        // anim_stride u32le @ +1. Duration of one animation frame in milliseconds.
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_stride u32 @ +1 (ms): CONFIRMED.
        uint animStride = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 1)..]);

        // anim_base_time u32le @ +5. Base time offset in milliseconds.
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_base_time u32 @ +5 (ms): CONFIRMED.
        uint animBaseTime = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 5)..]);
        offset += TrackHeaderSize; // consume all 9 bytes

        // ─── A.4.4 / A.4.6 Keyframe array ────────────────────────────────────
        // Animated path (animLoop != 0): texCount frames.
        //   EVERY frame (including frame 0): u32 kf_index + 9 × f32 = 40 bytes.
        //   CORRECTED CAMPAIGN VFS-MASTERY: frame 0 is NOT a special no-index case.
        //   The "missing" 4-byte index prefix for frame 0 was absorbed into the phantom
        //   13-byte track header (the deleted "unknown_constant"). With the track header
        //   corrected to 9 bytes, frame 0 is a normal 40-byte entry with its own u32 kf_index.
        // Static path (animLoop == 0): exactly one entry.
        //   emitter_type != 2: 6 × f32 (velocity Vec3 + size Vec3) = 24 bytes, NO rotation.
        //   emitter_type == 2: 9 × f32 (velocity Vec3 + size Vec3 + Euler rotation) = 36 bytes.
        // spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array (CORRECTED): CONFIRMED (CAMPAIGN VFS-MASTERY).
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_KEYFRAME_ONDISK_STRIDE = 40 (0x28): u32 index + 9 × f32, every frame.
        // spec: Docs/RE/formats/effects.md §A.4.6 emitter_type-dependent static branch: CONFIRMED.
        XeffKeyframe[] keyframes;
        if (animLoop != 0)
        {
            // Animated path: EVERY frame has u32 kf_index + 9 × f32 = 40 bytes.
            // spec: Docs/RE/formats/effects.md §A.4.4 — "every animated keyframe carries a u32 index prefix": CONFIRMED.
            // spec: Docs/RE/formats/effects.md §A.14 XEFF_KEYFRAME_ONDISK_STRIDE = 40.
            keyframes = new XeffKeyframe[(int)texCount];
            for (int k = 0; k < (int)texCount; k++)
            {
                // All frames (including frame 0): u32 kf_index + 9 × f32 = 40 bytes.
                // spec: Docs/RE/formats/effects.md §A.4.4 — "frame 0 is a normal 40-byte entry": CONFIRMED.
                EnsureBytes(span, offset, 40, $"sub_effect[{subIndex}] keyframe[{k}]");
                uint kfIndex = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
                offset += 4;

                keyframes[k] = ReadNineFloats(span, ref offset, kfIndex, subIndex, k);
            }
        }
        else
        {
            // Static path: exactly one entry. Size depends on emitter_type.
            // spec: Docs/RE/formats/effects.md §A.4.6 — static path: 6×f32 (type 0/1) or 9×f32 (type 2): CONFIRMED.
            bool hasRotation = emitterType == EmitterDirectional; // emitter_type == 2
            int staticBytes = hasRotation ? 36 : 24;
            EnsureBytes(span, offset, staticBytes, $"sub_effect[{subIndex}] static-state entry");

            // Read velocity Vec3 + size Vec3.
            // spec: Docs/RE/formats/effects.md §A.4.6 — static path: velocity Vec3 + size Vec3 always: CONFIRMED.
            float vx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            float vy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
            float vz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
            float sx = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]);
            float sy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]);
            float sz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]);
            offset += 24;

            // Euler rotation only present when emitter_type == 2.
            // spec: Docs/RE/formats/effects.md §A.4.6 — emitter_type==2: +12 bytes Euler rotation in static branch: CONFIRMED.
            float rxd = 0f, ryd = 0f, rzd = 0f;
            if (hasRotation)
            {
                rxd = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
                ryd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
                rzd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
                offset += 12;
            }

            keyframes = new[]
            {
                new XeffKeyframe
                {
                    KfIndex = 0,
                    VelocityX = vx,
                    VelocityY = vy,
                    VelocityZ = vz,
                    SizeX = sx,
                    SizeY = sy,
                    SizeZ = sz,
                    RotXDeg = rxd,
                    RotYDeg = ryd,
                    RotZDeg = rzd,
                },
            };
        }

        return new XeffSubEffect
        {
            EmitterType = emitterType,
            ResourceId = resourceId,
            AnimFlag = animFlag,
            FieldUnknownA = fieldUnknownA,
            ElementDword2 = elementDword2,
            EntryCount = texCount,
            TextureNames = texNames,
            AlphaKeys = alphaKeys,
            ScaleX = scaleX,
            ScaleY = scaleY,
            ScaleZ = scaleZ,
            AnimLoop = animLoop,
            AnimStride = animStride,
            AnimBaseTime = animBaseTime,
            Keyframes = keyframes,
        };
    }

    /// <summary>
    /// Reads 9 × f32 and constructs an XeffKeyframe (animated path).
    /// spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
    /// </summary>
    private static XeffKeyframe ReadNineFloats(
        ReadOnlySpan<byte> span, ref int offset, uint kfIndex, int subIndex, int k)
    {
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

        return new XeffKeyframe
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