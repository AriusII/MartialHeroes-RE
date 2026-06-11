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
    // spec: Docs/RE/formats/effects.md §A.1 Anti-magic — 0x46464558: CONFIRMED.
    private const uint XeffInvalidMagic = 0x46464558; // "XEFF" LE

    // .xeff header size: 8 bytes.
    // spec: Docs/RE/formats/effects.md §A.2 File Header (8 bytes): VERIFIED.
    private const int XeffHeaderSize = 8;

    // Texture name field width per Group B entry: 64 bytes.
    // spec: Docs/RE/formats/effects.md §A.9 XEFF_TEX_NAME_LEN = 64: CONFIRMED.
    private const int TexNameLen = 64;

    // Emitter type value that triggers rotation reads in Branch B.
    // spec: Docs/RE/formats/effects.md §A.9 XEFF_EMITTER_DIRECTIONAL = 2: CONFIRMED.
    private const uint EmitterDirectional = 2;

    /// <summary>
    /// Parses a <c>.xeff</c> particle-effect descriptor file.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded effect data.</returns>
    /// <exception cref="InvalidDataException">Thrown when the anti-magic sentinel is found or buffer is truncated.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/effects.md §A.2 File Header: VERIFIED (3 samples with element_count=0).
    /// spec: Docs/RE/formats/effects.md §A.3 Element Array: PARSER-CONFIRMED (no sample with element_count>0).
    /// </remarks>
    public static XeffData ParseXeff(ReadOnlyMemory<byte> data) =>
        ParseXeff(data.Span);

    /// <inheritdoc cref="ParseXeff(ReadOnlyMemory{byte})"/>
    public static XeffData ParseXeff(ReadOnlySpan<byte> span)
    {
        if (span.Length < XeffHeaderSize)
            throw new InvalidDataException(
                $".xeff parse error: buffer too short for {XeffHeaderSize}-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §A.2.");

        // effect_id u32le @ offset 0x00. Must not equal 0x46464558.
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 @ +0x00: VERIFIED.
        uint effectId = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        if (effectId == XeffInvalidMagic)
            throw new InvalidDataException(
                $".xeff parse error: effect_id == 0x{XeffInvalidMagic:X8} (anti-magic sentinel). " +
                "File is corrupt. spec: Docs/RE/formats/effects.md §A.1.");

        // element_count u32le @ offset 0x04.
        // spec: Docs/RE/formats/effects.md §A.2 — element_count u32 @ +0x04: VERIFIED.
        uint elementCount = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        int offset = XeffHeaderSize;
        var elements = new XeffElement[(int)elementCount];

        for (int e = 0; e < (int)elementCount; e++)
        {
            elements[e] = ReadElement(span, ref offset, e);
        }

        return new XeffData { EffectId = effectId, Elements = elements };
    }

    private static XeffElement ReadElement(ReadOnlySpan<byte> span, ref int offset, int elementIndex)
    {
        // ─── Group A — Emitter identity (20 bytes, 5 × u32le) ─────────────────
        // spec: Docs/RE/formats/effects.md §A.3.1 Group A (20 bytes): PARSER-CONFIRMED.
        EnsureBytes(span, offset, 20, $"element[{elementIndex}] Group A");

        // emitter_type @ +0: PARSER-CONFIRMED. Value 2 = directional.
        uint emitterType = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        // emitter_subtype @ +4: PARSER-CONFIRMED, semantics UNRESOLVED.
        uint emitterSubtype = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
        // anim_flag @ +8: PARSER-CONFIRMED.
        uint animFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);
        // tex_count @ +12: PARSER-CONFIRMED.
        uint texCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);
        // field_unknown_a @ +16: PARSER-CONFIRMED (exists), semantics UNRESOLVED.
        uint fieldUnknownA = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);
        offset += 20;

        // ─── Group B — Texture sub-array (tex_count × 64 bytes) ────────────────
        // spec: Docs/RE/formats/effects.md §A.3.2 Group B — tex_count × 64 B: PARSER-CONFIRMED.
        long texBytes = (long)texCount * TexNameLen;
        EnsureBytes(span, offset, texBytes, $"element[{elementIndex}] Group B texture array");

        var texNames = new string[(int)texCount];
        for (int t = 0; t < (int)texCount; t++)
        {
            // char[64] ASCII null-padded base name.
            // spec: Docs/RE/formats/effects.md §A.3.2 — tex_name char[64] ASCII: PARSER-CONFIRMED.
            ReadOnlySpan<byte> nameBytes = span.Slice(offset + t * TexNameLen, TexNameLen);
            int nullIdx = nameBytes.IndexOf((byte)0);
            texNames[t] = System.Text.Encoding.ASCII.GetString(
                nullIdx >= 0 ? nameBytes[..nullIdx] : nameBytes);
        }

        offset += (int)texBytes;

        // ─── Group C — Alpha keyframes ─────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.3.3 Group C: PARSER-CONFIRMED.
        EnsureBytes(span, offset, 4, $"element[{elementIndex}] Group C count");
        uint alphaKeyCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        EnsureBytes(span, offset, (long)alphaKeyCount * 4, $"element[{elementIndex}] Group C values");
        var alphaKeys = new float[(int)alphaKeyCount];
        for (int a = 0; a < (int)alphaKeyCount; a++)
        {
            // Stored inverted: 1.0 − file_value at load time.
            // spec: Docs/RE/formats/effects.md §A.3.3 — alpha stored inverted: HIGH.
            alphaKeys[a] = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + a * 4)..]);
        }

        offset += (int)alphaKeyCount * 4;

        // ─── Group D — Scale channels (3 passes: X, Y, Z) ─────────────────────
        // spec: Docs/RE/formats/effects.md §A.3.4 Group D: PARSER-CONFIRMED.
        float[] scaleX = ReadFloatArray(span, ref offset, $"element[{elementIndex}] Group D scaleX");
        float[] scaleY = ReadFloatArray(span, ref offset, $"element[{elementIndex}] Group D scaleY");
        float[] scaleZ = ReadFloatArray(span, ref offset, $"element[{elementIndex}] Group D scaleZ");

        // ─── Group E — Animation timing (9 bytes) ─────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.3.5 Group E (9 bytes): CONFIRMED.
        EnsureBytes(span, offset, 9, $"element[{elementIndex}] Group E");

        // anim_loop u8 @ Group E offset 0 (single byte, NOT u32).
        // spec: Docs/RE/formats/effects.md §A.3.5 — anim_loop u8 CONFIRMED (single-byte read).
        byte animLoop = span[offset];
        // anim_stride u32le @ Group E offset 1.
        uint animStride = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 1)..]);
        // anim_base_time u32le @ Group E offset 5.
        uint animBaseTime = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 5)..]);
        offset += 9;

        // ─── Group F — Keyframe / static-state (branched on anim_loop) ────────
        XeffKeyframe[]? keyframes = null;
        XeffStaticState? staticState = null;

        if (animLoop != 0)
        {
            // Branch A: animated keyframe array. Keyframe count == tex_count.
            // spec: Docs/RE/formats/effects.md §A.3.6 Branch A: PARSER-CONFIRMED, SAMPLE-UNVERIFIED.
            keyframes = new XeffKeyframe[(int)texCount];
            for (int k = 0; k < (int)texCount; k++)
            {
                // 10 × f32le or u32le fields per keyframe.
                // spec: Docs/RE/formats/effects.md §A.3.6 Branch A — 10 fields × 4 bytes: PARSER-CONFIRMED.
                EnsureBytes(span, offset, 40, $"element[{elementIndex}] keyframe[{k}]");
                uint kfIndex = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
                float p0 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
                float p1 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
                float p2 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]);
                float p3 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]);
                float p4 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]);
                float p5 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 24)..]);
                // kf_rot_x_deg, kf_rot_y_deg, kf_rot_z_deg in degrees (stored on disk).
                // spec: Docs/RE/formats/effects.md §A.3.6 Branch A — kf_rot_x_deg CONFIRMED (degrees).
                float rotXDeg = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 28)..]);
                float rotYDeg = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 32)..]);
                float rotZDeg = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 36)..]);
                offset += 40;

                keyframes[k] = new XeffKeyframe
                {
                    KfIndex = kfIndex,
                    Params = [p0, p1, p2, p3, p4, p5],
                    RotXDeg = rotXDeg, RotYDeg = rotYDeg, RotZDeg = rotZDeg,
                };
            }
        }
        else
        {
            // Branch B: static state. Always 6 floats; +3 rotation floats only if emitter_type == 2.
            // spec: Docs/RE/formats/effects.md §A.3.6 Branch B: PARSER-CONFIRMED, SAMPLE-UNVERIFIED.
            bool hasRotation = emitterType == EmitterDirectional;
            int branchBytes = hasRotation ? 36 : 24;
            EnsureBytes(span, offset, branchBytes, $"element[{elementIndex}] Group F Branch B");

            float sp0 = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            float sp1 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
            float sp2 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
            float sp3 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]);
            float sp4 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]);
            float sp5 = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]);
            offset += 24;

            float sRotX = 0f, sRotY = 0f, sRotZ = 0f;
            if (hasRotation)
            {
                // static_rot_x_deg, static_rot_y_deg, static_rot_z_deg — degrees on disk.
                // spec: Docs/RE/formats/effects.md §A.3.6 Branch B — static_rot_*_deg: CONFIRMED.
                sRotX = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
                sRotY = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
                sRotZ = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
                offset += 12;
            }

            staticState = new XeffStaticState
            {
                Params = [sp0, sp1, sp2, sp3, sp4, sp5],
                RotXDeg = hasRotation ? sRotX : null,
                RotYDeg = hasRotation ? sRotY : null,
                RotZDeg = hasRotation ? sRotZ : null,
            };
        }

        return new XeffElement
        {
            EmitterType = emitterType,
            EmitterSubtype = emitterSubtype,
            AnimFlag = animFlag,
            TexCount = texCount,
            FieldUnknownA = fieldUnknownA,
            TextureNames = texNames,
            AlphaKeyframes = alphaKeys,
            ScaleX = scaleX,
            ScaleY = scaleY,
            ScaleZ = scaleZ,
            AnimLoop = animLoop,
            AnimStride = animStride,
            AnimBaseTime = animBaseTime,
            AnimKeyframes = keyframes,
            StaticState = staticState,
        };
    }

    private static float[] ReadFloatArray(ReadOnlySpan<byte> span, ref int offset, string fieldName)
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
                "spec: Docs/RE/formats/effects.md §A.3.");
    }
}