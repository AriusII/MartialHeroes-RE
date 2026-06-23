namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class RegionGrid
{
    public const int CellWorldSize = 256;
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Cells { get; init; }
    public required int OriginX { get; init; }
    public required int OriginZ { get; init; }

    public byte GetRegionId(int worldX, int worldZ)
    {
        var col = (worldX - OriginX) / CellWorldSize;
        var row = (worldZ - OriginZ) / CellWorldSize;

        if ((uint)col >= (uint)Width || (uint)row >= (uint)Height)
            return 0;

        return Cells
            [col + row * Width];
    }
}