namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class NpcSpawnRecord
{
    public required ushort MobId { get; init; }

    public required ushort Field02Inert { get; init; }

    public required float WorldX { get; init; }

    public required float WorldZ { get; init; }

    public required float Facing { get; init; }

    public float AppliedFacing => MathF.PI / 2f - Facing;

    public required uint SpawnType { get; init; }

    public required uint Field20Inert { get; init; }

    public required uint Field24Inert { get; init; }
}

public sealed class NpcSpawnArray
{
    public required NpcSpawnRecord[] Records { get; init; }
}