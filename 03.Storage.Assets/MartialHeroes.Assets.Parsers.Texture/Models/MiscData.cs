namespace MartialHeroes.Assets.Parsers.Texture.Models;

public sealed class MapZoneRecord
{
    public required int ZoneId { get; init; }

    public required string ZoneName { get; init; }

    public required int WorldMinX { get; init; }

    public required int WorldMinZ { get; init; }

    public required int WorldMaxX { get; init; }

    public required int WorldMaxZ { get; init; }

    public required float FogDensity { get; init; }

    public required int FlagsA { get; init; }

    public required int FlagsB { get; init; }

    public required int Unknown0x44 { get; init; }

    public required int Unknown0x48 { get; init; }

    public required int Unknown0x4C { get; init; }

    public required int Unknown0x50 { get; init; }
}