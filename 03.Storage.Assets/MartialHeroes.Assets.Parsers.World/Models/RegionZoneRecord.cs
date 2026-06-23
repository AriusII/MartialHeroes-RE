namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class RegionZoneRecord
{
    public required int RegionId { get; init; }

    public required uint ZoneTypeRaw { get; init; }

    public required ReadOnlyMemory<byte> OpaqueLeading { get; init; }

    public required ReadOnlyMemory<byte> OpaqueTrailing { get; init; }
}