using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Core;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

public static class BndParser
{
    private const int BoneRecordStride = 36;

    public static Skeleton Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static Skeleton Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;


        var actorId = ReadU32LE(data, ref offset, "actor_id");

        var actorName = LenStrReader.Read(data, ref offset);

        var boneCountRaw = ReadU32LE(data, ref offset, "bone_count");
        var boneCount = (int)(boneCountRaw & 0xFF);

        var boneDataBytes = (long)boneCount * BoneRecordStride;
        if (offset + boneDataBytes > data.Length)
            throw new InvalidDataException(
                $".bnd bone array truncated: bone_count={boneCount} requires {boneDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var bones = new Bone[boneCount];

        for (var b = 0; b < boneCount; b++)
        {

            var selfId = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            var parentId = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            var tX = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var tY = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var tZ = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;

            var rX = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var rY = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var rZ = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var rW = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;

            bones[b] = new Bone(
                selfId,
                parentId,
                new Vec3(tX, tY, tZ),
                new Quat(rX, rY, rZ, rW));
        }

        return new Skeleton
        {
            ActorId = actorId,
            ActorName = actorName,
            Bones = bones
        };
    }


    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".bnd parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        var value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}