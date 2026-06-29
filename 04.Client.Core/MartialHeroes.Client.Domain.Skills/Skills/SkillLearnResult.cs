namespace MartialHeroes.Client.Domain.Skills.Skills;

public enum SkillLearnResult
{
    Ok = 0,

    UnknownTrainer = 1,

    LevelTooLow = 2,

    LevelTooHigh = 3,

    RankMismatch = 4,

    PrerequisiteMissing = 5
}