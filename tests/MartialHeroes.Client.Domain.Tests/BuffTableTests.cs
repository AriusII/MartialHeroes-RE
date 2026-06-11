using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class BuffTableTests
{
    [Fact]
    public void SlotCount_Is31()
    {
        Assert.Equal(31, new BuffTable().Count);
        Assert.Equal(31, BuffTable.SlotCount);
    }

    [Fact]
    public void Apply_SetsActiveSlot_AndTickDecrements()
    {
        var table = new BuffTable();
        table.Apply(slotIndex: 5, effectCode: (int)BuffEffectCode.RootSnare, durationTicks: 3, param: 0, magnitude: 0);

        Assert.True(table[5].IsActive);
        Assert.Equal(3, table[5].DurationTicks);

        table.Tick();
        Assert.Equal(2, table[5].DurationTicks);
        Assert.True(table[5].IsActive);
    }

    [Fact]
    public void Tick_ExpiresSlot_WhenDurationReachesZero()
    {
        var table = new BuffTable();
        table.Apply(0, (int)BuffEffectCode.EnterStance, durationTicks: 1, param: 0, magnitude: 0);

        int expired = table.Tick();

        Assert.Equal(1, expired);
        Assert.False(table[0].IsActive);
        Assert.Equal(0, table[0].DurationTicks);
    }

    [Fact]
    public void Apply_RefreshOverwrites_NoStackCounter()
    {
        var table = new BuffTable();
        table.Apply(2, (int)BuffEffectCode.MotionState44, durationTicks: 2, param: 1, magnitude: 10);
        // Re-apply the same slot: refresh (overwrite), not additive. spec: skills.md §6.3.
        table.Apply(2, (int)BuffEffectCode.MotionState44, durationTicks: 5, param: 2, magnitude: 20);

        Assert.Equal(5, table[2].DurationTicks);
        Assert.Equal(2, table[2].Param);
        Assert.Equal((ushort)20, table[2].Magnitude);
    }

    [Fact]
    public void Apply_ZeroDuration_ClearsSlot()
    {
        var table = new BuffTable();
        table.Apply(0, (int)BuffEffectCode.RootSnare, 3, 0, 0);
        table.Apply(0, (int)BuffEffectCode.RootSnare, durationTicks: 0, param: 0, magnitude: 0);

        Assert.True(table[0].IsEmpty);
        Assert.False(table[0].IsActive);
    }

    [Fact]
    public void Dispel_ClearsEffects43_46_47_Only()
    {
        var table = new BuffTable();
        table.Apply(0, (int)BuffEffectCode.EnterStance, 5, 0, 0);    // 43 -> cleared
        table.Apply(1, (int)BuffEffectCode.AppearanceSwap, 5, 0, 0); // 46 -> cleared
        table.Apply(2, (int)BuffEffectCode.RootSnare, 5, 0, 0);      // 47 -> cleared
        table.Apply(3, (int)BuffEffectCode.PoisonTransform, 5, 0, 0); // 50 -> kept

        table.Dispel();

        Assert.True(table[0].IsEmpty);
        Assert.True(table[1].IsEmpty);
        Assert.True(table[2].IsEmpty);
        Assert.True(table[3].IsActive);
    }

    [Fact]
    public void IsRooted_TrueWhenRootSnareActive()
    {
        var table = new BuffTable();
        Assert.False(table.IsRooted);
        table.Apply(0, (int)BuffEffectCode.RootSnare, 2, 0, 0);
        Assert.True(table.IsRooted);
    }

    [Fact]
    public void PercentGatedFlag_SetOnlyBelow100()
    {
        var below = new BuffDebuff { EffectCode = (int)BuffEffectCode.PercentGatedFlag, DurationTicks = 5, Magnitude = 99 };
        var atGate = new BuffDebuff { EffectCode = (int)BuffEffectCode.PercentGatedFlag, DurationTicks = 5, Magnitude = 100 };

        Assert.True(below.IsPercentGatedFlagSet);
        Assert.False(atGate.IsPercentGatedFlagSet);
    }

    [Fact]
    public void NegativeDuration_ClampsToZero_NotExpiredEvent()
    {
        var buff = new BuffDebuff { EffectCode = 1, DurationTicks = -5 };
        var (next, expired) = buff.TickOnce();

        Assert.Equal(0, next.DurationTicks);
        Assert.False(expired);
    }

    [Fact]
    public void BuffStatBridge_BuildsContributions_ForActiveBuffsOnly()
    {
        var active = new BuffDebuff { EffectCode = 1, DurationTicks = 3 };
        var expired = new BuffDebuff { EffectCode = 2, DurationTicks = 0 };
        ReadOnlySpan<BuffStatGrant> grants =
        [
            new BuffStatGrant(active, StatKey.Str, 10),
            new BuffStatGrant(expired, StatKey.Dex, 99),
            new BuffStatGrant(active, StatKey.AllStats, 5),
        ];

        Span<BuffContribution> dest = stackalloc BuffContribution[3];
        int written = BuffStatBridge.BuildContributions(grants, dest);

        Assert.Equal(2, written);
        Assert.Equal(new BuffContribution(StatKey.Str, 10), dest[0]);
        Assert.Equal(new BuffContribution(StatKey.AllStats, 5), dest[1]);
    }

    [Fact]
    public void BuffStatBridge_IntegratesWith_StatAggregation()
    {
        // An active +10 STR buff should add to the aggregated STR. spec: combat.md §2.2.
        var buff = new BuffDebuff { EffectCode = 1, DurationTicks = 3 };
        Span<BuffContribution> buffs = stackalloc BuffContribution[1];
        int n = BuffStatBridge.BuildContributions([new BuffStatGrant(buff, StatKey.Str, 10)], buffs);

        int str = StatAggregation.Aggregate(
            StatKey.Str,
            serverBase: 50,
            buffs[..n],
            equipment: [],
            setPieces: [],
            modifierSlots: []);

        Assert.Equal(60, str);
    }
}
