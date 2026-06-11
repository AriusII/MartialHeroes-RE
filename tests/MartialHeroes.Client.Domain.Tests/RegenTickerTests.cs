using MartialHeroes.Client.Domain.Simulation;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class RegenTickerTests
{
    [Fact]
    public void Advance_ProducesNoStep_BeforeIntervalElapses()
    {
        var ticker = new RegenTicker(stepIntervalMs: 100, amountPerStep: 5);

        (RegenTicker next, uint steps) = ticker.Advance(50);

        Assert.Equal(0u, steps);
        Assert.Equal(50u, next.AccumulatedMs);
    }

    [Fact]
    public void Advance_ProducesOneStep_AtExactInterval()
    {
        var ticker = new RegenTicker(100, 5);

        (RegenTicker next, uint steps) = ticker.Advance(100);

        Assert.Equal(1u, steps);
        Assert.Equal(0u, next.AccumulatedMs);
        Assert.Equal(5u, ticker.AmountFor(steps));
    }

    [Fact]
    public void Advance_CarriesRemainderForward()
    {
        var ticker = new RegenTicker(100, 5);

        (RegenTicker next, uint steps) = ticker.Advance(250);

        Assert.Equal(2u, steps);
        Assert.Equal(50u, next.AccumulatedMs);
    }

    [Fact]
    public void Regen_IsFrameRateIndependent_OneBigTickEqualsManySmallTicks()
    {
        // One 100ms advance.
        var big = new RegenTicker(10, 1);
        (RegenTicker bigNext, uint bigSteps) = big.Advance(100);

        // Ten 10ms advances.
        var small = new RegenTicker(10, 1);
        uint smallSteps = 0;
        for (int i = 0; i < 10; i++)
        {
            (small, uint s) = small.Advance(10);
            smallSteps += s;
        }

        Assert.Equal(10u, bigSteps);
        Assert.Equal(bigSteps, smallSteps);
        Assert.Equal(bigNext.AccumulatedMs, small.AccumulatedMs);
    }

    [Fact]
    public void Regen_FrameRateIndependent_WithRemainder()
    {
        // Total 95ms, step 10ms => 9 steps, 5ms remainder, regardless of chunking.
        var big = new RegenTicker(10, 2);
        (RegenTicker bigNext, uint bigSteps) = big.Advance(95);

        var small = new RegenTicker(10, 2);
        uint smallSteps = 0;
        foreach (uint chunk in new uint[] { 7, 13, 30, 45 }) // sums to 95
        {
            (small, uint s) = small.Advance(chunk);
            smallSteps += s;
        }

        Assert.Equal(9u, bigSteps);
        Assert.Equal(bigSteps, smallSteps);
        Assert.Equal(5u, bigNext.AccumulatedMs);
        Assert.Equal(5u, small.AccumulatedMs);
    }

    [Fact]
    public void Constructor_RejectsZeroInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegenTicker(0, 1));
    }

    [Fact]
    public void AmountFor_SaturatesAtUintMax()
    {
        var ticker = new RegenTicker(1, uint.MaxValue);
        Assert.Equal(uint.MaxValue, ticker.AmountFor(2));
    }
}