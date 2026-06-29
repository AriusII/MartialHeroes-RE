namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class RegionGridData
{
    public const int CellWorldSize = 256;

    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required ReadOnlyMemory<byte> Cells { get; init; }
    public required int OriginX { get; init; }
    public required int OriginZ { get; init; }

    public byte GetRegionId(int worldX, int worldZ)
    {
        var col = FloorDiv(worldX - OriginX, CellWorldSize);
        var row = FloorDiv(worldZ - OriginZ, CellWorldSize);

        if (col < 0 || row < 0 || (uint)col >= Width || (uint)row >= Height)
            return 0;

        return Cells.Span[col + row * (int)Width];
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        if (value % divisor != 0 && (value ^ divisor) < 0)
            quotient--;
        return quotient;
    }
}