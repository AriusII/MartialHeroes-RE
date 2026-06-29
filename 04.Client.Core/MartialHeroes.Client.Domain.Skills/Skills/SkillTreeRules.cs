namespace MartialHeroes.Client.Domain.Skills.Skills;

public static class SkillTreeRules
{
    public static bool MeetsPrerequisites(in SkillDefinition skill, ISkillTreeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (skill.Prerequisite0 == 0 && skill.Prerequisite1 == 0 && skill.Prerequisite2 == 0)
            return true;

        if (skill.Prerequisite0 != 0 && query.OwnsSkill(skill.Prerequisite0)) return true;

        if (skill.Prerequisite1 != 0 && query.OwnsSkill(skill.Prerequisite1)) return true;

        if (skill.Prerequisite2 != 0 && query.OwnsSkill(skill.Prerequisite2)) return true;

        return false;
    }

    public static SkillLearnResult CanLearn(
        in SkillDefinition skill,
        int trainerJobId,
        int playerLevel,
        int playerRank,
        ISkillTreeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var grant = SkillTrainerGate.Resolve(trainerJobId);
        if (!grant.Valid) return SkillLearnResult.UnknownTrainer;

        if (playerLevel < grant.LevelLow) return SkillLearnResult.LevelTooLow;

        if (playerLevel >= grant.LevelHigh) return SkillLearnResult.LevelTooHigh;

        if (playerRank != grant.Rank) return SkillLearnResult.RankMismatch;

        if (!MeetsPrerequisites(in skill, query)) return SkillLearnResult.PrerequisiteMissing;

        return SkillLearnResult.Ok;
    }
}
