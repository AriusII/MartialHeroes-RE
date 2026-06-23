namespace MartialHeroes.Assets.Parsers.DataTables.Models;


public sealed class ItemEffectEntry
{
    public required ushort EffectA { get; init; }

    public required short EffectB { get; init; }

    public required ushort EffectC { get; init; }

    public required byte EffectD { get; init; }

}

public sealed class ItemsScrRecord
{
    public required string ItemName { get; init; }

    public required uint ItemUid { get; init; }

    public required string ItemDesc { get; init; }

    public required uint ModelRefKey { get; init; }

    public required uint AnimRefKey { get; init; }

    public string SknVfsPath => $"data/char/skin/g{ModelRefKey}.skn";

    public uint BindPosePoolId => AnimRefKey;

    public required ReadOnlyMemory<byte> Opaque0A4 { get; init; }

    public required byte RecordDiscriminator { get; init; }

    public required ReadOnlyMemory<byte> DispatchFlags { get; init; }

    public required ReadOnlyMemory<byte> Opaque200 { get; init; }

    public required ReadOnlyMemory<byte> Opaque21C { get; init; }

    public required byte EffectCount { get; init; }

    public required IReadOnlyList<ItemEffectEntry> Effects { get; init; }

    public required ReadOnlyMemory<byte> FixedBlockRaw { get; init; }
}