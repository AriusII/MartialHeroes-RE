using MartialHeroes.Client.Domain.Quests;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class QuestStateTests
{
    private static QuestState Available => default; // Phase == Available, QuestId == 0.

    [Fact]
    public void AcceptGateThreshold_Is26()
    {
        Assert.Equal(26, QuestState.AcceptGateThreshold);
        Assert.False(QuestState.AcceptGatePasses(25));
        Assert.True(QuestState.AcceptGatePasses(26));
    }

    [Fact]
    public void Accept_GatePasses_EntersInProgress()
    {
        var (next, accepted) = Available.Accept(questId: 500, gatedStatusValue: 26);

        Assert.True(accepted);
        Assert.Equal(QuestPhase.InProgress, next.Phase);
        Assert.Equal(500u, next.QuestId);
    }

    [Fact]
    public void Accept_GateFails_Rejected()
    {
        var (next, accepted) = Available.Accept(500, gatedStatusValue: 25);

        Assert.False(accepted);
        Assert.Equal(QuestPhase.Available, next.Phase);
    }

    [Fact]
    public void Accept_RejectedWhenNotAvailable()
    {
        var (state, _) = Available.Accept(1, 26);
        Assert.False(state.Accept(2, 26).Accepted); // already in progress
    }

    [Fact]
    public void Complete_FromInProgress_Only()
    {
        var (state, _) = Available.Accept(1, 26);

        Assert.Equal(QuestPhase.Completed, state.Complete().Phase);
        Assert.Equal(QuestPhase.Available, Available.Complete().Phase); // no-op when not in progress
    }

    [Fact]
    public void Fail_FromInProgress_Only()
    {
        var (state, _) = Available.Accept(1, 26);
        Assert.Equal(QuestPhase.Failed, state.Fail().Phase);
    }

    [Fact]
    public void ApplyVerdict_OnlyWhenApply_GrantCompletes_DenyFails()
    {
        var (inProgress, _) = Available.Accept(1, 26);

        Assert.Equal(QuestPhase.InProgress, inProgress.ApplyVerdict(apply: false, QuestRewardState.Grant).Phase);
        Assert.Equal(QuestPhase.Completed, inProgress.ApplyVerdict(apply: true, QuestRewardState.Grant).Phase);
        Assert.Equal(QuestPhase.Failed, inProgress.ApplyVerdict(apply: true, QuestRewardState.Deny).Phase);
    }

    [Fact]
    public void GiveUp_ClearsActiveQuest()
    {
        var (state, _) = Available.Accept(777, 26);

        QuestState givenUp = state.GiveUp();

        Assert.Equal(QuestPhase.GivenUp, givenUp.Phase);
        Assert.Equal(0u, givenUp.QuestId); // active quest cleared.
    }

    [Fact]
    public void GiveUp_RejectedWhenNotInProgress()
    {
        Assert.Equal(QuestPhase.Available, Available.GiveUp().Phase);
    }
}
