namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class MobSpawnRecord
{
    public required ushort MobId { get; init; }

    public required ushort Pad { get; init; }

    public required float WorldX { get; init; }

    public required float WorldZ { get; init; }

    public required float FieldC { get; init; }

    public required float Field10 { get; init; }
}