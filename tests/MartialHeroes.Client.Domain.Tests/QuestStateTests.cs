using MartialHeroes.Client.Domain.Quests;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class QuestStateTests
{
    private static QuestState Available => default; // Phase == Available, QuestId == 0.

    // The §4.3 gate proceeds when (bypass set) OR (level < 26); it blocks when (bypass clear) AND
    // (level >= 26). A level below the threshold is the accepting side. spec: Docs/RE/specs/quests.md §4.3/§10/§11.
    private const int BelowThreshold = 25; // < 26 → accept proceeds. spec: quests.md §4.3.
    private const int AtThreshold = 26; // >= 26 → blocked (absent bypass). spec: quests.md §4.3.

    [Fact]
    public void AcceptGate_ProceedsBelow26_BlocksAtOrAbove26()
    {
        Assert.Equal(26, QuestState.AcceptGateThreshold);

        // spec: quests.md §4.3 — send when level < 26; block when level >= 26 (bypass clear).
        Assert.True(QuestState.AcceptGatePasses(BelowThreshold));
        Assert.False(QuestState.AcceptGatePasses(AtThreshold));
        Assert.False(QuestState.AcceptGatePasses(99));
    }

    [Fact]
    public void AcceptGate_BillingBypass_OverridesLevelBlock()
    {
        // spec: quests.md §4.3 / §10 — the billing/account bypass forces the send through at any level.
        Assert.True(QuestState.AcceptGatePasses(AtThreshold, billingBypass: true));
        Assert.True(QuestState.AcceptGatePasses(99, billingBypass: true));
        Assert.True(QuestState.AcceptGatePasses(BelowThreshold, billingBypass: true));
    }

    [Fact]
    public void Accept_BelowThreshold_EntersInProgress()
    {
        var (next, accepted) = Available.Accept(questId: 500, level: BelowThreshold);

        Assert.True(accepted);
        Assert.Equal(QuestPhase.InProgress, next.Phase);
        Assert.Equal(500u, next.QuestId);
    }

    [Fact]
    public void Accept_AtThreshold_BlockedWithoutBypass()
    {
        // spec: quests.md §4.3 — level >= 26 with the bypass clear is the BLOCKING side (no 2/28).
        var (next, accepted) = Available.Accept(500, level: AtThreshold);

        Assert.False(accepted);
        Assert.Equal(QuestPhase.Available, next.Phase);
    }

    [Fact]
    public void Accept_AtThreshold_ProceedsWithBillingBypass()
    {
        // spec: quests.md §4.3 / §10 — the bypass lets the high-level accept through.
        var (next, accepted) = Available.Accept(500, level: AtThreshold, billingBypass: true);

        Assert.True(accepted);
        Assert.Equal(QuestPhase.InProgress, next.Phase);
        Assert.Equal(500u, next.QuestId);
    }

    [Fact]
    public void Accept_RejectedWhenNotAvailable()
    {
        var (state, _) = Available.Accept(1, BelowThreshold);
        Assert.False(state.Accept(2, BelowThreshold).Accepted); // already in progress
    }

    [Fact]
    public void Complete_FromInProgress_Only()
    {
        var (state, _) = Available.Accept(1, BelowThreshold);

        Assert.Equal(QuestPhase.Completed, state.Complete().Phase);
        Assert.Equal(QuestPhase.Available, Available.Complete().Phase); // no-op when not in progress
    }

    [Fact]
    public void Fail_FromInProgress_Only()
    {
        var (state, _) = Available.Accept(1, BelowThreshold);
        Assert.Equal(QuestPhase.Failed, state.Fail().Phase);
    }

    [Fact]
    public void ApplyVerdict_OnlyWhenApply_GrantCompletes_DenyFails()
    {
        var (inProgress, _) = Available.Accept(1, BelowThreshold);

        Assert.Equal(QuestPhase.InProgress, inProgress.ApplyVerdict(apply: false, QuestRewardState.Grant).Phase);
        Assert.Equal(QuestPhase.Completed, inProgress.ApplyVerdict(apply: true, QuestRewardState.Grant).Phase);
        Assert.Equal(QuestPhase.Failed, inProgress.ApplyVerdict(apply: true, QuestRewardState.Deny).Phase);
    }

    [Fact]
    public void GiveUp_ClearsActiveQuest()
    {
        var (state, _) = Available.Accept(777, BelowThreshold);

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