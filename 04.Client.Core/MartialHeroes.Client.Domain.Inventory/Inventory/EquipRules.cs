using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public enum EquipCheckResult
{
    Allowed = 0,

    InvalidIndex = 1,

    StateBlocked = 2,

    RelationGuardBlocked = 3
}

public readonly record struct EquipStateGates
{
    public bool InGame { get; init; }
    public bool NotBusy { get; init; }
    public bool NotDead { get; init; }
    public bool AllPass => InGame && NotBusy && NotDead;
}

public readonly record struct EquipRelationContext
{
    public bool BothActorsExist { get; init; }
    public bool ActorsAreDifferent { get; init; }
    public int SlotActorContextId { get; init; }
    public int OtherActorContextId { get; init; }

    public bool ShareSameNonZeroContext =>
        SlotActorContextId != 0 && SlotActorContextId == OtherActorContextId;
}

public static class EquipRules
{
    public static EquipCheckResult CheckEquip(
        int itemIndex,
        int destinationSlot,
        in EquipStateGates state,
        in EquipRelationContext relation)
    {
        if (itemIndex < 0) return EquipCheckResult.InvalidIndex;

        if (!state.AllPass) return EquipCheckResult.StateBlocked;

        if (destinationSlot == EquipSlots.StatExcludedSlot
            && relation.BothActorsExist
            && relation.ActorsAreDifferent
            && relation.ShareSameNonZeroContext)
            return EquipCheckResult.RelationGuardBlocked;

        return EquipCheckResult.Allowed;
    }

    public static int RecomputeEquipmentContributions(
        ReadOnlySpan<SlottedEquipmentContribution> wornBySlot,
        Span<EquipmentContribution> destination)
    {
        var written = 0;
        for (var i = 0; i < wornBySlot.Length; i++)
        {
            var entry = wornBySlot[i];

            if (EquipSlots.IsExcludedFromStatSum(entry.Slot)) continue;

            if (written >= destination.Length)
                throw new ArgumentException(
                    "Destination span is too small for the equipment contributions.", nameof(destination));

            destination[written++] = entry.Contribution;
        }

        return written;
    }
}

public readonly record struct SlottedEquipmentContribution(int Slot, EquipmentContribution Contribution);