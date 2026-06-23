using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public interface ISkillTargetingQuery
{
    float CasterBodyRadius { get; }

    float BuffRangeBonus { get; }

    float SquaredPlanarDistanceToAim(in Vector3Fixed aimPoint);

    bool HasLineOfSight(in Vector3Fixed aimPoint);

    bool IsTargetStateValid(bool isReviveSkill);
}