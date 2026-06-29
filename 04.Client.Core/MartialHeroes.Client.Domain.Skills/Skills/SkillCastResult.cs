namespace MartialHeroes.Client.Domain.Skills.Skills;

public enum SkillCastResult
{
    Ok = 0,

    BillingOrRank = 1,

    NotAlive = 2,

    TargetHostileState = 3,

    MountedOrUnresolved = 4,

    SelfCastIneligible = 5,

    NotEnoughMp = 6,

    Reason7 = 7,

    MoveCloser = 8,

    LineOfSightBlocked = 9,

    InvalidTarget = 10,

    CastWindowTiming = 11,

    NoTargets = 12,

    AlreadyCasting = 13,

    Reason14 = 14,

    Reason15 = 15,

    MapModeForbidden = 16,

    PartyRelation = 17,

    WeaponRequirement = 18,

    StunnedOrSilenced = 19,

    ActionLocked = 20,

    OnCooldown = 21,

    NotEnoughHp = 22,

    NotEnoughStamina = 23
}