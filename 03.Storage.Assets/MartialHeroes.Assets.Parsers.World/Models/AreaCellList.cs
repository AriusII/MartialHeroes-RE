namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class AreaCellList
{
    public required uint CellCount { get; init; }
    public required ReadOnlyMemory<uint> CellKeys { get; init; }
}
