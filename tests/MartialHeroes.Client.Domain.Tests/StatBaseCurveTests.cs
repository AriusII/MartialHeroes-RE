using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Tests for the wave-7 unblock: an injectable per-level base curve replacing the hard-coded 0.
/// spec: Docs/RE/formats/config_tables.md §2.4 (userlevel.scr). The curve VALUES used here are
/// arbitrary test fixtures — no curve data is taken from the spec (the byte layout is UNVERIFIED).
/// </summary>
public sealed class StatBaseCurveTests
{
    // -------------------------------------------------------------------------
    // StatBaseCurve lookup semantics.
    // -------------------------------------------------------------------------

    [Fact]
    public void EmptyCurve_ReturnsZeroForAllLevels()
    {
        var curve = StatBaseCurve.Empty;
        Assert.True(curve.IsEmpty);
        Assert.Equal(0, curve.Count);
        Assert.Equal(0L, curve.BaseForLevel(0));
        Assert.Equal(0L, curve.BaseForLevel(1));
        Assert.Equal(0L, curve.BaseForLevel(999));
    }

    [Fact]
    public void DefaultCurve_IsEmpty()
    {
        StatBaseCurve curve = default;
        Assert.True(curve.IsEmpty);
        Assert.Equal(0L, curve.BaseForLevel(5));
    }

    [Fact]
    public void NullOrEmptyList_TreatedAsEmpty()
    {
        Assert.True(new StatBaseCurve(null).IsEmpty);
        Assert.True(new StatBaseCurve(new List<long>()).IsEmpty);
    }

    [Fact]
    public void Curve_LooksUpOneBasedLevel()
    {
        // Index 0 = level 1, index 1 = level 2, ...
        var curve = new StatBaseCurve(new long[] { 100, 200, 350 });
        Assert.Equal(3, curve.Count);
        Assert.False(curve.IsEmpty);
        Assert.Equal(100L, curve.BaseForLevel(1));
        Assert.Equal(200L, curve.BaseForLevel(2));
        Assert.Equal(350L, curve.BaseForLevel(3));
    }

    [Fact]
    public void Curve_ClampsBelowOneToFirstEntry()
    {
        var curve = new StatBaseCurve(new long[] { 100, 200, 350 });
        Assert.Equal(100L, curve.BaseForLevel(0));
        Assert.Equal(100L, curve.BaseForLevel(-5));
    }

    [Fact]
    public void Curve_ClampsAboveMaxToLastEntry()
    {
        var curve = new StatBaseCurve(new long[] { 100, 200, 350 });
        Assert.Equal(350L, curve.BaseForLevel(4));
        Assert.Equal(350L, curve.BaseForLevel(1000));
    }

    // -------------------------------------------------------------------------
    // Injection into the vital formula — the unblock proof.
    // -------------------------------------------------------------------------

    private static VitalFormulaInputs TenAcross(byte classId = 1) =>
        VitalFormulaInputs.Empty with
        {
            Stats = new PrimaryStats(10, 10, 10, 10, 10),
            ClassId = classId,
        };

    [Fact]
    public void NoCurve_PreservesPriorBehaviour_BaseZero()
    {
        // Baseline (unchanged from existing tests): base 132, class 1 0.3 -> 39.
        long hp = VitalFormula.ComputeMaxHp(TenAcross());
        Assert.Equal(39L, hp);
    }

    [Fact]
    public void InjectedHpCurve_ChangesBase()
    {
        // Inject an HP base curve where level 5 contributes 168.
        // base = floor(132) + curve(5)=168 = 300, class 1 (0.3) -> floor(90) = 90.
        var curve = new StatBaseCurve(new long[] { 0, 10, 20, 50, 168 }); // level 5 -> 168
        var inputs = TenAcross() with
        {
            Level = 5,
            LevelBaseHpCurve = curve,
        };

        Assert.Equal(90L, VitalFormula.ComputeMaxHp(inputs));

        // Sanity: without the curve the same inputs give the baseline 39.
        Assert.Equal(39L, VitalFormula.ComputeMaxHp(inputs with { LevelBaseHpCurve = StatBaseCurve.Empty, Level = 0 }));
    }

    [Fact]
    public void InjectedMpCurve_ChangesBase()
    {
        // base MP 126 + curve(2)=74 = 200, mult 1.0 -> 200.
        var curve = new StatBaseCurve(new long[] { 30, 74 }); // level 2 -> 74
        var inputs = TenAcross() with
        {
            Level = 2,
            LevelBaseMpCurve = curve,
        };
        Assert.Equal(200L, VitalFormula.ComputeMaxMp(inputs));
    }

    [Fact]
    public void Curve_AndFlatOverride_BothSum()
    {
        // Curve(3)=100 plus flat LevelBaseHp=68 -> effective level base 168.
        // base = 132 + 168 = 300, class 1 0.3 -> 90.
        var curve = new StatBaseCurve(new long[] { 0, 0, 100 }); // level 3 -> 100
        var inputs = TenAcross() with
        {
            Level = 3,
            LevelBaseHpCurve = curve,
            LevelBaseHp = 68,
        };
        Assert.Equal(90L, VitalFormula.ComputeMaxHp(inputs));
    }

    [Fact]
    public void ResolveLevelBase_Helpers_ReturnZeroByDefault()
    {
        var empty = VitalFormulaInputs.Empty;
        Assert.Equal(0L, empty.ResolveLevelBaseHp());
        Assert.Equal(0L, empty.ResolveLevelBaseMp());
    }

    [Fact]
    public void Injection_IsDeterministic()
    {
        var curve = new StatBaseCurve(new long[] { 5, 15, 30, 60, 168 });
        var inputs = TenAcross() with { Level = 5, LevelBaseHpCurve = curve };

        long first = VitalFormula.ComputeMaxHp(inputs);
        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(first, VitalFormula.ComputeMaxHp(inputs));
        }
    }
}
