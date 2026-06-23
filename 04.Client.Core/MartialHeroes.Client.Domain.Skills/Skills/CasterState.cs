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

    public int AvailableMp { get; init; }

    public bool CastWindowOpen { get; init; }

    public bool HasTargets { get; init; }

    public static CasterState AllClear => new()
    {
        PartyRelationAllied = true,
        BillingRankOk = true,
        IsBusyCasting = false,
        IsMounted = false,
        MapModeForbidsCasting = false,
        IsStunnedOrSilenced = false,
        IsAlive = true,
        IsActionLocked = false,
        TargetInBlockingHostileState = false,
        WeaponRequirementSatisfied = true,
        SelfCastEligible = true,
        AvailableMp = int.MaxValue,
        CastWindowOpen = true,
        HasTargets = true
    };
}