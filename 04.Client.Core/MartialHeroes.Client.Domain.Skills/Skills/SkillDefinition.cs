using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct SkillDefinition
{
    public const ushort BasicAttackCategory = 1;

    public const ushort ReviveCategory = 14;

    public const ushort CooldownExemptCategory = 5;

    public const float MinEffectiveRange = 1.0f;

    public const int CastCadenceUnitMs = 100;

    public const short HpCostClientGuard = 30000;

    public required SkillId Id { get; init; }

    public required ushort Category { get; init; }

    public required SkillTargetMode TargetMode { get; init; }

    public uint GlobalCategory { get; init; }

    public byte TierByte { get; init; }

    public uint Prerequisite0 { get; init; }

    public uint Prerequisite1 { get; init; }

    public uint Prerequisite2 { get; init; }

    public float BaseRange { get; init; }

    public float AoeRadius { get; init; }

    public short MaxTargets { get; init; }

    public short CastCadenceFactor { get; init; }

    public ushort CooldownCentiseconds { get; init; }

    public ushort WeaponReqA { get; init; }

    public uint WeaponReqB { get; init; }

    public bool WeaponReqActive { get; init; }

    public short HpCost { get; init; }

    public ushort StaminaCost { get; init; }

    public uint CastEffectId { get; init; }

    public bool IsRevive => Category == ReviveCategory;

    public bool IsCastGateCooldownExempt => Category == BasicAttackCategory;

    public bool IsCooldownArmExempt => Category == CooldownExemptCategory;

    public int CadenceWindowMs => CastCadenceUnitMs * CastCadenceFactor;

    public int CooldownDurationMs => CooldownCentiseconds * 100;
}
