using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class LstManifestParser
{
    public static LstManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static LstManifest Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;

        if (offset + 4 > data.Length)
            throw new InvalidDataException(
                $".lst parse error: buffer too short for count field (buffer length {data.Length}).");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        var keysBytes = (long)count * 4;
        if (offset + keysBytes > data.Length)
            throw new InvalidDataException(
                $".lst parse error: key array truncated — count={count} requires {keysBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        var entries = new LstCellEntry[(int)count];

        for (var i = 0; i < (int)count; i++)
        {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            var mapX = (int)(key / 100000u);
            var mapZ = (int)(key % 100000u);

            entries[i] = new LstCellEntry(key, mapX, mapZ);
        }

        return new LstManifest { Entries = entries };
    }

    public static uint ComputeKey(int mapX, int mapZ)
    {
        return (uint)mapZ + 100000u * (uint)mapX;
    }
}