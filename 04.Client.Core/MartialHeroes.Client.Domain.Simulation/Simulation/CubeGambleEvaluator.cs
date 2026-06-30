namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public enum CubeGambleOutcome : byte
{
    Push,
    Win,
    Loss
}

[Flags]
public enum CubeGambleWinLines : uint
{
    None = 0u,
    Jackpot = 1u << 0,
    PairTie = 1u << 1,
    PairHigh = 1u << 2,
    PairLow = 1u << 3,
    LineLow = 1u << 4,
    LineSeven = 1u << 5,
    LineHigh = 1u << 6,
    Odd = 1u << 7,
    Even = 1u << 8,
    Doubles = 1u << 9
}

public readonly record struct CubeGambleSettlement(CubeGambleOutcome Outcome, long Delta)
{
    public byte LabelAlign => Outcome switch
    {
        CubeGambleOutcome.Win => (byte)0,
        CubeGambleOutcome.Loss => (byte)1,
        _ => (byte)2
    };
}

public readonly record struct CubeGamblePayout(
    CubeGambleOutcome Outcome,
    long Delta,
    byte LabelAlign,
    long DisplayedTotal,
    bool RaiseWinBanner,
    bool IncrementDailyPlay);

public readonly record struct CubeGambleReelMatch(
    CubeGambleWinLines Lines,
    sbyte SpecialSlot,
    byte DoublesFace);

public static class CubeGambleEvaluator
{
    public const sbyte NoSpecialSlot = -1;
    public const byte NoDoublesFace = 0;

    public static CubeGambleSettlement Settle(long oldMoney, long newMoney)
    {
        var delta = newMoney - oldMoney;

        var outcome = delta > 0L
            ? CubeGambleOutcome.Win
            : delta < 0L
                ? CubeGambleOutcome.Loss
                : CubeGambleOutcome.Push;

        return new CubeGambleSettlement(outcome, delta);
    }

    public static CubeGamblePayout SettleWithStake(long oldMoney, long newMoney, long stake)
    {
        var settlement = Settle(oldMoney, newMoney);

        return new CubeGamblePayout(
            settlement.Outcome,
            settlement.Delta,
            settlement.LabelAlign,
            newMoney + stake - oldMoney,
            settlement.Outcome == CubeGambleOutcome.Win && newMoney != oldMoney,
            stake > 0L);
    }

    public static CubeGambleReelMatch EvaluateReels(byte d5a, byte d5b, byte d4a, byte d4b)
    {
        var f5a = d5a + 1;
        var f5b = d5b + 1;
        var f4a = d4a + 1;
        var f4b = d4b + 1;

        var pairA = f5a + f5b;
        var pairB = f4a + f4b;

        var lines = CubeGambleWinLines.None;

        byte doublesFace = NoDoublesFace;

        if (d5a == d5b)
        {
            lines |= CubeGambleWinLines.Doubles;
            doublesFace = (byte)f5a;
        }

        if (d5a == d5b && d5b == d4a && d4a == d4b)
            lines |= CubeGambleWinLines.Jackpot;

        if (pairA == pairB)
            lines |= CubeGambleWinLines.PairTie;
        else if (pairA > pairB)
            lines |= CubeGambleWinLines.PairHigh;
        else
            lines |= CubeGambleWinLines.PairLow;

        if (pairA < 7)
            lines |= CubeGambleWinLines.LineLow;
        else if (pairA == 7)
            lines |= CubeGambleWinLines.LineSeven;
        else
            lines |= CubeGambleWinLines.LineHigh;

        if ((f5a & 1) == 1 && (f5b & 1) == 1)
            lines |= CubeGambleWinLines.Odd;

        if ((f5a & 1) == 0 && (f5b & 1) == 0)
            lines |= CubeGambleWinLines.Even;

        return new CubeGambleReelMatch(lines, MatchSpecialCombo(f5a, f5b), doublesFace);
    }

    private static sbyte MatchSpecialCombo(int f5a, int f5b)
    {
        if (IsPair(f5a, f5b, 1, 2) || IsPair(f5a, f5b, 5, 6))
            return 0;

        if (IsPair(f5a, f5b, 2, 3) || IsPair(f5a, f5b, 4, 5))
            return 1;

        if (IsPair(f5a, f5b, 1, 4) || IsPair(f5a, f5b, 3, 6))
            return 2;

        return NoSpecialSlot;
    }

    private static bool IsPair(int a, int b, int x, int y)
    {
        return (a == x && b == y) || (a == y && b == x);
    }
}
