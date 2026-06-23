namespace MartialHeroes.Client.Domain.Skills.Skills;

public enum SkillTargetMode : byte
{
    SingleSelfOrPrimary = 0,

    SingleTarget = 1,

    SingleEnemyOrHeal = 2,

    ChainNearbyAoe = 3,

    ConeForwardAoe = 4,

    GroundPoint = 5,

    PartyAoe = 6,

    FactionGatedSingle = 7,

    PkGatedSingle = 9,

    RadialAoeBothFactions = 10,

    SelfOnly = 11
}