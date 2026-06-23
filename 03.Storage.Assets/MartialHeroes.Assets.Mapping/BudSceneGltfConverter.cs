using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Mapping;

public static class BudSceneGltfConverter
{
    private const uint GlbMagic = 0x46546C67u;

    private const uint GlbVersion = 2u;

    private const uint ChunkTypeJson = 0x4E4F534Au;

    private const uint ChunkTypeBin = 0x004E4942u;

    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeFloat = 5126;

    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;


    public static void WriteGlb(BudScene scene, Stream output)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(output);

        if (scene.Objects.Length == 0)
        {
            WriteEmptyGlb(output);
            return;
        }

        var objectCount = scene.Objects.Length;
        var sections = new ObjectSection[objectCount];

        var bufferCursor = 0;
        for (var i = 0; i < objectCount; i++)
        {
            var obj = scene.Objects[i];
            var vertexCount = obj.Vertices.Length;
            var indexCount = obj.Indices.Length;

            var posLen = vertexCount * 3 * sizeof(float);
            var nrmLen = vertexCount * 3 * sizeof(float);
            var uvLen = vertexCount * 2 * sizeof(float);
            var idxLen = indexCount * sizeof(ushort);

            var posOff = bufferCursor;
            var nrmOff = Align4(posOff + posLen);
            var uvOff = Align4(nrmOff + nrmLen);
            var idxOff = Align4(uvOff + uvLen);
            bufferCursor = Align4(idxOff + idxLen);

            sections[i] = new ObjectSection(
                vertexCount,
                indexCount,
                posOff, posLen,
                nrmOff, nrmLen,
                uvOff, uvLen,
                idxOff, idxLen);
        }

        var binaryBuffer = new byte[bufferCursor];

        for (var i = 0; i < objectCount; i++)
        {
            var obj = scene.Objects[i];
            var sec = sections[i];
            WriteObjectBinary(binaryBuffer, obj, sec);
        }

        var json = BuildJson(scene, sections, binaryBuffer.Length);

