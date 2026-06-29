using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Audio.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct MudSoundTile
{
    public readonly byte Unread0;

    public readonly byte Unread1;

    public readonly byte BgmZoneId;

    public readonly byte BgeAmbientId0;

    public readonly byte BgeAmbientId1;

    public readonly byte EffId0;

    public readonly byte EffId1;

    public readonly byte EffId2;
}

public sealed class MudSoundGrid
{
    public const int Cols = 64;

    public const int Rows = 64;

    public const int TileWorldSize = 16;

    public const int TileStride = 8;

    public const int FixedFileSize = Cols * Rows * TileStride;

    public const int TileCount = Cols * Rows;


    public required MudSoundTile[] Tiles { get; init; }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MudSoundTile GetTile(int localX, int localZ)
    {
        var col = (localX / TileWorldSize) &
                  0x3F;
        var row = (localZ / TileWorldSize) &
                  0x3F;
        var index = col +
                    (row << 6);
        return Tiles[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TileSoundIndices ResolveSoundIndices(int localX, int localZ)
    {
        var tile = GetTile(localX, localZ);
        return new TileSoundIndices
        {
            BgmIndex = tile.BgmZoneId,
            BgeIndices = (tile.BgeAmbientId0, tile.BgeAmbientId1),
            EffIndices = (tile.EffId0, tile.EffId1, tile.EffId2)
        };
    }


    public readonly struct TileSoundIndices
    {
        public byte BgmIndex { get; init; }

        public (byte Slot0, byte Slot1) BgeIndices { get; init; }

        public (byte Slot0, byte Slot1, byte Slot2) EffIndices { get; init; }
    }
}