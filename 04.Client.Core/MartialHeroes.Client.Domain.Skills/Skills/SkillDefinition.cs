using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct SkillDefinition
{
    public const ushort BasicAttackCategory = 1;

    public const ushort CooldownArmExemptCategory = 5;

    public const ushort ReviveCategory = 14;

    public const int MaxTargetCap = 40;

    public const float MinEffectiveRange = 1.0f;

    public const int MpGateMultiplier = 100;

    public required SkillId Id { get; init; }

    public required ushort Category { get; init; }

    public ushort Rank { get; init; }

    public required SkillTargetMode TargetMode { get; init; }

    public float BaseRange { get; init; }

    public float AoeRadius { get; init; }

    public short MaxTargets { get; init; }

    public short MpCostFactor { get; init; }

    public ushort CooldownCentiseconds { get; init; }

    public ushort WeaponReqA { get; init; }

    public uint WeaponReqB { get; init; }

    public bool WeaponReqActive { get; init; }

    public short ConsumedCost { get; init; }

    public ushort StaminaCost { get; init; }

    public int CooldownMs => CooldownCentiseconds * 100;

    public bool IsRevive => Category == ReviveCategory;

    public bool IsCooldownExempt => Category is BasicAttackCategory or CooldownArmExemptCategory;

    public bool IsCastGateCooldownExempt => Category == BasicAttackCategory;
}