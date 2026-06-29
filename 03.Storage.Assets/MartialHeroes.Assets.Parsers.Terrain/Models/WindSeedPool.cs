namespace MartialHeroes.Assets.Parsers.Terrain.Models;

public readonly record struct WindSeedRecord(
    float Pad0,
    float Speed,
    float Pad2,
    float Coord,
    float Scale,
    uint TexId);

public sealed class WindSeedPool
{
    public required uint RecordCount { get; init; }

    public required uint SourceFlag { get; init; }

    public required WindSeedRecord[] Records { get; init; }
}