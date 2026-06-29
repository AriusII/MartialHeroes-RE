namespace MartialHeroes.Client.Domain.Quests.Quests;

public enum QuestGateResult
{
    Available,
    RecordNotFound,
    LevelTooLow,
    LevelTooHigh,
    NotAcceptedForSlot,
    WrongClassRace,
    SecondaryStatTooLow,
    SecondaryStatTooHigh,
    TertiaryStatFailed,
    SameCategoryActive,
    SameIdActive,
    PrerequisiteNotMet,
}
