using MartialHeroes.Client.Domain.Stats.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Stats.Tests;

public sealed class SkillDamageAccumulatorTests
{
    [Fact]
    public void Empty_HasNoDisplayDamage()
    {
        var acc = new SkillDamageAccumulator();

        Assert.Equal(0L, acc.RawSum);
        Assert.Equal(0, acc.RecordCount);
        Assert.Equal(0L, acc.DisplayTotal);
        Assert.False(acc.HasDisplayDamage);
    }

    [Fact]
    public void SingleNegativeRecord_NegatesForDisplay()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(-100L);

        Assert.Equal(-100L, acc.RawSum);
        Assert.Equal(1, acc.RecordCount);
        Assert.Equal(100L, acc.DisplayTotal);
        Assert.True(acc.HasDisplayDamage);
    }

    [Fact]
    public void MultipleRecords_SumThenNegate()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(-100L);
        acc.Add(-250L);
        acc.Add(-50L);

        Assert.Equal(-400L, acc.RawSum);
        Assert.Equal(3, acc.RecordCount);
        Assert.Equal(400L, acc.DisplayTotal);
    }

    [Fact]
    public void TwoDwordOverload_CombinesLowAndHigh()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(0x0000_0000u, 0x0000_0001u);
        acc.Add(0xFFFF_FFFFu, 0x0000_0000u);

        Assert.Equal(0x1_FFFF_FFFFL, acc.RawSum);
        Assert.Equal(2, acc.RecordCount);
    }

    [Fact]
    public void TwoDwordOverload_PropagatesCarryAcrossDwordBoundary()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(0xFFFF_FFFFu, 0x0000_0000u);
        acc.Add(0x0000_0001u, 0x0000_0000u);

        Assert.Equal(0x1_0000_0000L, acc.RawSum);
    }

    [Fact]
    public void TwoDwordOverload_ReconstructsSignedNegative()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(0xFFFF_FFFFu, 0xFFFF_FFFFu);

        Assert.Equal(-1L, acc.RawSum);
        Assert.Equal(1L, acc.DisplayTotal);
        Assert.True(acc.HasDisplayDamage);
    }

    [Fact]
    public void ZeroMagnitudeRecord_CountsButHasNoDisplayDamage()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(0L);

        Assert.Equal(1, acc.RecordCount);
        Assert.Equal(0L, acc.DisplayTotal);
        Assert.False(acc.HasDisplayDamage);
    }

    [Fact]
    public void ApplyTo_ReducesHpBySignedSum()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(-300L);

        Assert.Equal(700L, acc.ApplyTo(1000L));
    }

    [Fact]
    public void ApplyTo_ClampsDepletedHpToZero()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(-500L);

        Assert.Equal(0L, acc.ApplyTo(100L));
    }

    [Fact]
    public void ApplyTo_AddsPositiveMagnitudeToHp()
    {
        var acc = new SkillDamageAccumulator();

        acc.Add(200L);

        Assert.Equal(1200L, acc.ApplyTo(1000L));
    }

    [Fact]
    public void ApplyTo_HandlesHpBeyondU32()
    {
        var acc = new SkillDamageAccumulator();

        var hp = (long)uint.MaxValue + 5000L;
        acc.Add(-1000L);

        Assert.Equal(hp - 1000L, acc.ApplyTo(hp));
    }
}
