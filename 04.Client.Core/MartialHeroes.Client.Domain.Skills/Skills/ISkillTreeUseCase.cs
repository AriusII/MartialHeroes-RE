using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct SkillLearnEntry(SkillId Skill, short Rank);

public interface ISkillTreeUseCase
{
    SkillLearnResult RequestLearn(
        in SkillDefinition skill,
        int trainerJobId,
        int playerLevel,
        int playerRank,
        ISkillTreeQuery query,
        ReadOnlySpan<SkillLearnEntry> pending);

    bool RequestHotbarBind(
        int hotbarSlot,
        in SkillDefinition candidate,
        ReadOnlySpan<HotbarOccupant> occupants);
}
