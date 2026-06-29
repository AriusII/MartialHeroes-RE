using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class RegionGridParser
{
    private const int HeightOffset = 4;
    private const int CellsOffset = 8;
    private const int OriginSize = 4;
    private const int MinFileSize = 16;

    public static RegionGridData Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < MinFileSize)
            throw new InvalidDataException(
                $"region.bin parse error: buffer length {span.Length} is too short " +
                $"(minimum {MinFileSize} bytes for width, height and the two trailing origins).");

        var width = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);

        var height = BinaryPrimitives.ReadUInt32LittleEndian(span[HeightOffset..]);

        var cellCount = (long)width * height;
        var expectedTotal = CellsOffset + cellCount + OriginSize + OriginSize;
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $"region.bin parse error: buffer length {span.Length} is too short for a " +
                $"{width}x{height} grid ({cellCount} cells) plus the two trailing origins " +
                $"(expected {expectedTotal} bytes).");

        var cells = data.Slice(CellsOffset, (int)cellCount);

        var originXOffset = CellsOffset + (int)cellCount;

        var originX = BinaryPrimitives.ReadInt32LittleEndian(span[originXOffset..]);

        var originZ = BinaryPrimitives.ReadInt32LittleEndian(span[(originXOffset + OriginSize)..]);

        return new RegionGridData
        {
            Width = width,
            Height = height,
            Cells = cells,
            OriginX = originX,
            OriginZ = originZ
        };
    }
}