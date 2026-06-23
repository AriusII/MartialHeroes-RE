using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

public static class XeffParser
{
    private const uint XeffInvalidMagic = 0x46464558;

    private const int XeffHeaderSize = 8;

    private const int ElementFixedHeadSize = 24;

    private const int TexNameLen = 64;

    private const int TrackHeaderSize = 9;

    private const uint EmitterBillboard = 0;
    private const uint EmitterMesh = 1;
    private const uint EmitterDirectional = 2;


    private const int EffVertexStride = 32;

    public static XeffData ParseXeff(ReadOnlyMemory<byte> data)
    {
        return ParseXeff(data.Span);
    }

    public static XeffData ParseXeff(ReadOnlySpan<byte> span)
    {
        if (span.Length < XeffHeaderSize)
            throw new InvalidDataException(
                $".xeff parse error: buffer too short for {XeffHeaderSize}-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §A.2.");

        var effectId = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        if (effectId == XeffInvalidMagic)
            throw new InvalidDataException(
                $".xeff parse error: effect_id == 0x{XeffInvalidMagic:X8} (anti-magic sentinel). " +
                "File is corrupt. spec: Docs/RE/formats/effects.md §A.1.");

        var subEffectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);

        var offset = XeffHeaderSize;
        var subEffects = new XeffSubEffect[(int)subEffectCount];

        for (var s = 0; s < (int)subEffectCount; s++) subEffects[s] = ReadSubEffect(span, ref offset, s);

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

        var fieldUnknownA = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);

        var elementDword2 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);

        var texCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 20)..]);

        offset += ElementFixedHeadSize;

        var nameTableBytes = (long)texCount * TexNameLen;
        EnsureBytes(span, offset, nameTableBytes, $"sub_effect[{subIndex}] name table");

        var texNames = new string[(int)texCount];
        for (var t = 0; t < (int)texCount; t++)
        {
            var nameBytes = span.Slice(offset + t * TexNameLen, TexNameLen);
            var nullIdx = nameBytes.IndexOf((byte)0);
            texNames[t] = Encoding.ASCII.GetString(
                nullIdx >= 0 ? nameBytes[..nullIdx] : nameBytes);
        }

        offset += (int)nameTableBytes;


        var alphaKeys = ReadFloatCurve(span, ref offset, $"sub_effect[{subIndex}] alpha curve");

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
            keyframes = new XeffKeyframe[(int)texCount];

            var totalKfBytes = (long)texCount * 40;
            if (offset + totalKfBytes > span.Length)
                throw new InvalidDataException(
                    $".xeff parse error: sub_effect[{subIndex}] animated keyframe block truncated — " +
                    $"need {texCount} × 40 = {totalKfBytes} bytes at offset {offset}, " +
                    $"buffer length {span.Length}. spec: Docs/RE/formats/effects.md §A.4.4.");

            for (var k = 0; k < (int)texCount; k++)
            {
                var kfIndex = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
                offset += 4;

                keyframes[k] = ReadNineFloats(span, ref offset, kfIndex, subIndex, k);
            }
        }
        else
        {
            var hasRotation = emitterType == EmitterDirectional;
            var staticBytes = hasRotation ? 36 : 24;
            EnsureBytes(span, offset, staticBytes, $"sub_effect[{subIndex}] static-state entry");

            var vx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            var vy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
            var vz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]);
            var sx = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 12)..]);
            var sy = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 16)..]);
            var sz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 20)..]);
            offset += 24;

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
                    RotZDeg = rzd
                }
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
            DiffuseR = diffuseR,
            DiffuseG = diffuseG,
            DiffuseB = diffuseB,
            AnimLoop = animLoop,
            AnimStride = animStride,
            AnimBaseTime = animBaseTime,
            Keyframes = keyframes
        };
    }

    private static XeffKeyframe ReadNineFloats(
        ReadOnlySpan<byte> span, ref int offset, uint kfIndex, int subIndex, int k)
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
            RotZDeg = rzd
        };
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
                $".eff parse error: buffer too short for 4-byte index_count (got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §B.3.");

        var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var indexBytes = (long)indexCount * 2;

        if (span.Length < 4 + indexBytes + 4)
            throw new InvalidDataException(
                $".eff parse error: truncated at index array or vert_count (need {4 + indexBytes + 4}, got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §B.3.");

        var indices = new ushort[(int)indexCount];
        for (var i = 0; i < (int)indexCount; i++)
            indices[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(4 + i * 2)..]);

        var vertCountOffset = (int)(4 + indexBytes);
        var vertCount = BinaryPrimitives.ReadUInt32LittleEndian(span[vertCountOffset..]);

        var vertBytes = (long)vertCount * EffVertexStride;
        var expectedTotal = vertCountOffset + 4 + vertBytes;
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $".eff parse error: truncated at vertex array (need {expectedTotal}, got {span.Length}). " +
                "spec: Docs/RE/formats/effects.md §B.4.");

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
                $"need {needed} bytes at offset {offset}, buffer length {span.Length}. " +
                "spec: Docs/RE/formats/effects.md §A.4.");
    }
}