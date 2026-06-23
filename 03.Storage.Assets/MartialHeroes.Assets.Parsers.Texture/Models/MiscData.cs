namespace MartialHeroes.Assets.Parsers.Texture.Models;


public sealed class MobInfoRecord
{
    public required uint EntryId { get; init; }

    public required uint CaptionMsgId { get; init; }

    public required uint DescriptionMsgId { get; init; }

    public required uint SmallParam { get; init; }

    public required uint PackedCodeA { get; init; }

    public required uint PackedCodeB { get; init; }

    public required uint AuxField { get; init; }
}


public sealed class TolMapData
{
    public required uint WorldOriginX { get; init; }

    public required uint WorldOriginY { get; init; }

    public required uint WidthTiles { get; init; }

    public required uint HeightTiles { get; init; }

    public required ReadOnlyMemory<byte> TileGrid { get; init; }
}


public sealed class DescriptorRecord
{
    public required uint DescriptorId { get; init; }

    public required uint Category { get; init; }

    public required string DisplayName { get; init; }

    public required string KeyboardShortcut { get; init; }

    public required ReadOnlyMemory<byte> Reserved { get; init; }
}


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