using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Structural tests for the derived combat-stat aggregate and the recovered stat-key enumeration.
/// spec: Docs/RE/specs/combat.md §1 / §2.2.
/// </summary>
public sealed class CombatStatsTests
{
    [Fact]
    public void CombatStats_EmptyIsAllZero()
    {
        CombatStats c = CombatStats.Empty;
        Assert.Equal(0, c.Str);
        Assert.Equal(0, c.MinDamage);
        Assert.Equal(0, c.MaxDamage);
        Assert.Equal(0, c.Defence);
        Assert.Equal(0f, c.CriticalRate);
        Assert.Equal(0f, c.CriticalHit);
        Assert.Equal(0f, c.HitRate);
        Assert.Equal(0f, c.Range);
        Assert.Equal(0, c.AttackRating);
        Assert.Equal(0, c.HitRating);
    }

    [Fact]
    public void CombatStats_CriticalRateAndCriticalHitAreDistinctFields()
    {
        // spec §1 / UNVERIFIED #2: both critical floats are modelled, never collapsed.
        var c = CombatStats.Empty with { CriticalRate = 1.5f, CriticalHit = 2.5f };
        Assert.Equal(1.5f, c.CriticalRate);
        Assert.Equal(2.5f, c.CriticalHit);
        Assert.NotEqual(c.CriticalRate, c.CriticalHit);
    }

    [Fact]
    public void CombatStats_PveAndPvpRatePairsAreDistinct()
    {
        var c = CombatStats.Empty with
        {
            HuntDamageRate0 = 1, HuntDamageRate1 = 2,
            PvpDamageRate0 = 3, PvpDamageRate1 = 4,
        };
        Assert.Equal(1, c.HuntDamageRate0);
        Assert.Equal(2, c.HuntDamageRate1);
        Assert.Equal(3, c.PvpDamageRate0);
        Assert.Equal(4, c.PvpDamageRate1);
    }

    [Fact]
    public void CombatStats_FourOrderSpecialBuckets()
    {
        var c = CombatStats.Empty with
        {
            OrderSpecial0 = 0.1f, OrderSpecial1 = 0.2f, OrderSpecial2 = 0.3f, OrderSpecial3 = 0.4f,
        };
        Assert.Equal(0.1f, c.OrderSpecial0);
        Assert.Equal(0.2f, c.OrderSpecial1);
        Assert.Equal(0.3f, c.OrderSpecial2);
        Assert.Equal(0.4f, c.OrderSpecial3);
    }

    [Theory]
    [InlineData(StatKey.Str, 70)]
    [InlineData(StatKey.Agi, 71)]
    [InlineData(StatKey.Dex, 72)]
    [InlineData(StatKey.Int, 73)]
    [InlineData(StatKey.Con, 74)]
    [InlineData(StatKey.AllStats, 93)]
    [InlineData(StatKey.HpPercent, 81)]
    [InlineData(StatKey.HitPercentMultiplier, 83)]
    [InlineData(StatKey.HitFlatFinal, 61)]
    public void StatKey_HasRecoveredNumericValues(StatKey key, int expected)
    {
        // spec: Docs/RE/specs/combat.md §2.2 — confirmed stat-key integers.
        Assert.Equal(expected, (int)key);
    }
}