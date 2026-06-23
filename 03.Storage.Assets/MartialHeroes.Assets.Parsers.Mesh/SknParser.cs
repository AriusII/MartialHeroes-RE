using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Core;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Parsers.Mesh;

public static class SknParser
{
    private const int CornerStride = 12;
    private const int FaceStride = 36;

    private const int VertexStride = 24;

    private const int WeightStride = 12;

    public static SkinnedMesh Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static SkinnedMesh Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;


        var idA = ReadU32LE(data, ref offset, "id_a");
        var idB = ReadU32LE(data, ref offset, "id_b");

        var name = LenStrReader.Read(data, ref offset);

        var faceCount = ReadU32LE(data, ref offset, "face_count");

        var faceDataBytes = (long)faceCount * FaceStride;
        if (offset + faceDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn face table truncated: face_count={faceCount} requires {faceDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var cornerCount = checked((int)(faceCount * 3));
        var corners = new SknCorner[cornerCount];

        for (var f = 0; f < (int)faceCount; f++)
        for (var c = 0; c < 3; c++)
        {
            var vIdx = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            var uvU = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var uvV = 1.0f - BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;

            corners[f * 3 + c] = new SknCorner(vIdx, uvU, uvV);
        }

        var vertexCount = ReadU32LE(data, ref offset, "vertex_count");

        var vertexDataBytes = (long)vertexCount * VertexStride;
        if (offset + vertexDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn vertex table truncated: vertex_count={vertexCount} requires {vertexDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var positions = new Vec3[vertexCount];
        var normals = new Vec3[vertexCount];

        for (var v = 0; v < (int)vertexCount; v++)
        {
            var normX = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var normY = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var normZ = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var posX = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var posY = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            var posZ = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;

            positions[v] = new Vec3(posX, posY, posZ);
            normals[v] = new Vec3(normX, normY, normZ);
        }

        var weightCount = ReadU32LE(data, ref offset, "weight_count");

        var weightDataBytes = (long)weightCount * WeightStride;
        if (offset + weightDataBytes > data.Length)
            throw new InvalidDataException(
                $".skn weight table truncated: weight_count={weightCount} requires {weightDataBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var weights = new SknWeight[weightCount];
        for (var w = 0; w < (int)weightCount; w++)
        {
            var wVertIdx = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            var wBoneId = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            var wVal = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
            offset += 4;
            weights[w] = new SknWeight(wVertIdx, wBoneId, wVal);
        }

        return new SkinnedMesh
        {
            IdA = idA,
            IdB = idB,
            Name = name,
            FaceCount = faceCount,
            Corners = corners,
            Positions = positions,
            Normals = normals,
            Weights = weights
        };
    }


    private static uint ReadU32LE(ReadOnlySpan<byte> span, ref int offset, string fieldName)
    {
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                $".skn parse error: buffer truncated reading '{fieldName}' u32 at offset {offset} " +
                $"(buffer length {span.Length}).");

        var value = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;
        return value;
    }
}