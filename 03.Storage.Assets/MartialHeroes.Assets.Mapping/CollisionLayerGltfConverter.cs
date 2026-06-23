using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Mapping;

public static class CollisionLayerGltfConverter
{
    private const uint GlbMagic = 0x46546C67u;
    private const uint GlbVersion = 2u;
    private const uint ChunkTypeJson = 0x4E4F534Au;
    private const uint ChunkTypeBin = 0x004E4942u;

    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeFloat = 5126;
    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;


    public static void WriteGlb(CollisionTriangleList triangleList, Stream output)
    {
        ArgumentNullException.ThrowIfNull(triangleList);
        ArgumentNullException.ThrowIfNull(output);

        var tris = triangleList.Triangles;
        var triCount = tris.Length;
        var vertCount = triCount * 3;
        var indexCount = triCount * 3;

        var use32 = vertCount > ushort.MaxValue;

        var posLen = vertCount * 3 * sizeof(float);
        var idxStride = use32 ? sizeof(uint) : sizeof(ushort);
        var idxLen = indexCount * idxStride;

        var posOff = 0;
        var idxOff = Align4(posOff + posLen);
        var bufSize = Align4(idxOff + idxLen);

        var buf = new byte[bufSize];

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        var cursor = posOff;
        for (var t = 0; t < triCount; t++)
        {
            var tri = tris[t];

            WriteVertex(buf, ref cursor, -tri.V1X, tri.V1Y, tri.V1Z, ref minX, ref minY, ref minZ, ref maxX, ref maxY,
                ref maxZ);
            WriteVertex(buf, ref cursor, -tri.V2X, tri.V2Y, tri.V2Z, ref minX, ref minY, ref minZ, ref maxX, ref maxY,
                ref maxZ);
            WriteVertex(buf, ref cursor, -tri.V3X, tri.V3Y, tri.V3Z, ref minX, ref minY, ref minZ, ref maxX, ref maxY,
                ref maxZ);
        }

        cursor = idxOff;
        for (var t = 0; t < triCount; t++)
        {
            var base3 = t * 3;
            if (use32)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor), (uint)(base3 + 0));
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 4), (uint)(base3 + 2));
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 8), (uint)(base3 + 1));
                cursor += 12;
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), (ushort)(base3 + 0));
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), (ushort)(base3 + 2));
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), (ushort)(base3 + 1));
                cursor += 6;
            }
        }

        var json = BuildJson(bufSize, vertCount, indexCount, use32,
            posOff, posLen, idxOff, idxLen,
            minX, minY, minZ, maxX, maxY, maxZ);

        WriteGlbChunks(output, json, buf);
    }


    private static string BuildJson(
        int bufferByteLength,
        int vertCount, int indexCount, bool use32,
        int posOff, int posLen,
        int idxOff, int idxLen,
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ)
    {
        var indexComponentType = use32 ? 5125 : ComponentTypeUnsignedShort;

        var sb = new StringBuilder(512);
        sb.Append('{');
        sb.Append(
            "\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.CollisionLayerGltfConverter\"},");
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{\"POSITION\":0},");
        sb.Append("\"indices\":1");
        sb.Append("}]}],");

        sb.Append("\"accessors\":[");

        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertCount},");
        sb.Append("\"type\":\"VEC3\",");
        sb.Append($"\"min\":[{F(minX)},{F(minY)},{F(minZ)}],");
        sb.Append($"\"max\":[{F(maxX)},{F(maxY)},{F(maxZ)}]");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{indexComponentType},");
        sb.Append($"\"count\":{indexCount},");
        sb.Append("\"type\":\"SCALAR\"");
        sb.Append('}');

        sb.Append("],");

        sb.Append("\"bufferViews\":[");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{posOff},");
        sb.Append($"\"byteLength\":{posLen},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{idxOff},");
        sb.Append($"\"byteLength\":{idxLen},");
        sb.Append($"\"target\":{TargetElementArrayBuffer}");
        sb.Append('}');

        sb.Append("],");
        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");
        sb.Append('}');
        return sb.ToString();
    }


    private static void WriteGlbChunks(Stream output, string json, byte[] binaryBuffer)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPadded = Align4(jsonBytes.Length);
        var binPadded = Align4(binaryBuffer.Length);

        var totalLength = (uint)(12 + 8 + jsonPadded + 8 + binPadded);

        Span<byte> hdr = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[8..], totalLength);
        output.Write(hdr);

        Span<byte> jsonHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHdr, (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHdr[4..], ChunkTypeJson);
        output.Write(jsonHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20);

        Span<byte> binHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binHdr[4..], ChunkTypeBin);
        output.Write(binHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteVertex(
        byte[] buf, ref int cursor,
        float x, float y, float z,
        ref float minX, ref float minY, ref float minZ,
        ref float maxX, ref float maxY, ref float maxZ)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), x);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), z);
        cursor += 12;

        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
        if (z < minZ) minZ = z;
        if (z > maxZ) maxZ = z;
    }

    private static void WritePadding(Stream output, int actual, int padded, byte padByte)
    {
        var pad = padded - actual;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int v)
    {
        return (v + 3) & ~3;
    }

    private static string F(float v)
    {
        return v.ToString("G9", CultureInfo.InvariantCulture);
    }
}