using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Audio.Models;

namespace MartialHeroes.Assets.Parsers.Audio;

public static class MudSoundGridParser
{
    private const int ExpectedFileSize = MudSoundGrid.FixedFileSize;

    public static MudSoundGrid Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MudSoundGrid Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length != ExpectedFileSize)
            throw new InvalidDataException(
                $".mud parse error: expected exactly {ExpectedFileSize} bytes (0x{ExpectedFileSize:X4}), " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/mud.md §Identification.");

        var rawTiles = MemoryMarshal.Cast<byte, MudSoundTile>(span);

        var tileCount = MudSoundGrid.TileCount;

        var tiles = new MudSoundTile[tileCount];
        rawTiles.CopyTo(tiles);

        return new MudSoundGrid { Tiles = tiles };
    }
}