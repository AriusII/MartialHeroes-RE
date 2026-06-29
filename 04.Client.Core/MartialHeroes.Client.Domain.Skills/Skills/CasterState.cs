namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct CasterState
{
    public bool PartyRelationAllied { get; init; }

    public bool BillingRankOk { get; init; }

    public bool IsBusyCasting { get; init; }

    public bool IsMounted { get; init; }

    public bool MapModeForbidsCasting { get; init; }

    public bool IsStunnedOrSilenced { get; init; }

    public bool IsAlive { get; init; }

    public bool IsActionLocked { get; init; }

    public bool TargetInBlockingHostileState { get; init; }

    public bool WeaponRequirementSatisfied { get; init; }

    public bool SelfCastEligible { get; init; }

    public bool CastWindowOpen { get; init; }

    public bool HasTargets { get; init; }

    public bool HasResourceState { get; init; }

    public long CurrentHp { get; init; }

    public int CurrentStamina { get; init; }

    public long LastActionMs { get; init; }
}
