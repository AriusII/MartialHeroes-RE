namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class RegionGridData
{
    public required uint Width { get; init; }

    public required uint Height { get; init; }

    public required ReadOnlyMemory<byte> Cells { get; init; }

    public required int OriginX { get; init; }

    public required int OriginZ { get; init; }
}