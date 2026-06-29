using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Core;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

public static class AnimationParser
{
    private const int KeyframeStride = 28;

    private static ReadOnlySpan<byte> BaniMagic => "BANI"u8;

    public static bool IsBaniVariant(ReadOnlySpan<byte> data)
    {
        return data.Length >= 4 && data[..4].SequenceEqual(BaniMagic);
    }

    public static AnimationClip? Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static AnimationClip? Parse(ReadOnlySpan<byte> data)
    {
        if (IsBaniVariant(data))
            return null;

        var offset = 0;

        var idA = ReadU32LE(data, ref offset, "id_a");

        var idB = ReadU32LE(data, ref offset, "id_b");

        var name = LenStrReader.Read(data, ref offset);

        var frameCount = ReadU32LE(data, ref offset, "frame_count");

        var trackCount = ReadU32LE(data, ref offset, "track_count");

        var tracks = ReadTrackArray(data, ref offset, trackCount);

        return new AnimationClip
        {
            IdA = idA,
            IdB = idB,
            Name = name,
            FrameCount = frameCount,
            Tracks = tracks
        };
    }

    public static BaniClip? ParseBani(ReadOnlyMemory<byte> data)
    {
        return ParseBani(data.Span);
    }

    public static BaniClip? ParseBani(ReadOnlySpan<byte> data)
    {
        if (!IsBaniVariant(data))
            return null;

        var offset = 4;

        var version = ReadU32LE(data, ref offset, "version");

        var animId = ReadU32LE(data, ref offset, "anim_id");

        var rigGroupId = ReadU32LE(data, ref offset, "rig_group_id");

        var name = LenStrReader.Read(data, ref offset);

        var frameCount = ReadU32LE(data, ref offset, "frame_count");

        var trackCount = ReadU32LE(data, ref offset, "track_count");

        var tracks = ReadTrackArray(data, ref offset, trackCount);

        return new BaniClip
        {
            Version = version,
            AnimId = animId,
            RigGroupId = rigGroupId,
            Name = name,
            FrameCount = frameCount,
            Tracks = tracks
        };
    }

    private static AnimationTrack[] ReadTrackArray(ReadOnlySpan<byte> data, ref int offset, uint trackCount)
    {
        var tracks = new AnimationTrack[trackCount];

        for (var t = 0; t < (int)trackCount; t++)
        {
            var trackDescriptor = ReadU32LE(data, ref offset, $"track[{t}].track_descriptor");
            var boneId = (byte)(trackDescriptor & 0xFF);

            var keyCount = ReadU32LE(data, ref offset, $"track[{t}].key_count");

            var keyframeBytesNeeded = (long)keyCount * KeyframeStride;
            if (offset + keyframeBytesNeeded > data.Length)
                throw new InvalidDataException(
                    $".mot parse error: track[{t}] keyframe block truncated — " +
                    $"key_count={keyCount} requires {keyframeBytesNeeded} bytes at offset {offset}, " +
                    $"but buffer length is {data.Length}.");

            var keyframes = new Keyframe[(int)keyCount];

            for (var k = 0; k < (int)keyCount; k++)
            {
                var tx = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                var ty = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                var tz = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                var rx = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                var ry = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                var rz = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                var rw = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
                offset += 4;

                keyframes[k] = new Keyframe
                {
                    Translation = new Vec3(tx, ty, tz),
                    Rotation = new Quat(rx, ry, rz, rw)
                };
            }

            tracks[t] = new AnimationTrack
            {
                BoneId = boneId,
                Keyframes = keyframes
            };
        }

        return tracks;
    }

    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".mot parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        var value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}