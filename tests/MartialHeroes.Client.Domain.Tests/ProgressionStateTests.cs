using MartialHeroes.Client.Domain.Progression;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Spec-derived tests for the progression Domain models. All magnitudes used here are TEST DATA — the
/// real XP rates and per-level rank-XP tables are server-authored (spec: progression.md §12 Q6) and are
/// injected, never embedded. spec: Docs/RE/specs/progression.md §3 / §3.1 / §4.
/// </summary>
public sealed class ProgressionStateTests
{
    // ---- ExperienceModel §3 ------------------------------------------------------------------------

    [Fact]
    public void AddExperience_AddsToBothAccumulators()
    {
        // spec: progression.md §3 / §3.4 — the i64 amount adds to current-XP AND lifetime-XP.
        var xp = default(ExperienceModel)
            .AddExperience(1000)
            .AddExperience(250);

        Assert.Equal(1250L, xp.CurrentXp);
        Assert.Equal(1250L, xp.LifetimeXp);
    }

    [Fact]
    public void ResyncCurrentXp_OverwritesCurrent_LeavesLifetime()
    {
        // spec: progression.md §6 — 5/67 primes the XP bar (current-XP) authoritatively; lifetime untouched.
        var xp = default(ExperienceModel).AddExperience(900); // current=900, lifetime=900
        var resynced = xp.ResyncCurrentXp(400);

        Assert.Equal(400L, resynced.CurrentXp);
        Assert.Equal(900L, resynced.LifetimeXp);
    }

    [Fact]
    public void SplitBonus_FollowsTheSpecFormula()
    {
        // spec: progression.md §3.1 — shown_base = 100*amount/(rate+100); bonus = amount - shown_base.
        // amount 200, rate 100% → base = 100*200/200 = 100; bonus = 100.
        var (shownBase, bonus) = ExperienceModel.SplitBonus(amount: 200, ratePercent: 100);
        Assert.Equal(100L, shownBase);
        Assert.Equal(100L, bonus);

        // rate 0% → base = amount, bonus = 0 (no bonus).
        var (b0, bonus0) = ExperienceModel.SplitBonus(amount: 200, ratePercent: 0);
        Assert.Equal(200L, b0);
        Assert.Equal(0L, bonus0);
    }

    [Fact]
    public void SplitBonus_NonPositiveDenominator_YieldsFullAmountNoBonus()
    {
        // rate -100 → denominator 0; the guard returns the full amount as base, no bonus.
        var (shownBase, bonus) = ExperienceModel.SplitBonus(amount: 500, ratePercent: -100);
        Assert.Equal(500L, shownBase);
        Assert.Equal(0L, bonus);
    }

    // ---- RankXpModel §4 ----------------------------------------------------------------------------

    [Fact]
    public void RankCap_Is25()
    {
        Assert.Equal(25, RankXpModel.RankCap); // spec: progression.md §4 (cap of 25).
    }

    [Fact]
    public void ApplyRankGain_Mode2_DirectAdd_NoLevelMath()
    {
        // spec: progression.md §4 — mode 2 adds straight to the rank accumulator (no table).
        var rank = default(RankXpModel)
            .ApplyRankGain(amount: 30, mode: 2, levelCache: 7, divisorTable: null, capTable: null)
            .ApplyRankGain(amount: 12, mode: 2, levelCache: 7, divisorTable: null, capTable: null);

        Assert.Equal(42L, rank.RankAccumulator);
        Assert.Equal(0L, rank.WithinRank);
    }

    [Fact]
    public void ApplyRankGain_NonMode2_RunsTheDivisorRoutine()
    {
        // spec: progression.md §4 — rank_acc += (remainder+amount)/divisor[level]; within = % divisor.
        // level 3 → divisor 10; amount 25 → +2 ranks, remainder 5.
        var divisors = new long[] { 0, 0, 0, 10 };
        var rank = default(RankXpModel)
            .ApplyRankGain(amount: 25, mode: 0, levelCache: 3, divisorTable: divisors, capTable: null);

        Assert.Equal(2L, rank.RankAccumulator);
        Assert.Equal(5L, rank.WithinRank);
    }

    [Fact]
    public void ApplyRankGain_CarriesRemainderAcrossGains()
    {
        // spec: progression.md §4 — the within-rank remainder carries into the next gain.
        var divisors = new long[] { 0, 0, 0, 10 };
        var rank = default(RankXpModel)
            .ApplyRankGain(amount: 7, mode: 0, levelCache: 3, divisorTable: divisors, capTable: null) // within=7
            .ApplyRankGain(amount: 8, mode: 0, levelCache: 3, divisorTable: divisors,
                capTable: null); // 7+8=15 → +1 rank, within=5

        Assert.Equal(1L, rank.RankAccumulator);
        Assert.Equal(5L, rank.WithinRank);
    }

    [Fact]
    public void ApplyRankGain_CapBoundsTheWithinRankRemainder()
    {
        // spec: progression.md §4 — the cap table bounds the within-rank value.
        var divisors = new long[] { 0, 0, 0, 100 };
        var caps = new long[] { 0, 0, 0, 3 };
        var rank = default(RankXpModel)
            .ApplyRankGain(amount: 50, mode: 0, levelCache: 3, divisorTable: divisors, capTable: caps);

        // 50 / 100 = 0 ranks, remainder 50 → clamped to cap 3.
        Assert.Equal(0L, rank.RankAccumulator);
        Assert.Equal(3L, rank.WithinRank);
    }

    [Fact]
    public void ApplyRankGain_LevelAboveCap_ClampsIndexTo25()
    {
        // spec: progression.md §4 — the index (and the 25 test) use the level cache, clamped at 25.
        var divisors = new long[26];
        divisors[25] = 5;
        var rank = default(RankXpModel)
            .ApplyRankGain(amount: 12, mode: 0, levelCache: 99, divisorTable: divisors, capTable: null);

        Assert.Equal(2L, rank.RankAccumulator); // 12 / 5 = 2
        Assert.Equal(2L, rank.WithinRank); // 12 % 5 = 2
    }

    [Fact]
    public void ApplyRankGain_ZeroDivisor_ThrowsLevelTableException()
    {
        // spec: progression.md §4 — divisor 0 for the active level is the "leveltable error".
        var divisors = new long[] { 0, 0, 0, 0 };
        var ex = Assert.Throws<LevelTableException>(() =>
            default(RankXpModel).ApplyRankGain(
                amount: 5, mode: 0, levelCache: 3, divisorTable: divisors, capTable: null));

        Assert.Equal(3, ex.LevelIndex);
    }

    // ---- ProgressionState aggregate ----------------------------------------------------------------

    [Fact]
    public void ProgressionState_RoutesExperienceAndRankIndependently()
    {
        var divisors = new long[] { 0, 0, 10 };
        var state = default(ProgressionState)
            .AddExperience(1000)
            .AddRankXp(amount: 25, mode: 0, levelCache: 2, divisorTable: divisors, capTable: null);

        Assert.Equal(1000L, state.Experience.CurrentXp);
        Assert.Equal(1000L, state.Experience.LifetimeXp);
        Assert.Equal(2L, state.RankXp.RankAccumulator);
        Assert.Equal(5L, state.RankXp.WithinRank);
    }
}