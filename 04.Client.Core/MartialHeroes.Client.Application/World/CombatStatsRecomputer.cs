using MartialHeroes.Client.Domain.Inventory.Inventory;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Application.World;

public static class CombatStatsRecomputer
{
    public static CombatStats Recompute(
        in PrimaryStatServerBases serverBases,
        ReadOnlySpan<BuffStatGrant> buffGrants,
        ReadOnlySpan<SlottedEquipmentContribution> wornEquipment,
        ReadOnlySpan<SetPieceContribution> setPieces,
        ReadOnlySpan<ModifierSlotContribution> modifierSlots,
        in CombatRatingTerms ratingTerms = default)
    {
        var buffs = buffGrants.Length <= 64
            ? stackalloc BuffContribution[buffGrants.Length]
            : new BuffContribution[buffGrants.Length];
        var buffCount = BuffStatBridge.BuildContributions(buffGrants, buffs);
        buffs = buffs[..buffCount];

        var equipment = wornEquipment.Length <= 64
            ? stackalloc EquipmentContribution[wornEquipment.Length]
            : new EquipmentContribution[wornEquipment.Length];
        var equipCount = EquipRules.RecomputeEquipmentContributions(wornEquipment, equipment);
        equipment = equipment[..equipCount];

        var primary = StatAggregation.AggregatePrimaryStats(
            in serverBases, buffs, equipment, setPieces, modifierSlots);

        var attackInputs = ratingTerms.ToAttackInputs(primary);
        var hitInputs = ratingTerms.ToHitInputs(primary);

        var attackRating = CombatFormula.AttackRating(in attackInputs);
        var hitRating = CombatFormula.HitRating(in hitInputs);

        return CombatStats.Empty with
        {
            Str = primary.Str,
            Dex = primary.Dex,
            Agil = primary.Agi,
            Vital = primary.Con,
            Inte = primary.Int,
            AttackRating = attackRating,
            HitRating = hitRating
        };
    }
}

public readonly record struct CombatRatingTerms
{
    public int Slot15 { get; init; }

    public int Slot94 { get; init; }

    public int Slot5 { get; init; }

    public int Slot16 { get; init; }

    public int Slot20 { get; init; }

    public int Slot83 { get; init; }

    public int Slot61 { get; init; }

    public int WeaponTerm { get; init; }

    public int WeaponGrade { get; init; }

    public int DamageEquipSum { get; init; }

    public int AccuracyEquipSum { get; init; }

    public int LevelTerm { get; init; }

    public int GradeByte { get; init; }

    public int ProficiencyKey { get; init; }

    public bool RankProgressGate { get; init; }

    public AttackRatingInputs ToAttackInputs(in PrimaryStats stats)
    {
        return new AttackRatingInputs
        {
            Stats = stats,
            Slot15 = Slot15,
            Slot94 = Slot94,
            Slot5 = Slot5,
            WeaponTerm = WeaponTerm,
            WeaponGrade = WeaponGrade,
            DamageEquipSum = DamageEquipSum,
            LevelTerm = LevelTerm,
            GradeByte = GradeByte,
            Slot83 = Slot83,
            Slot61 = Slot61
        };
    }

    public HitRatingInputs ToHitInputs(in PrimaryStats stats)
    {
        return new HitRatingInputs
        {
            Stats = stats,
            Slot16 = Slot16,
            Slot20 = Slot20,
            WeaponTerm = WeaponTerm,
            WeaponGrade = WeaponGrade,
            AccuracyEquipSum = AccuracyEquipSum,
            LevelTerm = LevelTerm,
            GradeByte = GradeByte,
            ProficiencyPenaltyPercent = CombatFormula.WeaponProficiencyPenaltyPercent(ProficiencyKey),
            RankProgressGate = RankProgressGate
        };
    }
}