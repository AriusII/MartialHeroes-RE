using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public sealed class ActorSizeTable
{
    private readonly Dictionary<uint, ActorSizeRecord> _byActorKindId;

    private ActorSizeTable(ActorSizeRecord[] records)
    {
        Records = records;
        _byActorKindId = new Dictionary<uint, ActorSizeRecord>(records.Length);
        foreach (var r in records)
            _byActorKindId[r.ActorClassId] = r;
    }

    public IReadOnlyList<ActorSizeRecord> Records { get; }

    public static ActorSizeTable Parse(ReadOnlyMemory<byte> data)
    {
        var records = XdbParser.ParseActorSizeXdb(data);
        return new ActorSizeTable(records);
    }

    public ActorSizeRecord? TryGetByActorKindId(uint actorKindId)
    {
        return _byActorKindId.TryGetValue(actorKindId, out var r) ? r : null;
    }
}

public static class ActorSizeParser
{
    public static ActorSizeTable Parse(ReadOnlyMemory<byte> data)
    {
        return ActorSizeTable.Parse(data);
    }
}
