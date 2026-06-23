namespace MartialHeroes.Assets.Parsers.Texture.Models;

public enum UiTexBlockKind
{
    Dds,

    Msk
}

public sealed record UiTexEntry(int TexId, string VfsPath, UiTexBlockKind BlockKind);

public sealed class UiTexManifest
{
    private readonly Dictionary<int, UiTexEntry> _byId;

    internal UiTexManifest(IReadOnlyList<UiTexEntry> dds, IReadOnlyList<UiTexEntry> msk)
    {
        DdsEntries = dds;
        MskEntries = msk;

        _byId = new Dictionary<int, UiTexEntry>(dds.Count + msk.Count);
        foreach (var e in dds) _byId[e.TexId] = e;
        foreach (var e in msk) _byId[e.TexId] = e;
    }

    public IReadOnlyList<UiTexEntry> DdsEntries { get; }

    public IReadOnlyList<UiTexEntry> MskEntries { get; }

    public int Count => DdsEntries.Count + MskEntries.Count;

    public UiTexEntry? GetById(int texId)
    {
        return _byId.GetValueOrDefault(texId);
    }
}