using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class MapBinParser
{
    private const int RecordSize = MapBinRecord.RecordSize;
    private const int ModeOffset = 0x3C;
    private const int NameMaskOffset = 0x50;

    public static MapBinRecord Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    public static MapBinRecord Parse(ReadOnlySpan<byte> span)
    {
        return Parse(span, ReadOnlyMemory<byte>.Empty);
    }

    private static MapBinRecord Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < RecordSize)
            throw new InvalidDataException(
                $"map*.bin parse error: buffer length {span.Length} is too short " +
                $"(expected exactly {RecordSize} = 0x208 bytes). " +
                "spec: Docs/RE/formats/region_grid.md §map<NNN>.bin");

        var mode = span[ModeOffset];
        var nameMask = span[NameMaskOffset];

        var opaqueBody = backing.IsEmpty
            ? span[..RecordSize].ToArray().AsMemory()
            : backing[..RecordSize];

        return new MapBinRecord
        {
            Mode = mode,
            NameMask = nameMask,
            OpaqueBody = opaqueBody
        };
    }
}