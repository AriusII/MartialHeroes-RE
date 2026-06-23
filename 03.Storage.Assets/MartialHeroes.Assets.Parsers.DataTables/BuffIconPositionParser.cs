using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public sealed class BuffIconPositionTable
{
    private readonly Dictionary<uint, BuffIconPositionRecord> _byBuffId;

    private BuffIconPositionTable(BuffIconPositionRecord[] records)
    {
        Records = records;
        _byBuffId = new Dictionary<uint, BuffIconPositionRecord>(records.Length);
        foreach (var r in records)
            _byBuffId[r.BuffId] = r;
    }

    public IReadOnlyList<BuffIconPositionRecord> Records { get; }

    public static BuffIconPositionTable Parse(ReadOnlyMemory<byte> data)
    {
        var records = XdbParser.ParseBuffIconPositionXdb(data);
        return new BuffIconPositionTable(records);
    }

    public BuffIconPositionRecord? TryGetById(uint buffId)
    {
        return _byBuffId.TryGetValue(buffId, out var r) ? r : null;
    }
}

public static class BuffIconPositionParser
{
    public static BuffIconPositionTable Parse(ReadOnlyMemory<byte> data)
    {
        return BuffIconPositionTable.Parse(data);
    }
}