namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed record SkinTxtEntry(
    int Category,
    int HundredsGroup,
    int MillionsGroup,
    int LowRemainder,
    int MeshGid,
    int TextureId);

public sealed class SkinTxtCatalog
{
    private const int BodySlot = 3;

    private const int BaseCategory = 0;
    private const int NoRemainder = 0;

    private readonly Dictionary<(int Category, int Slot, int ClassKey, int Remainder), SkinTxtEntry> _byBodyKey;

    private readonly Dictionary<int, SkinTxtEntry> _byMeshGid;

    internal SkinTxtCatalog(IReadOnlyList<SkinTxtEntry> entries)
    {
        Entries = entries;
        _byMeshGid = new Dictionary<int, SkinTxtEntry>(entries.Count);
        _byBodyKey = new Dictionary<(int, int, int, int), SkinTxtEntry>(entries.Count);
        foreach (var entry in entries)
        {
            _byMeshGid.TryAdd(entry.MeshGid, entry);
            _byBodyKey.TryAdd((entry.Category, entry.MillionsGroup, entry.HundredsGroup, entry.LowRemainder), entry);
        }
    }

    public IReadOnlyList<SkinTxtEntry> Entries { get; }

    public int Count => Entries.Count;

    public SkinTxtEntry? GetByMeshGid(int meshGid)
    {
        return _byMeshGid.TryGetValue(meshGid, out var entry) ? entry : null;
    }

    public int? GetBodyMeshGid(int modelClassId)
    {
        return _byBodyKey.TryGetValue((BaseCategory, BodySlot, modelClassId, NoRemainder), out var entry)
               && entry.MeshGid > 0
            ? entry.MeshGid
            : null;
    }
}