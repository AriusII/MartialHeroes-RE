namespace MartialHeroes.Assets.Parsers.DataTables.Models;


public sealed class CitemsRecord
{
    public required uint ItemId { get; init; }

    public required string ItemName { get; init; }


    public required ushort Unknown36 { get; init; }

    public required uint CashPriceNx { get; init; }

    public required uint SlotSeq2 { get; init; }


    public required uint ItemUid { get; init; }

    public required uint Flag4C { get; init; }

    public required string[] DescParagraphs { get; init; }

    public required ReadOnlyMemory<byte> RemainderRaw { get; init; }

    public string? GetParagraph(int index)
    {
        if ((uint)index >= 10u) return null;
        return index < DescParagraphs.Length ? DescParagraphs[index] : null;
    }
}

public sealed class CitemsCatalog
{
    private readonly Dictionary<uint, CitemsRecord> _byUid;

    internal CitemsCatalog(CitemsRecord[] records)
    {
        Records = records;
        _byUid = new Dictionary<uint, CitemsRecord>(records.Length);
        foreach (var r in records)
            _byUid.TryAdd(r.ItemUid, r);
    }

    public IReadOnlyList<CitemsRecord> Records { get; }

    public CitemsRecord? TryGetByUid(uint itemUid)
    {
        return _byUid.TryGetValue(itemUid, out var r) ? r : null;
    }
}