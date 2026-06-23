namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct BuffContribution(StatKey Key, int Value);

public readonly record struct EquipmentContribution(StatKey Key, int Value);

public readonly record struct ModifierSlotContribution(StatKey Key, int Value);

public readonly record struct SetPieceContribution(
    int SetTypeId,
    int RequiredPieceCount,
    StatKey Key,
    int PerPieceBonus,
    int SetCompleteBonus);