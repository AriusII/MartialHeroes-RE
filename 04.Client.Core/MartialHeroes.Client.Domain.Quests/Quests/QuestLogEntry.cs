namespace MartialHeroes.Client.Domain.Quests.Quests;

public readonly record struct QuestLogEntry(uint QuestId, string Name, QuestProgressState State)
{
    public static readonly QuestLogEntry Empty = new(0, string.Empty, QuestProgressState.NotTracked);

    public bool IsEmpty => QuestId == 0;
}
