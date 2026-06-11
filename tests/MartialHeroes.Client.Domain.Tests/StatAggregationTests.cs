using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Tests for the per-stat three-source aggregation and the all-or-nothing set-bonus rule.
/// spec: Docs/RE/specs/combat.md §2 / §2.4.
/// </summary>
public sealed class StatAggregationTests
{
    private static readonly ReadOnlyMemory<BuffContribution> NoBuffs = ReadOnlyMemory<BuffContribution>.Empty;
    private static readonly ReadOnlyMemory<EquipmentContribution> NoEquip = ReadOnlyMemory<EquipmentContribution>.Empty;
    private static readonly ReadOnlyMemory<SetPieceContribution> NoSets = ReadOnlyMemory<SetPieceContribution>.Empty;

    private static readonly ReadOnlyMemory<ModifierSlotContribution> NoSlots =
        ReadOnlyMemory<ModifierSlotContribution>.Empty;

    // -------------------------------------------------------------------------
    // Single-source contributions.
    // -------------------------------------------------------------------------

    [Fact]
    public void Aggregate_ServerBaseOnly()
    {
        int v = StatAggregation.Aggregate(
            StatKey.Str, serverBase: 25,
            NoBuffs.Span, NoEquip.Span, NoSets.Span, NoSlots.Span);
        Assert.Equal(25, v);
    }

    [Fact]
    public void Aggregate_BuffMatchingKey()
    {
        BuffContribution[] buffs = [new(StatKey.Str, 5), new(StatKey.Dex, 99)];
        int v = StatAggregation.Aggregate(
            StatKey.Str, serverBase: 10,
            buffs, NoEquip.Span, NoSets.Span, NoSlots.Span);
        Assert.Equal(15, v); // 10 + 5 (the Dex buff is ignored for Str)
    }

    [Fact]
    public void Aggregate_AllStatsBuffAddedToEveryPrimary()
    {
        BuffContribution[] buffs = [new(StatKey.AllStats, 7)];
        Assert.Equal(7, StatAggregation.Aggregate(StatKey.Str, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span));
        Assert.Equal(7, StatAggregation.Aggregate(StatKey.Dex, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span));
        Assert.Equal(7, StatAggregation.Aggregate(StatKey.Agi, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span));
        Assert.Equal(7, StatAggregation.Aggregate(StatKey.Int, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span));
        Assert.Equal(7, StatAggregation.Aggregate(StatKey.Con, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span));
    }

    [Fact]
    public void Aggregate_PerStatAndAllStatsBuffsBothCount()
    {
        BuffContribution[] buffs = [new(StatKey.Str, 4), new(StatKey.AllStats, 6)];
        int v = StatAggregation.Aggregate(StatKey.Str, 10, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span);
        Assert.Equal(20, v); // 10 + 4 + 6
    }

    [Fact]
    public void Aggregate_AllStatsBuff_NotDoubleCountedWhenStatIsAllStats()
    {
        // Aggregating the AllStats key itself must not add the all-stats buff twice; the direct match
        // already counts it.
        BuffContribution[] buffs = [new(StatKey.AllStats, 6)];
        int v = StatAggregation.Aggregate(StatKey.AllStats, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span);
        Assert.Equal(6, v);
    }

    [Fact]
    public void Aggregate_AllStatsBuff_NotAppliedToNonPrimary()
    {
        // A non-primary stat (e.g. a hit term key) does not receive the shared all-stats buff.
        BuffContribution[] buffs = [new(StatKey.AllStats, 6)];
        int v = StatAggregation.Aggregate(StatKey.HitTermA, 0, buffs, NoEquip.Span, NoSets.Span, NoSlots.Span);
        Assert.Equal(0, v);
    }

