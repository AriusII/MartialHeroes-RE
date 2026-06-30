using MartialHeroes.Client.Domain.Simulation.Simulation;
using Xunit;

namespace MartialHeroes.Client.Domain.Simulation.Tests;

public sealed class CubeGambleEvaluatorTests
{
    [Theory]
    [InlineData(100L, 150L, CubeGambleOutcome.Win, 50L)]
    [InlineData(150L, 100L, CubeGambleOutcome.Loss, -50L)]
    [InlineData(100L, 100L, CubeGambleOutcome.Push, 0L)]
    [InlineData(0L, 1_000_000_000_000L, CubeGambleOutcome.Win, 1_000_000_000_000L)]
    public void Settle_SignedDelta_SelectsOutcome(
        long oldMoney, long newMoney, CubeGambleOutcome expected, long expectedDelta)
    {
        var r = CubeGambleEvaluator.Settle(oldMoney, newMoney);

        Assert.Equal(expected, r.Outcome);
        Assert.Equal(expectedDelta, r.Delta);
    }

    [Fact]
    public void EvaluateReels_AllFourEqual_SetsJackpot()
    {
        var r = CubeGambleEvaluator.EvaluateReels(3, 3, 3, 3);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.Jackpot));
    }

    [Fact]
    public void EvaluateReels_PairTie_WhenPairSumsEqual()
    {
        var r = CubeGambleEvaluator.EvaluateReels(2, 3, 1, 4);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.PairTie));
        Assert.False(r.Lines.HasFlag(CubeGambleWinLines.PairHigh));
        Assert.False(r.Lines.HasFlag(CubeGambleWinLines.PairLow));
    }

    [Fact]
    public void EvaluateReels_PairHigh_WhenPhase5SumGreater()
    {
        var r = CubeGambleEvaluator.EvaluateReels(5, 5, 0, 0);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.PairHigh));
    }

    [Fact]
    public void EvaluateReels_PairLow_WhenPhase5SumSmaller()
    {
        var r = CubeGambleEvaluator.EvaluateReels(0, 0, 5, 5);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.PairLow));
    }

    [Fact]
    public void EvaluateReels_LineLow_WhenPairAUnderSeven()
    {
        var r = CubeGambleEvaluator.EvaluateReels(0, 1, 0, 0);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.LineLow));
        Assert.False(r.Lines.HasFlag(CubeGambleWinLines.LineSeven));
        Assert.False(r.Lines.HasFlag(CubeGambleWinLines.LineHigh));
    }

    [Fact]
    public void EvaluateReels_LineSeven_WhenPairAEqualsSeven()
    {
        var r = CubeGambleEvaluator.EvaluateReels(2, 3, 0, 0);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.LineSeven));
    }

    [Fact]
    public void EvaluateReels_LineHigh_WhenPairAOverSeven()
    {
        var r = CubeGambleEvaluator.EvaluateReels(5, 5, 5, 5);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.LineHigh));
    }

    [Fact]
    public void EvaluateReels_Odd_WhenBothPhase5FacesOdd()
    {
        var r = CubeGambleEvaluator.EvaluateReels(0, 2, 0, 0);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.Odd));
        Assert.False(r.Lines.HasFlag(CubeGambleWinLines.Even));
    }

    [Fact]
    public void EvaluateReels_Even_WhenBothPhase5FacesEven()
    {
        var r = CubeGambleEvaluator.EvaluateReels(1, 3, 0, 0);

        Assert.True(r.Lines.HasFlag(CubeGambleWinLines.Even));
        Assert.False(r.Lines.HasFlag(CubeGambleWinLines.Odd));
    }

    [Theory]
    [InlineData(0, 1, (sbyte)0)]
    [InlineData(1, 0, (sbyte)0)]
    [InlineData(4, 5, (sbyte)0)]
    [InlineData(5, 4, (sbyte)0)]
    [InlineData(1, 2, (sbyte)1)]
    [InlineData(2, 1, (sbyte)1)]
    [InlineData(3, 4, (sbyte)1)]
    [InlineData(4, 3, (sbyte)1)]
    [InlineData(0, 3, (sbyte)2)]
    [InlineData(3, 0, (sbyte)2)]
    [InlineData(2, 5, (sbyte)2)]
    [InlineData(5, 2, (sbyte)2)]
    public void EvaluateReels_SpecialCombo_MatchesEitherOrder(byte d5a, byte d5b, sbyte expectedSlot)
    {
        var r = CubeGambleEvaluator.EvaluateReels(d5a, d5b, 0, 0);

        Assert.Equal(expectedSlot, r.SpecialSlot);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(2, 3)]
    [InlineData(0, 0)]
    public void EvaluateReels_NoSpecialCombo_ReturnsSentinel(byte d5a, byte d5b)
    {
        var r = CubeGambleEvaluator.EvaluateReels(d5a, d5b, 0, 0);

        Assert.Equal(CubeGambleEvaluator.NoSpecialSlot, r.SpecialSlot);
    }
}
