namespace MartialHeroes.Assets.Parsers.World.Models;

public sealed class MapBinRecord
{
    public const int RecordSize = 0x208;

    public required byte Mode { get; init; }
    public required byte NameMask { get; init; }
    public required ReadOnlyMemory<byte> OpaqueBody { get; init; }
}