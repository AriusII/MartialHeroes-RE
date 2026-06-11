using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Exact-value tests for the recovered derived combat-stat formulas (attack/secondary bases,
/// attack-rating, hit-rating, weapon-proficiency penalty). spec: Docs/RE/specs/combat.md §3/§4.
/// Expected numbers are hand-computed with the bit-exact float weights widened to double.
/// </summary>
public sealed class CombatFormulaTests
{
    private static PrimaryStats S(int str, int dex, int agi, int con, int @int) =>
        new(str, dex, agi, con, @int);

    // -------------------------------------------------------------------------
    // §3.1 Attack base — exact values. (STR*2.5 + DEX*2.0 + AGI*2.3 + CON*1.0 + INT*1.0) * 0.2
    // -------------------------------------------------------------------------

    [Fact]
    public void AttackBase_TenAcross()
    {
        // (25 + 20 + 23 + 10 + 10) * 0.2 = 88 * 0.2 = 17.6.
        double v = CombatFormula.AttackBase(S(10, 10, 10, 10, 10));
        Assert.Equal(17.6, v, precision: 5);
    }

    [Fact]
    public void AttackBase_PureStr()
    {
        // 100 * 2.5 * 0.2 = 50.0.
        Assert.Equal(50.0, CombatFormula.AttackBase(S(100, 0, 0, 0, 0)), precision: 5);
    }

    [Fact]
    public void AttackBase_Zero()
    {
        Assert.Equal(0.0, CombatFormula.AttackBase(PrimaryStats.Zero), precision: 10);
    }

    [Fact]
    public void AttackBase_Mixed()
    {
        // (50*2.5 + 30*2.0 + 20*2.3 + 10*1.0 + 5*1.0) * 0.2 = (125+60+46+10+5)*0.2 = 246*0.2 = 49.2.
        Assert.Equal(49.2, CombatFormula.AttackBase(S(50, 30, 20, 10, 5)), precision: 4);
    }

    [Fact]
    public void AttackBase_WeightsAreDexAndStrLed()
    {
        // STR weight (2.5) > AGI (2.3) > DEX (2.0); 1 point of STR yields more base than 1 of DEX.
        double str = CombatFormula.AttackBase(S(1, 0, 0, 0, 0));
        double dex = CombatFormula.AttackBase(S(0, 1, 0, 0, 0));
        double agi = CombatFormula.AttackBase(S(0, 0, 1, 0, 0));
        Assert.True(str > agi && agi > dex);
    }

    // -------------------------------------------------------------------------
    // §3.2 Secondary base. (STR*1.4 + DEX*2.65 + AGI*1.5 + CON*2.1 + INT*1.1) * 0.2
    // -------------------------------------------------------------------------

    [Fact]
    public void SecondaryBase_TenAcross()
    {
        // (14 + 26.5 + 15 + 21 + 11) * 0.2 = 87.5 * 0.2 = 17.5.
        Assert.Equal(17.5, CombatFormula.SecondaryBase(S(10, 10, 10, 10, 10)), precision: 5);
    }

    [Fact]
    public void SecondaryBase_PureDex()
    {
        // 100 * 2.65 * 0.2 = 53.0.
        Assert.Equal(53.0, CombatFormula.SecondaryBase(S(0, 100, 0, 0, 0)), precision: 4);
    }

    [Fact]
    public void SecondaryBase_IsDexDominant()
    {
        // §3.2 note: DEX-dominant distribution distinct from §3.1.
        double dex = CombatFormula.SecondaryBase(S(0, 1, 0, 0, 0));
        double str = CombatFormula.SecondaryBase(S(1, 0, 0, 0, 0));
        double agi = CombatFormula.SecondaryBase(S(0, 0, 1, 0, 0));
        Assert.True(dex > str && dex > agi);
    }

    [Fact]
    public void Bases_DifferFromEachOther()
    {
        // Same stats, the two bases use different distributions -> different results.
        var s = S(40, 10, 5, 30, 2);
        Assert.NotEqual(CombatFormula.AttackBase(s), CombatFormula.SecondaryBase(s));
    }

    // -------------------------------------------------------------------------
    // §4 Weapon-proficiency penalty bands.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)] // below the banded range
    [InlineData(4, 25)] // 4..10 -> 25
    [InlineData(7, 25)]
    [InlineData(10, 25)]
    [InlineData(11, 50)] // 11..30 -> 50
    [InlineData(30, 50)]
    [InlineData(31, 0)] // 31..74 -> unpenalised path (0) by default
    [InlineData(74, 0)]
    [InlineData(75, 100)] // 75+ -> 100
    [InlineData(200, 100)]
    public void WeaponProficiencyPenalty_Bands(int key, int expected)
    {
        Assert.Equal(expected, CombatFormula.WeaponProficiencyPenaltyPercent(key));
    }

    [Theory]
    [InlineData(31, 75, 75)] // UNVERIFIED 31..74 alternate exit -> caller picks 75
    [InlineData(74, 75, 75)]
    [InlineData(10, 75, 25)] // override only affects the mid band; 4..10 still 25
    [InlineData(75, 75, 100)] // 75+ unaffected by the override
    public void WeaponProficiencyPenalty_MidBandOverride(int key, int midBand, int expected)
    {
        Assert.Equal(expected, CombatFormula.WeaponProficiencyPenaltyPercent(key, midBand));
    }

    [Fact]
    public void ApplyHitPenalty_HundredPercent_ZeroesTheValue()
    {
        // A 100% penalty (unproficient weapon) contributes no hit bonus. spec: combat.md §4.
        Assert.Equal(0.0, CombatFormula.ApplyHitPenalty(500.0, 100), precision: 6);
    }

    [Fact]
    public void ApplyHitPenalty_QuarterPenalty()
    {
        Assert.Equal(300.0, CombatFormula.ApplyHitPenalty(400.0, 25), precision: 6);
    }

    [Fact]
    public void ApplyHitPenalty_NoPenalty_Identity()
    {
        Assert.Equal(400.0, CombatFormula.ApplyHitPenalty(400.0, 0), precision: 6);
    }

    // -------------------------------------------------------------------------
    // §2.2/§3.3 slot-83 hit % multiplier (value-100)%.
    // -------------------------------------------------------------------------

    [Fact]
    public void Slot83Multiplier_AbsentIsNoOp()
    {
        Assert.Equal(123.0, CombatFormula.ApplyHitPercentMultiplier(123.0, 0), precision: 6);
    }

    [Fact]
    public void Slot83Multiplier_OneFifty_Is150Percent()
    {
        // (150 - 100)% boost -> *1.5.
        Assert.Equal(150.0, CombatFormula.ApplyHitPercentMultiplier(100.0, 150), precision: 6);
    }

    [Fact]
    public void Slot83Multiplier_Fifty_IsHalf()
    {
        // (50 - 100)% -> -50% -> *0.5.
        Assert.Equal(50.0, CombatFormula.ApplyHitPercentMultiplier(100.0, 50), precision: 6);
    }

    // -------------------------------------------------------------------------
    // §3.3 Attack rating — full composition hand-check.
    // -------------------------------------------------------------------------

    [Fact]
    public void AttackRating_FullComposition()
    {
        // slot15=5 + slot94=3 + slot5=2 + weaponTerm=20 + attackBase(10across=17.6)
        //   + grade(1*0.1=0.1) + equip=40 + level(30*0.5=15) + (+2 grade>=8) + slot61=7
        // slot83 absent -> no multiplier.
        // = 5+3+2+20+17.6+0.1+40+15+2 + 7 = 111.7 -> floor 111.
        var inputs = new AttackRatingInputs
        {
            Stats = S(10, 10, 10, 10, 10),
            Slot15 = 5,
            Slot94 = 3,
            Slot5 = 2,
            WeaponTerm = 20,
            WeaponGrade = 1,
            DamageEquipSum = 40,
            LevelTerm = 30,
            GradeByte = 8,
            Slot61 = 7,
        };
        Assert.Equal(111, CombatFormula.AttackRating(inputs));
    }

    [Fact]
    public void AttackRating_Slot94OnlyAddedWhenNonZero()
    {
        var withZero = new AttackRatingInputs { Stats = PrimaryStats.Zero, Slot94 = 0, GradeByte = 0 };
        var withVal = withZero with { Slot94 = 9 };
        Assert.Equal(0, CombatFormula.AttackRating(withZero));
        Assert.Equal(9, CombatFormula.AttackRating(withVal));
    }

    [Fact]
    public void AttackRating_GradeBonusOnlyAtThreshold()
    {
        var below = new AttackRatingInputs { Stats = PrimaryStats.Zero, Slot15 = 100, GradeByte = 7 };
        var atThreshold = below with { GradeByte = 8 };
        Assert.Equal(100, CombatFormula.AttackRating(below)); // no +2
        Assert.Equal(102, CombatFormula.AttackRating(atThreshold)); // +2
    }

    [Fact]
    public void AttackRating_Slot83MultipliesRunningTotal()
    {
        // running total before slot83 = slot15(100) = 100; slot83=150 -> *1.5 = 150; + slot61(0).
        var inputs = new AttackRatingInputs { Stats = PrimaryStats.Zero, Slot15 = 100, Slot83 = 150 };
        Assert.Equal(150, CombatFormula.AttackRating(inputs));
    }

    [Fact]
    public void AttackRating_Slot61AddedAfterMultiplier()
    {
        // total 100, slot83=150 -> 150, then slot61=10 -> 160 (flat add happens AFTER the multiplier).
        var inputs = new AttackRatingInputs
        {
            Stats = PrimaryStats.Zero,
            Slot15 = 100,
            Slot83 = 150,
            Slot61 = 10,
        };
        Assert.Equal(160, CombatFormula.AttackRating(inputs));
    }

    // -------------------------------------------------------------------------
    // §3.4 Hit rating — full composition hand-check.
    // -------------------------------------------------------------------------

    [Fact]
    public void HitRating_FullComposition()
    {
        // slot16=4 + slot20=6 + weaponTerm=10 + secBase(10across=17.5) + grade(0.1)
        //   + accEquip=12 + level(20*0.5=10) + 300 + 300(rank) + 2(grade>=8) = 961.6
        // * (1 - 25/100) = 961.6 * 0.75 = 721.2; + 300 = 1021.2 ... wait recompute with rank.
        // Actually: 4+6+10+17.5+0.1+12+10+300+300+2 = 661.6
        //  -> *0.75 = 496.2 -> +300 = 796.2 -> floor 796.
        var inputs = new HitRatingInputs
        {
            Stats = S(10, 10, 10, 10, 10),
            Slot16 = 4,
            Slot20 = 6,
            WeaponTerm = 10,
            WeaponGrade = 1,
            AccuracyEquipSum = 12,
            LevelTerm = 20,
            RankProgressGate = true,
            GradeByte = 8,
            ProficiencyPenaltyPercent = 25,
        };
        Assert.Equal(796, CombatFormula.HitRating(inputs));
    }

    [Fact]
    public void HitRating_BaselinesPresentWithZeroInputs()
    {
        // With everything zero and no rank gate / penalty: only the two flat +300 baselines remain.
        var inputs = new HitRatingInputs { Stats = PrimaryStats.Zero, GradeByte = 0 };
        Assert.Equal(600, CombatFormula.HitRating(inputs)); // 300 + 300
    }

    [Fact]
    public void HitRating_RankGateAddsThirdBaseline()
    {
        var without = new HitRatingInputs { Stats = PrimaryStats.Zero, RankProgressGate = false };
        var with = without with { RankProgressGate = true };
        Assert.Equal(600, CombatFormula.HitRating(without)); // 300 + 300
        Assert.Equal(900, CombatFormula.HitRating(with)); // 300 + 300(rank) + 300
    }

    [Fact]
    public void HitRating_PenaltyAppliesBeforeSecondBaseline()
    {
        // pre-penalty total = 300 (first baseline only, no rank gate); * (1 - 100/100) = 0;
        // then + 300 second baseline = 300. Proves the penalty does NOT scale the second +300.
        var inputs = new HitRatingInputs
        {
            Stats = PrimaryStats.Zero,
            ProficiencyPenaltyPercent = 100,
        };
        Assert.Equal(300, CombatFormula.HitRating(inputs));
    }

    [Fact]
    public void HitRating_FromProficiencyKey_WiresThroughPenalty()
    {
        // Resolve the penalty from a key and feed the getter: key 75 -> 100% -> first baseline zeroed.
        int pen = CombatFormula.WeaponProficiencyPenaltyPercent(75);
        var inputs = new HitRatingInputs { Stats = PrimaryStats.Zero, ProficiencyPenaltyPercent = pen };
        Assert.Equal(300, CombatFormula.HitRating(inputs));
    }

    // -------------------------------------------------------------------------
    // Determinism.
    // -------------------------------------------------------------------------

    [Fact]
    public void Ratings_AreDeterministic()
    {
        var atk = new AttackRatingInputs
        {
            Stats = S(37, 12, 5, 21, 9),
            Slot15 = 11, Slot94 = 4, Slot5 = 2, WeaponTerm = 33,
            WeaponGrade = 2, DamageEquipSum = 88, LevelTerm = 47, GradeByte = 9,
            Slot83 = 120, Slot61 = 6,
        };
        var hit = new HitRatingInputs
        {
            Stats = S(37, 12, 5, 21, 9),
            Slot16 = 8, Slot20 = 3, WeaponTerm = 14, WeaponGrade = 2,
            AccuracyEquipSum = 21, LevelTerm = 47, RankProgressGate = true,
            GradeByte = 9, ProficiencyPenaltyPercent = 50,
        };

        int a0 = CombatFormula.AttackRating(atk);
        int h0 = CombatFormula.HitRating(hit);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a0, CombatFormula.AttackRating(atk));
            Assert.Equal(h0, CombatFormula.HitRating(hit));
        }
    }

    [Fact]
    public void NegativeRunningTotal_FloorsToZero()
    {
        // A 100% penalty on the only contribution drives the running total to 0 before the baselines;
        // use attack rating which has no baselines to show the negative-floor guard.
        var inputs = new AttackRatingInputs
        {
            Stats = PrimaryStats.Zero,
            Slot15 = -50, // a malformed negative running total
        };
        Assert.Equal(0, CombatFormula.AttackRating(inputs));
    }
}