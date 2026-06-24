namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed class CitemsRecord
{
    public required uint ItemId { get; init; }

    public required string ItemName { get; init; }

    public required byte Unknown36 { get; init; }

    public required byte ItemCategory { get; init; }

    public required uint CashPriceNx { get; init; }

    public required uint SlotSeq2 { get; init; }

    public required uint IconIdA { get; init; }

    public required uint IconIdB { get; init; }

    public required uint Flag4C { get; init; }

    public required string[] DescParagraphs { get; init; }

    public required ReadOnlyMemory<byte> RemainderRaw { get; init; }

    public required byte TailFlag418 { get; init; }

    public string? GetParagraph(int index)
    {
        if ((uint)index >= 10u) return null;
        return index < DescParagraphs.Length ? DescParagraphs[index] : null;
    }
}

public sealed class CitemsCatalog
{
    private readonly Dictionary<uint, CitemsRecord> _byId;

    internal CitemsCatalog(CitemsRecord[] records)
    {
        Records = records;
        _byId = new Dictionary<uint, CitemsRecord>(records.Length);
        foreach (var r in records)
            _byId.TryAdd(r.ItemId, r);
    }

    public IReadOnlyList<CitemsRecord> Records { get; }

    public CitemsRecord? TryGetById(uint itemId)
    {
        return _byId.TryGetValue(itemId, out var r) ? r : null;
    }
}