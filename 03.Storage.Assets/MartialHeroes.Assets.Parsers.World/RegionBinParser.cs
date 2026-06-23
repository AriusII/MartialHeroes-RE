using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class RegionBinParser
{
    private const int MinFileSize = 16;

    private const int WidthOffset = 0x00;

    private const int HeightOffset = 0x04;

    private const int CellsOffset = 0x08;


    public static RegionGrid Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static RegionGrid Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length < MinFileSize)
            throw new InvalidDataException(
                $"region.bin parse error: buffer length {span.Length} is too short " +
                $"(minimum {MinFileSize} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §Layout A.");

        var rawWidth = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);

        var rawHeight = BinaryPrimitives.ReadUInt32LittleEndian(span[HeightOffset..]);

        var cellCount = (long)rawWidth * rawHeight;
        var expectedTotal = CellsOffset + cellCount + 4L + 4L;
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $"region.bin parse error: buffer length {span.Length} is too short for a " +
                $"{rawWidth}×{rawHeight} grid ({cellCount} cells, expected total {expectedTotal} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §Layout A.");

        var cells = new byte[(int)cellCount];
        span.Slice(CellsOffset, (int)cellCount).CopyTo(cells);

        var originXOffset = CellsOffset + (int)cellCount;

        var originX = BinaryPrimitives.ReadInt32LittleEndian(span[originXOffset..]);

        var originZ = BinaryPrimitives.ReadInt32LittleEndian(span[(originXOffset + 4)..]);

        return new RegionGrid
        {
            Width = (int)rawWidth,
            Height = (int)rawHeight,
            Cells = cells,
            OriginX = originX,
            OriginZ = originZ
        };
    }
}