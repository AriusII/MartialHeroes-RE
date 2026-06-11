using MartialHeroes.Client.Domain.Quests;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class QuestObjectiveAndLogTests
{
    [Fact]
    public void Objective_Advance_MatchingTarget_Saturates()
    {
        var obj = new QuestObjective { Kind = QuestObjectiveKind.Kill, TargetId = 7, RequiredCount = 3 };

        obj = obj.Advance(targetId: 7);
        Assert.Equal(1, obj.CurrentCount);
        Assert.False(obj.IsComplete);

        obj = obj.Advance(7, amount: 5); // saturates at 3.
        Assert.Equal(3, obj.CurrentCount);
        Assert.True(obj.IsComplete);
    }

    [Fact]
    public void Objective_Advance_NonMatchingTarget_NoChange()
    {
        var obj = new QuestObjective { TargetId = 7, RequiredCount = 3 };
        var after = obj.Advance(targetId: 8);
        Assert.Equal(0, after.CurrentCount);
    }

    [Fact]
    public void Objective_Advance_NonPositiveAmount_NoChange()
    {
        var obj = new QuestObjective { TargetId = 7, RequiredCount = 3 };
        Assert.Equal(0, obj.Advance(7, 0).CurrentCount);
        Assert.Equal(0, obj.Advance(7, -1).CurrentCount);
    }

    [Fact]
    public void Objective_Advance_AlreadyComplete_NoChange()
    {
        var obj = new QuestObjective { TargetId = 7, RequiredCount = 1, CurrentCount = 1 };
        Assert.Equal(1, obj.Advance(7).CurrentCount);
    }

    [Fact]
    public void Log_Constants_MatchSpec()
    {
        Assert.Equal(20, QuestLog.EntryCount);
        Assert.Equal(10, QuestLog.SlotFlagCount);
    }

    [Fact]
    public void Log_ApplySnapshot_StoresEntries_AndReportsTrackingOnTransition()
    {
        var log = new QuestLog();
        ReadOnlySpan<QuestLogEntry> entries =
        [
            new QuestLogEntry(100, "First"),
            new QuestLogEntry(200, "Second"),
        ];
        ReadOnlySpan<byte> slotA = [1, 0, 1];
        ReadOnlySpan<byte> slotB = [0, 1];

        bool trackingOpened = log.ApplySnapshot(entries, slotA, slotB, trackingFlag: 1, panelB: 2, panelC: 3);

        Assert.True(trackingOpened); // 0 -> non-zero.
        Assert.Equal(2, log.ActiveEntryCount);
        Assert.Equal(100u, log.EntryAt(0).QuestId);
        Assert.Equal("Second", log.EntryAt(1).Name);
        Assert.Equal((byte)1, log.SlotAFlag(0));
        Assert.Equal((byte)1, log.SlotBFlag(1));
        Assert.Equal((byte)1, log.TrackingFlag);
        Assert.Equal((byte)2, log.PanelB);
        Assert.Equal((byte)3, log.PanelC);
        Assert.True(log.Contains(200));
    }

    [Fact]
    public void Log_ApplySnapshot_NoTrackingTransition_WhenAlreadyOn()
    {
        var log = new QuestLog();
        log.ApplySnapshot([], [], [], trackingFlag: 1, 0, 0); // first turns on
        bool secondTransition = log.ApplySnapshot([], [], [], trackingFlag: 1, 0, 0); // stays on

        Assert.False(secondTransition);
    }

    [Fact]
    public void Log_ApplySnapshot_ClearsPreviousEntries()
    {
        var log = new QuestLog();
        log.ApplySnapshot([new QuestLogEntry(1, "A"), new QuestLogEntry(2, "B")], [], [], 0, 0, 0);
        log.ApplySnapshot([new QuestLogEntry(3, "C")], [], [], 0, 0, 0);

        Assert.Equal(1, log.ActiveEntryCount);
        Assert.False(log.Contains(1)); // previous entries cleared.
        Assert.True(log.Contains(3));
    }

    [Fact]
    public void Log_ApplySnapshot_RejectsOversizedInput()
    {
        var log = new QuestLog();
        var tooMany = new QuestLogEntry[QuestLog.EntryCount + 1];

        Assert.Throws<ArgumentException>(() => log.ApplySnapshot(tooMany, [], [], 0, 0, 0));
    }
}