    [Fact]
    public void Aggregate_EquipmentSummedForMatchingKey()
    {
        EquipmentContribution[] equip = [new(StatKey.Str, 3), new(StatKey.Str, 4), new(StatKey.Dex, 100)];
        int v = StatAggregation.Aggregate(StatKey.Str, 0, NoBuffs.Span, equip, NoSets.Span, NoSlots.Span);
        Assert.Equal(7, v);
    }

    [Fact]
    public void Aggregate_ModifierSlotsSummedForMatchingKey()
    {
        ModifierSlotContribution[] slots = [new(StatKey.Con, 2), new(StatKey.Con, 5), new(StatKey.Str, 9)];
        int v = StatAggregation.Aggregate(StatKey.Con, 1, NoBuffs.Span, NoEquip.Span, NoSets.Span, slots);
        Assert.Equal(8, v); // 1 + 2 + 5
    }

    [Fact]
    public void Aggregate_GlobalAddend()
    {
        int v = StatAggregation.Aggregate(
            StatKey.Str, 10, NoBuffs.Span, NoEquip.Span, NoSets.Span, NoSlots.Span, globalAddend: 3);
        Assert.Equal(13, v);
    }

    [Fact]
    public void Aggregate_AllSourcesCombine()
    {
        BuffContribution[] buffs = [new(StatKey.Str, 2), new(StatKey.AllStats, 1)];
        EquipmentContribution[] equip = [new(StatKey.Str, 4)];
        ModifierSlotContribution[] slots = [new(StatKey.Str, 5)];
        SetPieceContribution[] sets =
            [new(SetTypeId: 1, RequiredPieceCount: 1, StatKey.Str, PerPieceBonus: 8, SetCompleteBonus: 16)];

        // serverBase 10 + buff 2 + allStats 1 + equip 4 + slot 5 + set (per-piece 8 + complete 16) + global 3 = 49.
        int v = StatAggregation.Aggregate(StatKey.Str, 10, buffs, equip, sets, slots, globalAddend: 3);
        Assert.Equal(49, v);
    }

    // -------------------------------------------------------------------------
    // §2.4 set bonus — all-or-nothing.
    // -------------------------------------------------------------------------

    [Fact]
    public void SetBonus_Incomplete_GrantsPerPieceOnly()
    {
        // Required 3 pieces, only 2 of the same set-type worn -> per-piece only, no set-complete.
        SetPieceContribution[] sets =
        [
            new(SetTypeId: 7, RequiredPieceCount: 3, StatKey.Str, PerPieceBonus: 10, SetCompleteBonus: 100),
            new(SetTypeId: 7, RequiredPieceCount: 3, StatKey.Str, PerPieceBonus: 10, SetCompleteBonus: 100),
        ];
        int v = StatAggregation.SumSetBonus(StatKey.Str, sets);
        Assert.Equal(20, v); // 2 * per-piece(10); no set-complete bonus
    }

    [Fact]
    public void SetBonus_Complete_GrantsPerPiecePlusFullSet()
    {
        // Required 3, exactly 3 worn -> per-piece for each plus set-complete for each.
        SetPieceContribution[] sets =
        [
            new(SetTypeId: 7, RequiredPieceCount: 3, StatKey.Str, PerPieceBonus: 10, SetCompleteBonus: 100),
            new(SetTypeId: 7, RequiredPieceCount: 3, StatKey.Str, PerPieceBonus: 10, SetCompleteBonus: 100),
            new(SetTypeId: 7, RequiredPieceCount: 3, StatKey.Str, PerPieceBonus: 10, SetCompleteBonus: 100),
        ];
        int v = StatAggregation.SumSetBonus(StatKey.Str, sets);
        Assert.Equal(330, v); // 3 * (per-piece 10 + complete 100)
    }

