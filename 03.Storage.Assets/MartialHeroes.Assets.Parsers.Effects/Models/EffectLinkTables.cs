namespace MartialHeroes.Assets.Parsers.Effects.Models;

public sealed class ItemJointEffRecord
{
    public required int GroupKey { get; init; }

    public required int EffectId { get; init; }

    public required int Field2 { get; init; }

    public required int Field3 { get; init; }

    public required float Field4 { get; init; }

    public required byte Field5 { get; init; }
}

public sealed class ItemJointEffTable
{
    private readonly ItemJointEffRecord[] _records;

    public ItemJointEffTable(ItemJointEffRecord[] records)
    {
        _records = records;
    }

    public int Count => _records.Length;

    public IReadOnlyList<ItemJointEffRecord> Records => _records;
}

public sealed class MobJointEffRecord
{
    public required int ClassToken { get; init; }

    public required int OffsetToken { get; init; }

    public required int Field2 { get; init; }

    public required int Field3 { get; init; }

    public required float Field4 { get; init; }

    public required byte Field5 { get; init; }
}

public sealed class MobJointEffTable
{
    private readonly MobJointEffRecord[] _records;

    public MobJointEffTable(MobJointEffRecord[] records)
    {
        _records = records;
    }

    public int Count => _records.Length;

    public IReadOnlyList<MobJointEffRecord> Records => _records;
}

public sealed class TotalMugongRecord
{
    public required int Field1 { get; init; }

    public required int Field2 { get; init; }

    public required int Field3 { get; init; }

    public required int Field4 { get; init; }
}

public sealed class TotalMugongTable
{
    private readonly TotalMugongRecord[] _records;

    public TotalMugongTable(TotalMugongRecord[] records)
    {
        _records = records;
    }

    public int Count => _records.Length;

    public IReadOnlyList<TotalMugongRecord> Records => _records;
}

public sealed class SwordLightRecord
{
    public required int Id { get; init; }

    public required string TextureName { get; init; }

    public required string[] Tokens { get; init; }
}

public sealed class SwordLightTable
{
    private readonly SwordLightRecord[] _records;

    public SwordLightTable(SwordLightRecord[] records)
    {
        _records = records;
    }

    public int Count => _records.Length;

    public IReadOnlyList<SwordLightRecord> Records => _records;
}