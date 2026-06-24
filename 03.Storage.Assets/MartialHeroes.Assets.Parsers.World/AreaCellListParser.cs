using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class AreaCellListParser
{
    private const int CountFieldSize = 4;
    private const int CellKeySize = 4;
    private const int MinFileSize = CountFieldSize;

    public static AreaCellList Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < MinFileSize)
            throw new InvalidDataException(
                $"d<NNN>.lst parse error: buffer length {span.Length} is too short " +
                $"(minimum {MinFileSize} bytes — leading cellCount u32 field). " +
                "spec: Docs/RE/formats/region_grid.md §d<NNN>.lst");

        var cellCount = BinaryPrimitives.ReadUInt32LittleEndian(span);

        var expectedBody = (long)cellCount * CellKeySize;
        var expectedTotal = CountFieldSize + expectedBody;

        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $"d<NNN>.lst parse error: buffer length {span.Length} is too short for " +
                $"{cellCount} cell-key entries (expected {expectedTotal} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §d<NNN>.lst");

        var keyCount = (int)cellCount;
        var keys = new uint[keyCount];

        for (var i = 0; i < keyCount; i++)
        {
            var offset = CountFieldSize + i * CellKeySize;
            keys[i] = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        }

        return new AreaCellList
        {
            CellCount = cellCount,
            CellKeys = keys
        };
    }
}