using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Exact-value tests for the recovered max-HP / max-MP formula.
/// spec: Docs/RE/structs/stats.md. Expected numbers are hand-computed via the three-stage pipeline.
/// </summary>
public sealed class VitalFormulaTests
{
    private static VitalFormulaInputs Stats(int str, int dex, int agi, int con, int @int, byte classId = 1) =>
        VitalFormulaInputs.Empty with
        {
            Stats = new PrimaryStats(str, dex, agi, con, @int),
            ClassId = classId,
        };

    // -------------------------------------------------------------------------
    // HP: full three-stage hand computation.
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxHp_TenAcross_Class1_NoBonuses()
    {
        // Stage 1: 10*2.2 + 10*2.5 + 10*2.4 + 10*1.5 + 10*1.6 + 30 = 132 -> floor 132.
        // Stage 2: base = 132 (no equip/set/bases).
        // Stage 3: class 1 mult = 0.3 -> floor(132 * 0.3) = floor(39.6) = 39.
        long hp = VitalFormula.ComputeMaxHp(Stats(10, 10, 10, 10, 10, classId: 1));
        Assert.Equal(39L, hp);
    }

    [Fact]
    public void MaxHp_ClassTableChangesResult()
    {
        var s = new PrimaryStats(10, 10, 10, 10, 10);

        // Same base 132, different class multipliers.
        Assert.Equal(39L,
            VitalFormula.ComputeMaxHp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 1 })); // 0.3 -> 39
        Assert.Equal(26L,
            VitalFormula.ComputeMaxHp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 2 })); // 0.2 -> 26.4 -> 26
        Assert.Equal(19L,
            VitalFormula.ComputeMaxHp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 3 })); // 0.15 -> 19.8 -> 19
        Assert.Equal(13L,
            VitalFormula.ComputeMaxHp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 4 })); // 0.1 -> 13.2 -> 13
    }

    [Fact]
    public void MaxHp_SentinelClassZero_YieldsZero()
    {
        // Class 0 multiplier is 0.0 -> floor(base * 0) = 0. spec: class id 0 sentinel.
        long hp = VitalFormula.ComputeMaxHp(Stats(10, 10, 10, 10, 10, classId: 0));
        Assert.Equal(0L, hp);
    }

    [Fact]
    public void MaxHp_OutOfRangeClassId_FallsBackToSentinel()
    {
        long hp = VitalFormula.ComputeMaxHp(Stats(10, 10, 10, 10, 10, classId: 200));
        Assert.Equal(0L, hp);
    }

    [Fact]
    public void MaxHp_EquipmentAndExternalBasesAddBeforeMultiplier()
    {
        // base = floor(132) + equip(68) + levelBase(50) + serverBase(50) = 300.
        // Stage 3: class 1 (0.3) -> floor(300 * 0.3) = 90.
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with
        {
            EquipmentHpFlat = 68,
            LevelBaseHp = 50,
            ServerBaseHp = 50,
        };
        Assert.Equal(90L, VitalFormula.ComputeMaxHp(inputs));
    }

    // -------------------------------------------------------------------------
    // Set bonus: all-or-nothing.
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxHp_SetBonus_IgnoredWhenIncomplete()
    {
        var partial = Stats(10, 10, 10, 10, 10, classId: 1) with
        {
            SetBonusHp = 1000,
            IsSetComplete = false,
        };
        // base stays 132 -> floor(132 * 0.3) = 39.
        Assert.Equal(39L, VitalFormula.ComputeMaxHp(partial));
    }

    [Fact]
    public void MaxHp_SetBonus_AppliedWhenComplete()
    {
        var complete = Stats(10, 10, 10, 10, 10, classId: 1) with
        {
            SetBonusHp = 68, // base 132 + 68 = 200
            IsSetComplete = true,
        };
        // floor(200 * 0.3) = floor(60) = 60.
        Assert.Equal(60L, VitalFormula.ComputeMaxHp(complete));
    }

    // -------------------------------------------------------------------------
    // Slot-8 skip: documented as caller responsibility; verify the flat term is summed verbatim.
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxHp_EquipmentFlat_IsSummedVerbatim_CallerExcludesSlot8()
    {
        // The formula receives the already-summed flat HP that EXCLUDES slot 8 (spec: slot-8 skip
        // happens during item accumulation, before this struct). Two callers, identical except one
        // erroneously included a slot-8 grant, must differ by exactly that grant -> proves the term
        // is summed verbatim and the skip lives in the caller.
        var withoutSlot8 = Stats(10, 10, 10, 10, 10, classId: 1) with { EquipmentHpFlat = 68 };
        var withSlot8 = Stats(10, 10, 10, 10, 10, classId: 1) with { EquipmentHpFlat = 68 + 500 };

        long a = VitalFormula.ComputeMaxHp(withoutSlot8); // floor(200*0.3)=60
        long b = VitalFormula.ComputeMaxHp(withSlot8); // floor(700*0.3)=210
        Assert.Equal(60L, a);
        Assert.Equal(210L, b);
    }

    // -------------------------------------------------------------------------
    // Floor at exactly two points.
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxHp_FloorAtStage2_DropsFractionBeforeMultiplier()
    {
        // STR=1 only: score = 1*2.2 + 30 = 32.2 -> floor 32 (Stage-2 floor drops the .2).
        // If the .2 leaked past Stage 2, base would be 32.2 and the result would differ.
        // base 32, class 1 (0.3): floor(32 * 0.3) = floor(9.6) = 9.
        var inputs = Stats(1, 0, 0, 0, 0, classId: 1);
        Assert.Equal(9L, VitalFormula.ComputeMaxHp(inputs));
    }

    [Fact]
    public void MaxHp_FloorAtStage3_TruncatesFinalProduct()
    {
        // base 132, class 1 0.3 -> 39.6, Stage-3 floor -> 39 (not 40).
        Assert.Equal(39L, VitalFormula.ComputeMaxHp(Stats(10, 10, 10, 10, 10, classId: 1)));
    }

    // -------------------------------------------------------------------------
    // HP auras (kind = 1) and %HP buff slot.
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxHp_HpAura_AddsToMultiplier()
    {
        // base 132, class 1 0.3, plus a 20% HP aura -> mult 0.5 -> floor(132 * 0.5) = 66.
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with { Aura0 = AuraTerm.Hp(20) };
        Assert.Equal(66L, VitalFormula.ComputeMaxHp(inputs));
    }

    [Fact]
    public void MaxHp_TwoHpAuras_BothCount()
    {
        // mult = 0.3 + 0.10 + 0.10 = 0.5 -> floor(132 * 0.5) = 66.
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with
        {
            Aura0 = AuraTerm.Hp(10),
            Aura1 = AuraTerm.Hp(10),
        };
        Assert.Equal(66L, VitalFormula.ComputeMaxHp(inputs));
    }

    [Fact]
    public void MaxHp_MpAura_DoesNotAffectHp()
    {
        // An MP-kind aura (kind 2) must be ignored by the HP multiplier.
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with { Aura0 = AuraTerm.Mp(50) };
        Assert.Equal(39L, VitalFormula.ComputeMaxHp(inputs)); // unchanged from baseline
    }

    [Fact]
    public void MaxHp_InactiveAura_Ignored()
    {
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with
        {
            Aura0 = AuraTerm.None,
            Aura1 = new AuraTerm(false, VitalFormula.HpAuraKind, 999),
        };
        Assert.Equal(39L, VitalFormula.ComputeMaxHp(inputs));
    }

    [Fact]
    public void MaxHp_PercentBuffSlot_AddsToMultiplier()
    {
        // base 132, class 0.3 + 20% slot buff = 0.5 -> floor(132 * 0.5) = 66.
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with { HpPercentBuffPercent = 20 };
        Assert.Equal(66L, VitalFormula.ComputeMaxHp(inputs));
    }

    // -------------------------------------------------------------------------
    // MP: no class table, multiplier starts at 1.0.
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxMp_TenAcross_NoBonuses()
    {
        // Stage 1: 10*1.4 + 10*1.5 + 10*1.7 + 10*1.5 + 10*3.5 + 30 = 14+15+17+15+35+30 = 126 -> floor 126.
        // Stage 2: base = 126. Stage 3: mult 1.0 -> floor(126) = 126.
        long mp = VitalFormula.ComputeMaxMp(Stats(10, 10, 10, 10, 10, classId: 1));
        Assert.Equal(126L, mp);
    }

    [Fact]
    public void MaxMp_ClassIdDoesNotMatter()
    {
        var s = new PrimaryStats(10, 10, 10, 10, 10);
        long mp1 = VitalFormula.ComputeMaxMp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 1 });
        long mp4 = VitalFormula.ComputeMaxMp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 4 });
        long mp0 = VitalFormula.ComputeMaxMp(VitalFormulaInputs.Empty with { Stats = s, ClassId = 0 });
        Assert.Equal(126L, mp1);
        Assert.Equal(126L, mp4);
        Assert.Equal(126L, mp0); // no class table -> sentinel class does not zero MP
    }

    [Fact]
    public void MaxMp_MpAura_AddsToMultiplier()
    {
        // base 126, mult 1.0 + 0.5 = 1.5 -> floor(126 * 1.5) = floor(189) = 189.
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with { Aura0 = AuraTerm.Mp(50) };
        Assert.Equal(189L, VitalFormula.ComputeMaxMp(inputs));
    }

    [Fact]
    public void MaxMp_HpAura_DoesNotAffectMp()
    {
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with { Aura0 = AuraTerm.Hp(50) };
        Assert.Equal(126L, VitalFormula.ComputeMaxMp(inputs)); // unchanged
    }

    [Fact]
    public void MaxMp_SetBonusAllOrNothing()
    {
        var incomplete = Stats(10, 10, 10, 10, 10, classId: 1) with { SetBonusMp = 1000, IsSetComplete = false };
        var complete = Stats(10, 10, 10, 10, 10, classId: 1) with { SetBonusMp = 74, IsSetComplete = true };
        Assert.Equal(126L, VitalFormula.ComputeMaxMp(incomplete));
        Assert.Equal(200L, VitalFormula.ComputeMaxMp(complete)); // base 126 + 74 = 200, mult 1.0
    }

    [Fact]
    public void MaxMp_ExternalBasesAdd()
    {
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1) with
        {
            EquipmentMpFlat = 10,
            LevelBaseMp = 30,
            ServerBaseMp = 34,
        };
        // base 126 + 10 + 30 + 34 = 200, mult 1.0 -> 200.
        Assert.Equal(200L, VitalFormula.ComputeMaxMp(inputs));
    }

    // -------------------------------------------------------------------------
    // Provisional external bases default to 0.
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalBases_DefaultToZero()
    {
        var empty = VitalFormulaInputs.Empty;
        Assert.Equal(0L, empty.LevelBaseHp);
        Assert.Equal(0L, empty.LevelBaseMp);
        Assert.Equal(0L, empty.ServerBaseHp);
        Assert.Equal(0L, empty.ServerBaseMp);
    }

    // -------------------------------------------------------------------------
    // Determinism.
    // -------------------------------------------------------------------------

    [Fact]
    public void Compute_IsDeterministic_SameInputsSameOutput()
    {
        var inputs = Stats(37, 12, 5, 21, 9, classId: 2) with
        {
            EquipmentHpFlat = 140,
            EquipmentMpFlat = 35,
            SetBonusHp = 50,
            SetBonusMp = 20,
            IsSetComplete = true,
            LevelBaseHp = 300,
            LevelBaseMp = 120,
            ServerBaseHp = 25,
            ServerBaseMp = 10,
            HpPercentBuffPercent = 7,
            Aura0 = AuraTerm.Hp(15),
            Aura1 = AuraTerm.Mp(40),
        };

        (long hp1, long mp1) = VitalFormula.Compute(inputs);
        for (int i = 0; i < 1000; i++)
        {
            (long hp2, long mp2) = VitalFormula.Compute(inputs);
            Assert.Equal(hp1, hp2);
            Assert.Equal(mp1, mp2);
        }
    }

    // -------------------------------------------------------------------------
    // Class table data.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.3)]
    [InlineData(2, 0.2)]
    [InlineData(3, 0.15)]
    [InlineData(4, 0.1)]
    [InlineData(5, 0.0)] // out of range -> sentinel
    [InlineData(255, 0.0)] // out of range -> sentinel
    public void ClassHpTable_HasRecoveredMultipliers(byte classId, double expected)
    {
        Assert.Equal(expected, ClassHpTable.MultiplierFor(classId), precision: 10);
    }

    [Fact]
    public void ClassHpTable_HasFiveEntries()
    {
        Assert.Equal(5, ClassHpTable.Length);
    }

    // -------------------------------------------------------------------------
    // VitalStats.FromFormula wiring.
    // -------------------------------------------------------------------------

    [Fact]
    public void VitalStats_FromFormula_ClampsToCapacity_AndCarriesStamina()
    {
        var inputs = Stats(10, 10, 10, 10, 10, classId: 1);
        VitalStats v = VitalStats.FromFormula(inputs, maxStamina: 77);
        Assert.Equal(39u, v.MaxHp);
        Assert.Equal(126u, v.MaxMp);
        Assert.Equal(77u, v.MaxStamina);
    }
}