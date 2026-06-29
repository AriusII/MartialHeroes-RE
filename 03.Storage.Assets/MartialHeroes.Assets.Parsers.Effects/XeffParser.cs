using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

public static class XeffParser
{
    private const uint XeffInvalidMagic = 0x46464558;

    private const int XeffHeaderSize = 8;

    private const int ElementFixedHeadSize = 24;

    private const int CurveCountBytes = 16;

    private const int TexNameLen = 64;

    private const int TrackHeaderSize = 9;

    private const int MinSubEffectBytes = ElementFixedHeadSize + CurveCountBytes + TrackHeaderSize;

    private const uint EmitterDirectional = 2;

    private const int AnimatedKeyframeStride = 40;

    private const int StaticStateBytes = 24;

    private const int StaticStateRotationBytes = 36;

    private const int EffVertexStride = 32;

    public static XeffData ParseXeff(ReadOnlyMemory<byte> data)
    {
        return ParseXeff(data.Span);
    }

    public static XeffData ParseXeff(ReadOnlySpan<byte> span)
    {
        if (span.Length < XeffHeaderSize)
            throw new InvalidDataException(
                $".xeff parse error: buffer too short for {XeffHeaderSize}-byte header (got {span.Length}).");

        var effectId = BinaryPrimitives.ReadUInt32LittleEndian(span);
        if (effectId == XeffInvalidMagic)
            throw new InvalidDataException(
                $".xeff parse error: effect_id == 0x{XeffInvalidMagic:X8} (corrupt-file sentinel).");

        var subEffectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);

        if (XeffHeaderSize + (long)subEffectCount * MinSubEffectBytes > span.Length)
            throw new InvalidDataException(
                $".xeff parse error: sub_effect_count={subEffectCount} cannot fit in a {span.Length}-byte buffer " +
                $"(minimum {MinSubEffectBytes} bytes per sub-effect).");

        var offset = XeffHeaderSize;
        var count = (int)subEffectCount;
        var subEffects = new XeffSubEffect[count];

        for (var s = 0; s < count; s++)
            subEffects[s] = ReadSubEffect(span, ref offset, s);

