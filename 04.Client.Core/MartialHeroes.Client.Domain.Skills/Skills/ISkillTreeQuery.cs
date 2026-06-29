namespace MartialHeroes.Client.Domain.Skills.Skills;

public interface ISkillTreeQuery
{
    bool OwnsSkill(uint skillId);
}