    [Fact]
    public void SetBonus_OverCount_NotComplete()
    {
        // count != required (count 4 > required 3) -> set NOT complete (exact-match rule).
        SetPieceContribution[] sets =
        [
            new(7, 3, StatKey.Str, 10, 100),
            new(7, 3, StatKey.Str, 10, 100),
            new(7, 3, StatKey.Str, 10, 100),
            new(7, 3, StatKey.Str, 10, 100),
        ];
        int v = StatAggregation.SumSetBonus(StatKey.Str, sets);
        Assert.Equal(40, v); // 4 * per-piece only, no complete bonus
    }

    [Fact]
    public void SetBonus_DifferentSetTypesCountedIndependently()
    {
        // Two pieces of set 7 (required 2 -> complete) and one of set 9 (required 2 -> incomplete).
        SetPieceContribution[] sets =
        [
            new(7, 2, StatKey.Str, 5, 50),
            new(7, 2, StatKey.Str, 5, 50),
            new(9, 2, StatKey.Str, 3, 30),
        ];
        // set 7: 2*(5+50) = 110; set 9: 1*3 = 3. Total 113.
        Assert.Equal(113, StatAggregation.SumSetBonus(StatKey.Str, sets));
    }

    [Fact]
    public void SetBonus_DifferentStatKeyIgnored()
    {
        SetPieceContribution[] sets = [new(7, 1, StatKey.Dex, 10, 100)];
        Assert.Equal(0, StatAggregation.SumSetBonus(StatKey.Str, sets));
        Assert.Equal(110, StatAggregation.SumSetBonus(StatKey.Dex, sets));
    }

    [Fact]
    public void IsSetComplete_RequiredZeroIsNeverComplete()
    {
        SetPieceContribution[] sets = [new(7, 0, StatKey.Str, 1, 2)];
        Assert.False(StatAggregation.IsSetComplete(7, 0, sets));
    }

    [Fact]
    public void IsSetComplete_ExactMatch()
    {
        SetPieceContribution[] sets = [new(7, 2, StatKey.Str, 1, 2), new(7, 2, StatKey.Str, 1, 2)];
        Assert.True(StatAggregation.IsSetComplete(7, 2, sets));
        Assert.False(StatAggregation.IsSetComplete(7, 3, sets));
    }

    // -------------------------------------------------------------------------
    // AggregatePrimaryStats convenience.
    // -------------------------------------------------------------------------

    [Fact]
    public void AggregatePrimaryStats_CombinesAllFiveStats()
    {
        var bases = new PrimaryStatServerBases(Str: 10, Dex: 20, Agi: 30, Con: 40, Int: 50);
        BuffContribution[] buffs = [new(StatKey.AllStats, 5)];
        EquipmentContribution[] equip = [new(StatKey.Str, 1), new(StatKey.Dex, 2)];

        PrimaryStats s = StatAggregation.AggregatePrimaryStats(
            bases, buffs, equip, NoSets.Span, NoSlots.Span);

        Assert.Equal(16, s.Str); // 10 + allStats 5 + equip 1
        Assert.Equal(27, s.Dex); // 20 + allStats 5 + equip 2
        Assert.Equal(35, s.Agi); // 30 + allStats 5
        Assert.Equal(45, s.Con); // 40 + allStats 5
        Assert.Equal(55, s.Int); // 50 + allStats 5
    }

    // -------------------------------------------------------------------------
    // Determinism.
    // -------------------------------------------------------------------------

    [Fact]
    public void Aggregate_IsDeterministic()
    {
        BuffContribution[] buffs = [new(StatKey.Str, 3), new(StatKey.AllStats, 2)];
        EquipmentContribution[] equip = [new(StatKey.Str, 7)];
        ModifierSlotContribution[] slots = [new(StatKey.Str, 4)];
        SetPieceContribution[] sets = [new(1, 2, StatKey.Str, 9, 90), new(1, 2, StatKey.Str, 9, 90)];

        int v0 = StatAggregation.Aggregate(StatKey.Str, 12, buffs, equip, sets, slots);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(v0, StatAggregation.Aggregate(StatKey.Str, 12, buffs, equip, sets, slots));
        }
    }
}