        return new XeffData
        {
            EffectId = effectId,
            SubEffectCount = subEffectCount,
            SubEffects = subEffects
        };
    }

    private static XeffSubEffect ReadSubEffect(
        ReadOnlySpan<byte> span, ref int offset, int subIndex)
    {
        EnsureBytes(span, offset, ElementFixedHeadSize, $"sub_effect[{subIndex}] element fixed head");

        var emitterType = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        var resourceId = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
        var animFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);
        var blendMode = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);
        var elementDword2 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);
        var texCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 20)..]);

        offset += ElementFixedHeadSize;

        var nameTableBytes = (long)texCount * TexNameLen;
        EnsureBytes(span, offset, nameTableBytes, $"sub_effect[{subIndex}] name table");

        var entryCount = (int)texCount;
        var texNames = new string[entryCount];
        for (var t = 0; t < entryCount; t++)
        {
            var nameBytes = span.Slice(offset + t * TexNameLen, TexNameLen);
            var nullIdx = nameBytes.IndexOf((byte)0);
            texNames[t] = Cp949Encoding.Instance.GetString(
                nullIdx >= 0 ? nameBytes[..nullIdx] : nameBytes);
        }

        offset += (int)nameTableBytes;

        var opacity = ReadOpacityCurve(span, ref offset, $"sub_effect[{subIndex}] alpha curve");
        var diffuseR = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] pass2 curve");
        var diffuseG = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] pass3 curve");
        var diffuseB = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] pass4 curve");

        EnsureBytes(span, offset, TrackHeaderSize, $"sub_effect[{subIndex}] track header");

        var animLoop = span[offset];
        var animStride = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 1)..]);
        var animBaseTime = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 5)..]);
        offset += TrackHeaderSize;

        XeffKeyframe[] keyframes;
        if (animLoop != 0)
        {
            var totalKfBytes = (long)texCount * AnimatedKeyframeStride;
            EnsureBytes(span, offset, totalKfBytes, $"sub_effect[{subIndex}] animated keyframes");

            keyframes = new XeffKeyframe[entryCount];
            for (var k = 0; k < entryCount; k++)
            {
                var kfIndex = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
                offset += 4;
                keyframes[k] = ReadNineFloats(span, ref offset, kfIndex);
            }
        }
        else
        {
            var hasRotation = emitterType == EmitterDirectional;
            var staticBytes = hasRotation ? StaticStateRotationBytes : StaticStateBytes;
            EnsureBytes(span, offset, staticBytes, $"sub_effect[{subIndex}] static-state entry");

            var vx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            var vy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
            var vz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
            var sx = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]);
            var sy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]);
            var sz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]);
            offset += StaticStateBytes;

            float rxd = 0f, ryd = 0f, rzd = 0f;
            if (hasRotation)
            {
                rxd = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
                ryd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
                rzd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
                offset += 12;
            }

            keyframes = [new XeffKeyframe(0, vx, vy, vz, sx, sy, sz, rxd, ryd, rzd)];
        }

        return new XeffSubEffect
        {
            EmitterType = emitterType,
            ResourceId = resourceId,
            AnimFlag = animFlag,
            BlendMode = blendMode,
            ElementDword2 = elementDword2,
            EntryCount = texCount,
            TextureNames = texNames,
            Opacity = opacity,
            DiffuseR = diffuseR,
            DiffuseG = diffuseG,
            DiffuseB = diffuseB,
            AnimLoop = animLoop,
            AnimStride = animStride,
            AnimBaseTime = animBaseTime,
            Keyframes = keyframes
        };
    }

    private static XeffKeyframe ReadNineFloats(ReadOnlySpan<byte> span, ref int offset, uint kfIndex)
    {
        var vx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
        var vy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
        var vz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
        var sx = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]);
        var sy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]);
        var sz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]);
        var rxd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 24)..]);
        var ryd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 28)..]);
        var rzd = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 32)..]);
        offset += 36;

        return new XeffKeyframe(kfIndex, vx, vy, vz, sx, sy, sz, rxd, ryd, rzd);
    }

    private static float[] ReadOpacityCurve(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        EnsureBytes(span, offset, 4, $"{fieldName} count");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        EnsureBytes(span, offset, (long)count * 4, $"{fieldName} values");
        var arr = new float[(int)count];
        for (var i = 0; i < (int)count; i++)
            arr[i] = 1f - BinaryPrimitives.ReadSingleLittleEndian(span[(offset + i * 4)..]);
        offset += (int)count * 4;
        return arr;
    }

    private static float[] ReadFloatCurve(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        EnsureBytes(span, offset, 4, $"{fieldName} count");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        EnsureBytes(span, offset, (long)count * 4, $"{fieldName} values");
        var arr = new float[(int)count];
        for (var i = 0; i < (int)count; i++)
            arr[i] = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + i * 4)..]);
        offset += (int)count * 4;
        return arr;
    }

    public static EffObjectShape ParseEff(ReadOnlyMemory<byte> data)
    {
        return ParseEff(data.Span);
    }

    public static EffObjectShape ParseEff(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4)
            throw new InvalidDataException(
                $".eff parse error: buffer too short for 4-byte index_count (got {span.Length}).");

        var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var indexBytes = (long)indexCount * 2;

        if (span.Length < 4 + indexBytes + 4)
            throw new InvalidDataException(
                $".eff parse error: truncated at index array or vert_count (need {4 + indexBytes + 4}, got {span.Length}).");

        var indices = new ushort[(int)indexCount];
        for (var i = 0; i < (int)indexCount; i++)
            indices[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(4 + i * 2)..]);

        var vertCountOffset = (int)(4 + indexBytes);
        var vertCount = BinaryPrimitives.ReadUInt32LittleEndian(span[vertCountOffset..]);

        var vertBytes = (long)vertCount * EffVertexStride;
        var expectedTotal = vertCountOffset + 4 + vertBytes;
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $".eff parse error: truncated at vertex array (need {expectedTotal}, got {span.Length}).");

        var vertBase = vertCountOffset + 4;
        var vertices = new EffVertex[(int)vertCount];
        for (var v = 0; v < (int)vertCount; v++)
        {
            var vOff = vertBase + v * EffVertexStride;
            vertices[v] = new EffVertex(
                BinaryPrimitives.ReadSingleLittleEndian(span[vOff..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 8)..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 12)..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 16)..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 20)..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 24)..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 28)..])
            );
        }

        return new EffObjectShape { Indices = indices, Vertices = vertices };
    }

    private static void EnsureBytes(ReadOnlySpan<byte> span, int offset, long needed, string fieldName)
    {
        if (offset + needed > span.Length)
            throw new InvalidDataException(
                $".xeff parse error: truncated reading '{fieldName}' — " +
                $"need {needed} bytes at offset {offset}, buffer length {span.Length}.");
    }
}