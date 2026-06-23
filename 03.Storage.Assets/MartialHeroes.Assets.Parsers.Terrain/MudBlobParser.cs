using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class MudBlobParser
{
    public static MudBlob Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MudBlob Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length != MudBlob.FixedSize)
            throw new InvalidDataException(
                $".mud parse error: expected exactly {MudBlob.FixedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/mud.md §Fixed size.");

        var totalTiles = MudBlob.GridRows * MudBlob.GridCols;
        var tiles = new MudTileRecord[totalTiles];

        for (var t = 0; t < totalTiles; t++)
        {
            var offset = t * MudBlob.RecordStride;
            tiles[t] = new MudTileRecord(
                span[offset + 0],
                span[offset + 1],
                span[offset + 2],
                span[offset + 3],
                span[offset + 4],
                span[offset + 5],
                span[offset + 6],
                span[offset + 7]
            );
        }

        return new MudBlob { Tiles = tiles };
    }
}