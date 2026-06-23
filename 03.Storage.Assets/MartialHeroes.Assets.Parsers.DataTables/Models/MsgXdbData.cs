namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed record MsgXdbRecord(int CaptionId, string Text);

public sealed class MsgXdbCatalog
{
    private readonly Dictionary<uint, string> _map;

    internal MsgXdbCatalog(MsgXdbRecord[] records)
    {
        Records = records;
        _map = new Dictionary<uint, string>(records.Length);
        foreach (var r in records)
            _map[(uint)r.CaptionId] = r.Text;
    }

    public IReadOnlyList<MsgXdbRecord> Records { get; }

    public int Count => Records.Count;

    public bool TryGet(int captionId, out string? text)
    {
        return _map.TryGetValue((uint)captionId, out text);
    }

    public bool TryGet(uint captionId, out string? text)
    {
        return _map.TryGetValue(captionId, out text);
    }

    public string? GetText(int captionId)
    {
        return _map.TryGetValue((uint)captionId, out var t) ? t : null;
    }

    public string? GetText(uint captionId)
    {
        return _map.TryGetValue(captionId, out var t) ? t : null;
    }
}