        WriteGlbChunks(output, json, binaryBuffer);
    }


    private static void WriteObjectBinary(byte[] buf, BudObject obj, ObjectSection sec)
    {
        var vertexCount = obj.Vertices.Length;
        var indexCount = obj.Indices.Length;

        var cursor = sec.PosOff;
        for (var v = 0; v < vertexCount; v++)
        {
            var vert = obj.Vertices[v];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -vert.PosX);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), vert.PosY);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), vert.PosZ);
            cursor += 12;
        }

        cursor = sec.NrmOff;
        for (var v = 0; v < vertexCount; v++)
        {
            var vert = obj.Vertices[v];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -vert.NormalX);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), vert.NormalY);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), vert.NormalZ);
            cursor += 12;
        }

        cursor = sec.UvOff;
        for (var v = 0; v < vertexCount; v++)
        {
            var vert = obj.Vertices[v];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), vert.UvU);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), vert.UvV);
            cursor += 8;
        }

        cursor = sec.IdxOff;
        for (var tri = 0; tri < indexCount / 3; tri++)
        {
            var i0 = obj.Indices[tri * 3 + 0];
            var i1 = obj.Indices[tri * 3 + 1];
            var i2 = obj.Indices[tri * 3 + 2];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), i0);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), i2);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), i1);
            cursor += 6;
        }
    }


    private static string BuildJson(BudScene scene, ObjectSection[] sections, int bufferByteLength)
    {
        var objectCount = scene.Objects.Length;


        var sb = new StringBuilder(512 + objectCount * 256);
        sb.Append('{');

        sb.Append(
            "\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.BudSceneGltfConverter\"},");

        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        sb.Append("\"meshes\":[{\"primitives\":[");
        for (var i = 0; i < objectCount; i++)
        {
            if (i > 0) sb.Append(',');
            var accBase = i * 4;

            sb.Append('{');
            sb.Append("\"attributes\":{");
            sb.Append($"\"POSITION\":{accBase},");
            sb.Append($"\"NORMAL\":{accBase + 1},");
            sb.Append($"\"TEXCOORD_0\":{accBase + 2}");
            sb.Append("},");
            sb.Append($"\"indices\":{accBase + 3}");

            var obj = scene.Objects[i];
            sb.Append($",\"extras\":{{\"texId\":{obj.TexId},\"typeByte\":{obj.TypeByte}}}");
            sb.Append('}');
        }

        sb.Append("]}],");

        sb.Append("\"accessors\":[");
        var firstAcc = true;
        for (var i = 0; i < objectCount; i++)
        {
            var sec = sections[i];
            var obj = scene.Objects[i];
            var bvBase = i * 4;

            ComputePosMinMax(obj.Vertices,
                out var minX, out var minY, out var minZ,
                out var maxX, out var maxY, out var maxZ);

            if (!firstAcc) sb.Append(',');
            firstAcc = false;
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},");
            sb.Append($"\"count\":{sec.VertexCount},");
            sb.Append("\"type\":\"VEC3\",");
            sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],");
            sb.Append($"\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
            sb.Append('}');

            sb.Append(',');
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase + 1},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},");
            sb.Append($"\"count\":{sec.VertexCount},");
            sb.Append("\"type\":\"VEC3\"");
            sb.Append('}');

            sb.Append(',');
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase + 2},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},");
            sb.Append($"\"count\":{sec.VertexCount},");
            sb.Append("\"type\":\"VEC2\"");
            sb.Append('}');

            sb.Append(',');
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase + 3},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeUnsignedShort},");
            sb.Append($"\"count\":{sec.IndexCount},");
            sb.Append("\"type\":\"SCALAR\"");
            sb.Append('}');
        }

        sb.Append("],");

        sb.Append("\"bufferViews\":[");
        var firstBv = true;
        for (var i = 0; i < objectCount; i++)
        {
            var sec = sections[i];

            if (!firstBv) sb.Append(',');
            firstBv = false;
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.PosOff},");
            sb.Append($"\"byteLength\":{sec.PosLen},");
            sb.Append($"\"target\":{TargetArrayBuffer}");
            sb.Append('}');

            sb.Append(',');
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.NrmOff},");
            sb.Append($"\"byteLength\":{sec.NrmLen},");
            sb.Append($"\"target\":{TargetArrayBuffer}");
            sb.Append('}');

            sb.Append(',');
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.UvOff},");
            sb.Append($"\"byteLength\":{sec.UvLen},");
            sb.Append($"\"target\":{TargetArrayBuffer}");
            sb.Append('}');

            sb.Append(',');
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.IdxOff},");
            sb.Append($"\"byteLength\":{sec.IdxLen},");
            sb.Append($"\"target\":{TargetElementArrayBuffer}");
            sb.Append('}');
        }

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

    private static void WriteEmptyGlb(Stream output)
    {
        const string emptyJson =
            "{\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.BudSceneGltfConverter\"}," +
            "\"scene\":0,\"scenes\":[{\"nodes\":[]}],\"meshes\":[],\"buffers\":[]}";
        WriteGlbChunks(output, emptyJson, Array.Empty<byte>());
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

    private static void ComputePosMinMax(
        BudVertex[] vertices,
        out float minX, out float minY, out float minZ,
        out float maxX, out float maxY, out float maxZ)
    {
        if (vertices.Length == 0)
        {
            minX = minY = minZ = 0f;
            maxX = maxY = maxZ = 0f;
            return;
        }

        minX = maxX = vertices[0].PosX;
        minY = maxY = vertices[0].PosY;
        minZ = maxZ = vertices[0].PosZ;

        for (var i = 1; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (v.PosX < minX) minX = v.PosX;
            if (v.PosX > maxX) maxX = v.PosX;
            if (v.PosY < minY) minY = v.PosY;
            if (v.PosY > maxY) maxY = v.PosY;
            if (v.PosZ < minZ) minZ = v.PosZ;
            if (v.PosZ > maxZ) maxZ = v.PosZ;
        }
    }


    private readonly record struct ObjectSection(
        int VertexCount,
        int IndexCount,
        int PosOff,
        int PosLen,
        int NrmOff,
        int NrmLen,
        int UvOff,
        int UvLen,
        int IdxOff,
        int IdxLen);
}