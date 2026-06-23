namespace MartialHeroes.Client.Domain.Quests.Quests;

public enum QuestPhase : byte
{
    Available = 0,

    InProgress = 1,

    Completed = 2,

    GivenUp = 3,

    Failed = 4
}

public enum QuestSubAction : byte
{
    Accept = 2,

    Proceed = 3,

    GiveUp = 4
}

public enum QuestRewardState : byte
{
    Grant = 1,

    Deny = 2
}

public readonly record struct QuestState
{
    public const int AcceptGateThreshold = 26;

    public uint QuestId { get; init; }

    public QuestPhase Phase { get; init; }

    public bool IsInProgress => Phase == QuestPhase.InProgress;

    public bool IsCompleted => Phase == QuestPhase.Completed;

    public static bool AcceptGatePasses(int level, bool billingBypass = false)
    {
        return billingBypass || level < AcceptGateThreshold;
    }

    public (QuestState Next, bool Accepted) Accept(uint questId, int level, bool billingBypass = false)
    {
        if (Phase != QuestPhase.Available || questId == 0 || !AcceptGatePasses(level, billingBypass))
            return (this, false);

        return (new QuestState { QuestId = questId, Phase = QuestPhase.InProgress }, true);
    }

    public QuestState Complete()
    {
        return Phase == QuestPhase.InProgress ? this with { Phase = QuestPhase.Completed } : this;
    }

    public QuestState Fail()
    {
        return Phase == QuestPhase.InProgress ? this with { Phase = QuestPhase.Failed } : this;
    }

    public QuestState ApplyVerdict(bool apply, QuestRewardState rewardState)
    {
        if (!apply) return this;

        return rewardState switch
        {
            QuestRewardState.Grant => Complete(),
            QuestRewardState.Deny => Fail(),
            _ => this
        };
    }

    public QuestState GiveUp()
    {
        return Phase == QuestPhase.InProgress ? new QuestState { QuestId = 0, Phase = QuestPhase.GivenUp } : this;
    }
}