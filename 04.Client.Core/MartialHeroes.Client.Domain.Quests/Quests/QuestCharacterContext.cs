namespace MartialHeroes.Client.Domain.Quests.Quests;

public readonly record struct QuestCharacterContext(
    int Level,
    byte ClassRaceMask,
    int SecondaryStat,
    int TertiaryStat,
    int AcceptedSlotIndex,
    bool PrerequisiteChainSatisfied);
