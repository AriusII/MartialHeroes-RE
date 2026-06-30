namespace MartialHeroes.Client.Domain.Quests.Quests;

public readonly record struct QuestCharacterContext(
    int Level,
    byte PlayerStance,
    int SecondaryStat,
    int TertiaryStat,
    int ClassIndex,
    uint ChapterProgress);