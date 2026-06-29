namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct SkillTrainerGrant(bool Valid, int Rank, int LevelLow, int LevelHigh);

public static class SkillTrainerGate
{
    public const int JobBandLow = 2549;

    public const int JobBandHigh = 2553;

    public static SkillTrainerGrant Resolve(int trainerJobId)
    {
        return trainerJobId switch
        {
            2549 => new SkillTrainerGrant(true, 1, 2, 7),
            2550 => new SkillTrainerGrant(true, 2, 7, 12),
            2551 => new SkillTrainerGrant(true, 3, 12, 17),
            2552 => new SkillTrainerGrant(true, 4, 17, 21),
            2553 => new SkillTrainerGrant(true, 5, 21, 24),
            _ => new SkillTrainerGrant(false, 0, 0, 0)
        };
    }
}