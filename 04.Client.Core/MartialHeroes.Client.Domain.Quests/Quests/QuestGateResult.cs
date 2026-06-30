namespace MartialHeroes.Client.Domain.Quests.Quests;

public enum QuestGateResult
{
    Available,
    RecordNotFound,
    LevelTooLow,
    LevelTooHigh,
    NotAcceptedForClass,
    WrongStance,
    SecondaryStatTooLow,
    SecondaryStatTooHigh,
    TertiaryStatFailed,
    SameCategoryActive,
    SameIdActive,
    InChain
}