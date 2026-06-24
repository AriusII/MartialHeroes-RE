namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class RegionTableRecord
{
    public required int RegionId { get; init; }
    public required string ZoneName { get; init; }
    public required float LabelX { get; init; }
    public required float LabelZ { get; init; }
    public required uint ZoneType { get; init; }
    public required uint TailOpaque { get; init; }
}