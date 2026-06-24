namespace MartialHeroes.Assets.Parsers.World.Models;

public enum RegionZoneType
{
    Safe = 0,
    OpenPvp = 1,
    Closed = 2
}

public sealed class RegionZoneProperties
{
    public required int RegionId { get; init; }
    public required string ZoneName { get; init; }
    public required float LabelX { get; init; }
    public required float LabelZ { get; init; }
    public required RegionZoneType ZoneType { get; init; }
    public required int TailOpaque { get; init; }
}