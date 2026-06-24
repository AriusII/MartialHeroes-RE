namespace MartialHeroes.Assets.Parsers.Terrain.Models;

public sealed class WindSeedRecord
{
    public required float Pad0 { get; init; }

    public required float Speed { get; init; }

    public required float Pad2 { get; init; }

    public required float Coord { get; init; }

    public required float Scale { get; init; }

    public required uint TexId { get; init; }
}

public sealed class WindSeedPool
{
    public required uint RecordCount { get; init; }

    public required uint SourceFlag { get; init; }

    public required WindSeedRecord[] Records { get; init; }
}