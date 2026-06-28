using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Mapping;

public sealed class BgTextureCatalog
{
    public const string TerrainTextureDir = "data/map000/texture/";

    public const string EffectTextureDir = "data/effect/texture/";

    private const string DdsExtension = ".dds";

    private readonly string?[] _relPathBySlot;

    private readonly byte[] _kindBySlot;

    private BgTextureCatalog(string?[] relPathBySlot, byte[] kindBySlot)
    {
        _relPathBySlot = relPathBySlot;
        _kindBySlot = kindBySlot;
    }

    public int SlotCount => _relPathBySlot.Length;

    public static BgTextureCatalog FromLst(ReadOnlyMemory<byte> lstBytes)
    {
        return FromLst(BgtextureLstParser.Parse(lstBytes));
    }

    public static BgTextureCatalog FromLst(BgtextureLstCatalog parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var slots = new string?[parsed.Count];
        var kinds = new byte[parsed.Count];
        foreach (var record in parsed.Records)
        {
            slots[record.Index] = record.RelPath.Length == 0
                ? null
                : record.RelPath;
            kinds[record.Index] = record.KindRaw;
        }

        return new BgTextureCatalog(slots, kinds);
    }

    public string? ResolveRelativePath(int poolSlot)
    {
        return (uint)poolSlot < (uint)_relPathBySlot.Length ? _relPathBySlot[poolSlot] : null;
    }

    public string? ResolveTexturePath(int poolSlot, string textureDir = TerrainTextureDir)
    {
        ArgumentNullException.ThrowIfNull(textureDir);
        var rel = ResolveRelativePath(poolSlot);
        return rel is null ? null : textureDir + rel + DdsExtension;
    }

    public byte ResolveKind(int poolSlot)
    {
        return (uint)poolSlot < (uint)_kindBySlot.Length ? _kindBySlot[poolSlot] : (byte)1;
    }
}