namespace MartialHeroes.Client.Domain.Quests.Quests;

public enum QuestObjectiveKind : byte
{
    Kill = 0,

    Collect = 1,

    Talk = 2
}

public readonly record struct QuestObjective
{
    public QuestObjectiveKind Kind { get; init; }

    public uint TargetId { get; init; }

    public int RequiredCount { get; init; }

    public int CurrentCount { get; init; }

    public bool IsComplete => CurrentCount >= RequiredCount;

    public QuestObjective Advance(uint targetId, int amount = 1)
    {
        if (amount <= 0 || targetId != TargetId || IsComplete) return this;

        var next = CurrentCount + amount;
        if (next > RequiredCount) next = RequiredCount;

        return this with { CurrentCount = next };
    }
}