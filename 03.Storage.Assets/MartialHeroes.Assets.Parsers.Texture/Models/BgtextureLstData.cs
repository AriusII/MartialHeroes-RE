namespace MartialHeroes.Assets.Parsers.Texture.Models;


public enum BgTextureRenderBucket
{
    Static,

    ScrollAnimated
}

public enum BgTextureKind : byte
{
    Static = 0x01,

    ScrollUv = 0x02,

    Grass = 0x0A,

    Plant = 0x0B,

    TreeBark = 0x0C,

    Foliage = 0x14,

    Unknown = 0xFF
}

public sealed class BgtextureLstRecord
{
    public required int Index { get; init; }

    public required byte KindRaw { get; init; }

    public BgTextureRenderBucket RenderBucket =>
        KindRaw == 0x01
            ? BgTextureRenderBucket.Static
            : BgTextureRenderBucket.ScrollAnimated;

    public BgTextureKind KindEnum => KindRaw switch
    {
        0x01 => BgTextureKind.Static,
        0x02 => BgTextureKind.ScrollUv,
        0x0A => BgTextureKind.Grass,
        0x0B => BgTextureKind.Plant,
        0x0C => BgTextureKind.TreeBark,
        0x14 => BgTextureKind.Foliage,
        _ => BgTextureKind.Unknown
    };

    public required string RelPath { get; init; }
}

public sealed class BgtextureLstCatalog
{
    private readonly BgtextureLstRecord[] _records;

    internal BgtextureLstCatalog(BgtextureLstRecord[] records)
    {
        _records = records;
    }

    public static BgtextureLstCatalog Empty { get; } = new([]);

    public int Count => _records.Length;

    public IReadOnlyList<BgtextureLstRecord> Records => _records;

    public BgtextureLstRecord? GetByPoolSlot(int poolSlot)
    {
        return (uint)poolSlot < (uint)_records.Length ? _records[poolSlot] : null;
    }
}