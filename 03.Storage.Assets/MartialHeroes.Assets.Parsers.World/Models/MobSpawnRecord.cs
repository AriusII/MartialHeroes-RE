namespace MartialHeroes.Assets.Parsers.World.Models;

public readonly record struct MobSpawnRecord
{
    public required uint Field0 { get; init; }

    public required ReadOnlyMemory<byte> RawRemaining { get; init; }

    public ushort SequentialId => (ushort)(Field0 & 0xFFFF);

    public ushort ZoneTag => (ushort)(Field0 >> 16);
}