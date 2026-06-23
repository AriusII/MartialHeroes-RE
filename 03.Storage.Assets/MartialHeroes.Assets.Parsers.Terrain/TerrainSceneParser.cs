using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class TerrainSceneParser
{
    private const uint VertexCountWarnThreshold = 3072;

    private const int VertexStride = 32;

    public static BudScene Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    public static BudScene Parse(ReadOnlySpan<byte> span)
    {
        return Parse(span, ReadOnlyMemory<byte>.Empty);
    }

    private static BudScene Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        var offset = 0;

        EnsureBytes(span, offset, 4, "objectCount");
        var objectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        var objects = new BudObject[objectCount];

        for (var i = 0; i < (int)objectCount; i++)
        {
            EnsureBytes(span, offset, 9, $"object[{i}] header");

            var typeByte = span[offset];
            offset += 1;

            var texId = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            if (vertexCount > VertexCountWarnThreshold)
                Debug.WriteLine(
                    $"[TerrainSceneParser] .bud object[{i}] vertex_count {vertexCount} " +
                    $"exceeds the legacy warn-threshold {VertexCountWarnThreshold} — continuing " +
                    "(warn-only, not an error). " +
                    "spec: Docs/RE/formats/terrain_scene.md §vertex_count warn-and-continue.");

            var vertexBytes = (long)vertexCount * VertexStride;
            EnsureBytes(span, offset, vertexBytes, $"object[{i}] vertex array");

            var vertices = new BudVertex[(int)vertexCount];
            if (BitConverter.IsLittleEndian)
            {
                var vBlock = span.Slice(offset, (int)vertexBytes);
                var floatBlock = MemoryMarshal.Cast<byte, float>(vBlock);
                for (var v = 0; v < (int)vertexCount; v++)
                {
                    var fi = v * 8;
                    vertices[v] = new BudVertex(
                        floatBlock[fi],
                        floatBlock[fi + 1],
                        floatBlock[fi + 2],
                        floatBlock[fi + 3],
                        floatBlock[fi + 4],
                        floatBlock[fi + 5],
                        floatBlock[fi + 6],
                        floatBlock[fi + 7]
                    );
                }
            }
            else
            {
                for (var v = 0; v < (int)vertexCount; v++)
                {
                    var vOff = offset + v * VertexStride;
                    vertices[v] = new BudVertex(
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
            }

            offset += (int)vertexBytes;

            EnsureBytes(span, offset, 4, $"object[{i}] index_count");
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            var indexBytes = (long)indexCount * 2;
            EnsureBytes(span, offset, indexBytes, $"object[{i}] index array");

            var indices = new ushort[(int)indexCount];
            var iBlock = span.Slice(offset, (int)indexBytes);
            if (BitConverter.IsLittleEndian)
                MemoryMarshal.Cast<byte, ushort>(iBlock).CopyTo(indices);
            else
                for (var idx = 0; idx < (int)indexCount; idx++)
                    indices[idx] = BinaryPrimitives.ReadUInt16LittleEndian(iBlock[(idx * 2)..]);

            offset += (int)indexBytes;

            objects[i] = new BudObject
            {
                TypeByte = typeByte,
                TexId = texId,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new BudScene { Objects = objects };
    }

    private static void EnsureBytes(ReadOnlySpan<byte> span, int offset, long needed, string fieldName)
    {
        if (offset + needed > span.Length)
            throw new InvalidDataException(
                $".bud parse error: buffer truncated reading '{fieldName}' — " +
                $"need {needed} bytes at offset {offset}, buffer length {span.Length}. " +
                $"spec: Docs/RE/formats/terrain_scene.md §File-size formula.");
    }
}