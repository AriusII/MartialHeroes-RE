using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public sealed class EffectScaleTable
{
    private readonly Dictionary<uint, EffectScaleRecord> _byEffectKey;

    private EffectScaleTable(EffectScaleRecord[] records)
    {
        Records = records;
        _byEffectKey = new Dictionary<uint, EffectScaleRecord>(records.Length);
        foreach (var r in records)
            _byEffectKey[r.ObjectId] = r;
    }

    public IReadOnlyList<EffectScaleRecord> Records { get; }

    public static EffectScaleTable Parse(ReadOnlyMemory<byte> data)
    {
        var records = XdbParser.ParseEffectScaleXdb(data);
        return new EffectScaleTable(records);
    }

    public EffectScaleRecord? TryGetByEffectKey(uint effectKey)
    {
        return _byEffectKey.TryGetValue(effectKey, out var r) ? r : null;
    }
}

public static class EffectScaleParser
{
    public static EffectScaleTable Parse(ReadOnlyMemory<byte> data)
    {
        return EffectScaleTable.Parse(data);
    }
}
