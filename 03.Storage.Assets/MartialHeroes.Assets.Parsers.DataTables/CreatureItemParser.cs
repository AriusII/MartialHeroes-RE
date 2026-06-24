using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public sealed class CreatureItemTable
{
    private readonly Dictionary<uint, CreatureItemXdbRecord> _byCreatureKey;

    private CreatureItemTable(CreatureItemXdbRecord[] records)
    {
        Records = records;
        _byCreatureKey = new Dictionary<uint, CreatureItemXdbRecord>(records.Length);
        foreach (var r in records)
            _byCreatureKey[r.CreatureKey] = r;
    }

    public IReadOnlyList<CreatureItemXdbRecord> Records { get; }

    public static CreatureItemTable Parse(ReadOnlyMemory<byte> data)
    {
        var records = XdbParser.ParseCreatureItemXdb(data);
        return new CreatureItemTable(records);
    }

    public CreatureItemXdbRecord? TryGetByCreatureKey(uint creatureKey)
    {
        return _byCreatureKey.TryGetValue(creatureKey, out var r) ? r : null;
    }
}

public static class CreatureItemParser
{
    public static CreatureItemTable Parse(ReadOnlyMemory<byte> data)
    {
        return CreatureItemTable.Parse(data);
    }
}
