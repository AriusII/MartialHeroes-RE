using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Mapping;

public static class TerrainGltfConverter
{
    private const int GridSize = TerrainCell.GridSize;

    private const int QuadSize = GridSize - 1;

    private const float VertexSpacing = 16.0f;


    private const uint GlbMagic = 0x46546C67u;
    private const uint GlbVersion = 2u;
    private const uint ChunkTypeJson = 0x4E4F534Au;
    private const uint ChunkTypeBin = 0x004E4942u;

    private const int ComponentTypeUnsignedByte = 5121;
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;
    private const int ComponentTypeFloat = 5126;

    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;


    public static void WriteGlb(TerrainCell cell, Stream output)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(output);

        const int vertexCount = GridSize * GridSize;
        const int triangleCount = QuadSize * QuadSize * 2;
        const int indexCount = triangleCount * 3;

        const bool use32Bit = false;

        var binaryBuffer = BuildBinaryBuffer(
            cell, vertexCount, indexCount,
            out var posOffset, out var posLength,
            out var nrmOffset, out var nrmLength,
            out var uvOffset, out var uvLength,
            out var colOffset, out var colLength,
            out var idxOffset, out var idxLength);

        var json = BuildJson(
            cell, binaryBuffer.Length,
            vertexCount, indexCount, use32Bit,
            posOffset, posLength,
            nrmOffset, nrmLength,
            uvOffset, uvLength,
            colOffset, colLength,
            idxOffset, idxLength);

        WriteGlbChunks(output, json, binaryBuffer);
    }


    private static byte[] BuildBinaryBuffer(
        TerrainCell cell,
        int vertexCount, int indexCount,
        out int posOffset, out int posLength,
        out int nrmOffset, out int nrmLength,
        out int uvOffset, out int uvLength,
        out int colOffset, out int colLength,
        out int idxOffset, out int idxLength)
    {
        posLength = vertexCount * 3 * sizeof(float);
        nrmLength = vertexCount * 3 * sizeof(float);
        uvLength = vertexCount * 2 * sizeof(float);
        colLength = vertexCount * 4;
        idxLength = indexCount * sizeof(ushort);

        posOffset = 0;
        nrmOffset = Align4(posOffset + posLength);
        uvOffset = Align4(nrmOffset + nrmLength);
        colOffset = Align4(uvOffset + uvLength);
        idxOffset = Align4(colOffset + colLength);
        var bufSize = Align4(idxOffset + idxLength);

        var buf = new byte[bufSize];

        var cursor = posOffset;
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            var vi = r * GridSize + c;
            var worldX = c * VertexSpacing;
            var worldY = cell.Heights[vi];
            var worldZ = -(r * VertexSpacing);

            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), worldX);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), worldY);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), worldZ);
            cursor += 12;
        }

        cursor = nrmOffset;
        var normals = cell.Normals;
        for (var vi = 0; vi < vertexCount; vi++)
        {
            var (nx, ny, nz) = normals[vi];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), nx);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), ny);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), -nz);
            cursor += 12;
        }

        cursor = uvOffset;
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            var u = c / (float)(GridSize - 1);
            var v = r / (float)(GridSize - 1);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), u);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), v);
            cursor += 8;
        }

        cursor = colOffset;
        var diffuse = cell.DiffuseColours;
        for (var vi = 0; vi < vertexCount; vi++)
        {
            var (dr, dg, db, _) = diffuse[vi];
            buf[cursor + 0] = (byte)Math.Clamp((int)(dr * 255f + 0.5f), 0, 255);
            buf[cursor + 1] = (byte)Math.Clamp((int)(dg * 255f + 0.5f), 0, 255);
            buf[cursor + 2] = (byte)Math.Clamp((int)(db * 255f + 0.5f), 0, 255);
            buf[cursor + 3] = 255;
            cursor += 4;
        }

        cursor = idxOffset;
        for (var r = 0; r < QuadSize; r++)
        for (var c = 0; c < QuadSize; c++)
        {
            var vi0 = (ushort)(r * GridSize + c);
            var vi1 = (ushort)(r * GridSize + c + 1);
            var vi2 = (ushort)((r + 1) * GridSize + c);
            var vi3 = (ushort)((r + 1) * GridSize + c + 1);

            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), vi0);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), vi2);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), vi1);
            cursor += 6;

            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), vi1);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), vi2);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), vi3);
            cursor += 6;
        }

        return buf;
    }


    private static string BuildJson(
        TerrainCell cell,
        int bufferByteLength,
        int vertexCount, int indexCount, bool use32Bit,
        int posOffset, int posLength,
        int nrmOffset, int nrmLength,
        int uvOffset, int uvLength,
        int colOffset, int colLength,
        int idxOffset, int idxLength)
    {
        var minPosX = 0f;
        var maxPosX = (GridSize - 1) * VertexSpacing;
        var minPosZ = -((GridSize - 1) * VertexSpacing);
        var maxPosZ = 0f;

        var minPosY = float.MaxValue;
        var maxPosY = float.MinValue;
        foreach (var h in cell.Heights)
        {
            if (h < minPosY) minPosY = h;
            if (h > maxPosY) maxPosY = h;
        }

        if (cell.Heights.Length == 0)
        {
            minPosY = 0f;
            maxPosY = 0f;
        }

        var indexComponentType = use32Bit ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;

        var sb = new StringBuilder(1024);
        sb.Append('{');

        sb.Append(
            "\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.TerrainGltfConverter\"},");

        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append("\"POSITION\":0,");
        sb.Append("\"NORMAL\":1,");
        sb.Append("\"TEXCOORD_0\":2,");
        sb.Append("\"COLOR_0\":3");
        sb.Append("},");
        sb.Append("\"indices\":4");
        sb.Append("}]}],");

        sb.Append("\"accessors\":[");

        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC3\",");
        sb.Append($"\"min\":[{F(minPosX)},{F(minPosY)},{F(minPosZ)}],");
        sb.Append($"\"max\":[{F(maxPosX)},{F(maxPosY)},{F(maxPosZ)}]");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC3\"");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"bufferView\":2,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC2\"");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"bufferView\":3,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeUnsignedByte},");
        sb.Append("\"normalized\":true,");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC4\"");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"bufferView\":4,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{indexComponentType},");
        sb.Append($"\"count\":{indexCount},");
        sb.Append("\"type\":\"SCALAR\"");
        sb.Append('}');

        sb.Append("],");

        sb.Append("\"bufferViews\":[");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{posOffset},");
        sb.Append($"\"byteLength\":{posLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{nrmOffset},");
        sb.Append($"\"byteLength\":{nrmLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{uvOffset},");
        sb.Append($"\"byteLength\":{uvLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{colOffset},");
        sb.Append($"\"byteLength\":{colLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{idxOffset},");
        sb.Append($"\"byteLength\":{idxLength},");
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

        Span<byte> jsonChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr, (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr[4..], ChunkTypeJson);
        output.Write(jsonChunkHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20);

        Span<byte> binChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr[4..], ChunkTypeBin);
        output.Write(binChunkHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00);
    }

    private static void WritePadding(Stream output, int actualLength, int paddedLength, byte padByte)
    {
        var pad = paddedLength - actualLength;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

    private static string F(float v)
    {
        return v.ToString("G9", CultureInfo.InvariantCulture);
    }
}