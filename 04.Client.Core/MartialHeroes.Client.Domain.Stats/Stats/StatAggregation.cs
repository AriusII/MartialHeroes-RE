namespace MartialHeroes.Client.Domain.Stats.Stats;

public static class StatAggregation
{
    public static int Aggregate(
        StatKey statKey,
        int serverBase,
        ReadOnlySpan<BuffContribution> buffs,
        ReadOnlySpan<EquipmentContribution> equipment,
        ReadOnlySpan<SetPieceContribution> setPieces,
        ReadOnlySpan<ModifierSlotContribution> modifierSlots,
        int globalAddend = 0)
    {
        var total = serverBase;
        total += SumBuffs(statKey, buffs);
        total += SumEquipment(statKey, equipment);
        total += SumSetBonus(statKey, setPieces);
        total += SumModifierSlots(statKey, modifierSlots);
        total += globalAddend;
        return total;
    }

    public static int SumBuffs(StatKey statKey, ReadOnlySpan<BuffContribution> buffs)
    {
        var sum = 0;
        for (var i = 0; i < buffs.Length; i++)
        {
            var b = buffs[i];

            if (b.Key == statKey)
                sum += b.Value;

            else if (b.Key == StatKey.AllStats && IsPrimaryStat(statKey)) sum += b.Value;
        }

        return sum;
    }

    public static int SumEquipment(StatKey statKey, ReadOnlySpan<EquipmentContribution> equipment)
    {
        var sum = 0;
        for (var i = 0; i < equipment.Length; i++)
            if (equipment[i].Key == statKey)
                sum += equipment[i].Value;

        return sum;
    }

    public static int SumModifierSlots(StatKey statKey, ReadOnlySpan<ModifierSlotContribution> modifierSlots)
    {
        var sum = 0;
        for (var i = 0; i < modifierSlots.Length; i++)
            if (modifierSlots[i].Key == statKey)
                sum += modifierSlots[i].Value;

        return sum;
    }

    public static int SumSetBonus(StatKey statKey, ReadOnlySpan<SetPieceContribution> setPieces)
    {
        var sum = 0;
        for (var i = 0; i < setPieces.Length; i++)
        {
            var piece = setPieces[i];
            if (piece.Key != statKey) continue;

            sum += piece.PerPieceBonus;

            if (IsSetComplete(piece.SetTypeId, piece.RequiredPieceCount, setPieces)) sum += piece.SetCompleteBonus;
        }

        return sum;
    }

    public static bool IsSetComplete(
        int setTypeId,
        int requiredPieceCount,
        ReadOnlySpan<SetPieceContribution> setPieces)
    {
        if (requiredPieceCount <= 0) return false;

        var matched = CountSetPieces(setTypeId, setPieces);
        return matched == requiredPieceCount;
    }

    private static int CountSetPieces(int setTypeId, ReadOnlySpan<SetPieceContribution> setPieces)
    {
        var count = 0;
        for (var i = 0; i < setPieces.Length; i++)
            if (setPieces[i].SetTypeId == setTypeId)
                count++;

        return count;
    }

    private static bool IsPrimaryStat(StatKey statKey)
    {
        return statKey is StatKey.Str or StatKey.Agi or StatKey.Dex or StatKey.Int or StatKey.Con;
    }

    public static PrimaryStats AggregatePrimaryStats(
        in PrimaryStatServerBases serverBases,
        ReadOnlySpan<BuffContribution> buffs,
        ReadOnlySpan<EquipmentContribution> equipment,
        ReadOnlySpan<SetPieceContribution> setPieces,
        ReadOnlySpan<ModifierSlotContribution> modifierSlots)
    {
        var str = Aggregate(StatKey.Str, serverBases.Str, buffs, equipment, setPieces, modifierSlots);
        var dex = Aggregate(StatKey.Dex, serverBases.Dex, buffs, equipment, setPieces, modifierSlots);
        var agi = Aggregate(StatKey.Agi, serverBases.Agi, buffs, equipment, setPieces, modifierSlots);
        var con = Aggregate(StatKey.Con, serverBases.Con, buffs, equipment, setPieces, modifierSlots);
        var @int = Aggregate(StatKey.Int, serverBases.Int, buffs, equipment, setPieces, modifierSlots);
        return new PrimaryStats(str, dex, agi, con, @int);
    }
}

public readonly record struct PrimaryStatServerBases(int Str, int Dex, int Agi, int Con, int Int)
{
    public static readonly PrimaryStatServerBases Zero = new(0, 0, 0, 0, 0);
}