using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Shared.Kernel.Ids;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class CooldownTableTests
{
    [Fact]
    public void SlotCount_Is240()
    {
        Assert.Equal(240, new CooldownTable().Count);
        Assert.Equal(240, CooldownTable.SlotCount);
    }

    [Fact]
    public void Arm_ThenTick_DecrementsRemaining_AndExpires()
    {
        var table = new CooldownTable();
        var skill = new SkillId(7);
        table.SetSlot(3, skill, durationMs: 1000);

        table.Arm(skill, now: 100);
        table.TickAll(now: 600); // 100 + 1000 - 600 = 500 left
        Assert.Equal(500, table[3].RemainingMs);
        Assert.False(table[3].IsReady);

        table.TickAll(now: 1100); // expired (100 + 1000 = 1100)
        Assert.Equal(0, table[3].RemainingMs);
        Assert.True(table[3].IsReady);
        Assert.False(table[3].Armed);
    }

    [Fact]
    public void CheckReady_TicksThenReports()
    {
        var table = new CooldownTable();
        var skill = new SkillId(9);
        table.SetSlot(0, skill, durationMs: 500);
        table.Arm(skill, 0);

        Assert.False(table.CheckReady(skill, now: 400));
        Assert.True(table.CheckReady(skill, now: 500));
    }

    [Fact]
    public void CheckReady_SkillNotInTable_IsReady()
    {
        Assert.True(new CooldownTable().CheckReady(new SkillId(123), now: 0));
    }

    [Fact]
    public void Arm_ZeroDuration_StaysReady()
    {
        var table = new CooldownTable();
        var skill = new SkillId(5);
        table.SetSlot(0, skill, durationMs: 0);

        table.Arm(skill, 100);

        Assert.True(table[0].IsReady);
        Assert.False(table[0].Armed);
    }

    [Fact]
    public void Arm_MissingSkill_ReturnsMinusOne()
    {
        Assert.Equal(-1, new CooldownTable().Arm(new SkillId(1), 0));
    }
}