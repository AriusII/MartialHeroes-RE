using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// A neutral, injected skill-catalog record: the per-skill data the cast pipeline, cooldown table and
/// effect dispatch read from <c>skills.scr</c>. The Domain never parses <c>skills.scr</c> — these
/// values are produced by the Assets/Application layer and handed in.
/// spec: Docs/RE/specs/skills.md §1 ("the meaning of the skills.scr skill-data fields") / §8.
/// </summary>
/// <remarks>
/// <para>
/// Fields and their meanings are the record-relative skill-data fields of §1.4. The on-disk record is
/// 1504 bytes with a trailing per-rank sub-table; this model carries only the decoded fields the cast
/// / cooldown / effect logic reads, not the raw bytes. The per-rank sub-rows (§1.3) are
/// <b>opaque</b> coefficient/duration data and are deliberately not modelled here until decoded
/// (open question 2). spec: skills.md §1.2, §1.3, §1.4.
/// </para>
/// <para>
/// <b>UNVERIFIED — cost split.</b> The spec keeps two distinct cost fields apart: <see cref="MpCostFactor"/>
/// (the cast-gate affordability factor, +1332) and <see cref="ConsumedCost"/> (subtracted on
/// cast-confirm, +1368). They must not be collapsed into one "cost" (open question 1).
/// spec: skills.md §1.4, §5.2.
/// </para>
/// </remarks>
public readonly record struct SkillDefinition
{
    /// <summary>The catalog skill id (lookup key). spec: skills.md §4 (skill-id mirror).</summary>
    public required SkillId Id { get; init; }

    /// <summary>
    /// Skill category / sort (+1306). Observed values: 1,2,3,5,6,7,11,14,17. <c>14</c> = REVIVE,
    /// <c>1</c> = basic-attack class. Categories 1 and 5 are cooldown-exempt. spec: skills.md §1.4 (+1306) / §4.
    /// </summary>
    public required ushort Category { get; init; }

    /// <summary>Per-rank value carried alongside the skill (rank field). spec: skills.md §1.4 / structs/skill.md.</summary>
    /// <remarks>Modelled as injected data; not used by the gate logic itself.</remarks>
    public ushort Rank { get; init; }

    /// <summary>Target-resolution / area shape (+1308). spec: skills.md §1.4 (+1308) / §3.</summary>
    public required SkillTargetMode TargetMode { get; init; }

    /// <summary>Base cast range / cone length (+1312, f32). spec: skills.md §1.4 (+1312) / §2.2.</summary>
    public float BaseRange { get; init; }

    /// <summary>AoE radius / cast distance (+1316, f32). spec: skills.md §1.4 (+1316) / §3.</summary>
    public float AoeRadius { get; init; }

    /// <summary>Base maximum target hit count (+1330, i16; clamped to 40). spec: skills.md §1.4 (+1330) / §3.</summary>
    public short MaxTargets { get; init; }

    /// <summary>
    /// Cast-gate MP affordability factor (+1332, i16). The MP gate fails when available MP
    /// <c>&lt; 100 × mp_cost_factor</c>. <b>Distinct</b> from <see cref="ConsumedCost"/>.
    /// spec: skills.md §1.4 (+1332) / §2.1 gate 14.
    /// </summary>
    public short MpCostFactor { get; init; }

    /// <summary>Cooldown in 1/100 s (+1334, u16). Per-slot cooldown ms = value × 100. spec: skills.md §1.4 (+1334) / §4.</summary>
    public ushort CooldownCentiseconds { get; init; }

    /// <summary>Weapon / stance requirement A (+1344, u16; 0 = none). spec: skills.md §1.4 (+1344) / §2.1 gate 11.</summary>
    public ushort WeaponReqA { get; init; }

    /// <summary>Secondary weapon / stance requirement id (+1348, u32). spec: skills.md §1.4 (+1348).</summary>
    public uint WeaponReqB { get; init; }

    /// <summary>Flag enabling the weapon-requirement check (+1352, u8). spec: skills.md §1.4 (+1352).</summary>
    public bool WeaponReqActive { get; init; }

    /// <summary>
    /// Cost subtracted on cast-confirm (+1368, i16). <b>UNVERIFIED</b> whether HP or MP/ki (open
    /// question 1); <b>distinct</b> from <see cref="MpCostFactor"/>. spec: skills.md §1.4 (+1368) / §5.2.
    /// </summary>
    public short ConsumedCost { get; init; }

    /// <summary>Stamina cost per target (+1370, u16). Total = value × targetCount × buffFactor, floored at value. spec: skills.md §1.4 (+1370) / §5.2.</summary>
    public ushort StaminaCost { get; init; }

    /// <summary>Per-skill cooldown duration in milliseconds (<see cref="CooldownCentiseconds"/> × 100). spec: skills.md §4 (duration = +1334 × 100).</summary>
    public int CooldownMs => CooldownCentiseconds * 100;

    /// <summary>True for the REVIVE category (14): the target must be dead to be valid. spec: skills.md §1.4 (+1306, 14 = REVIVE) / §3.1.</summary>
    public bool IsRevive => Category == ReviveCategory;

    /// <summary>
    /// True for the categories exempt from <b>arming</b> a cooldown (1 basic-attack, 5). Used by the
    /// cast-confirm arm path. spec: skills.md §4.1 note / §5.2 (categories 1 and 5 exempt from arming).
    /// </summary>
    public bool IsCooldownExempt => Category is BasicAttackCategory or CooldownArmExemptCategory;

    /// <summary>
    /// True when the skill is exempt from the <b>cast-gate</b> cooldown check (category 1 only). This is
    /// strictly narrower than <see cref="IsCooldownExempt"/>: gate 13 only lets the basic-attack
    /// category bypass an armed cooldown, so a category-5 skill that is still cooling is blocked at the
    /// gate even though it never armed a cooldown of its own.
    /// spec: skills.md §2.1 gate 13 ("not in the exempt category (category 1)").
    /// </summary>
    public bool IsCastGateCooldownExempt => Category == BasicAttackCategory;

    /// <summary>Basic-attack category. spec: skills.md §1.4 (+1306, category 1 = basic-attack).</summary>
    public const ushort BasicAttackCategory = 1;

    /// <summary>
    /// The second category (alongside basic-attack) exempt from <b>arming</b> a cooldown — but NOT
    /// exempt at the cast gate. spec: skills.md §4.1 note / §5.2 ("category … 5 … exempt from arming").
    /// </summary>
    public const ushort CooldownArmExemptCategory = 5;

    /// <summary>REVIVE category. spec: skills.md §1.4 (+1306, 14 = REVIVE).</summary>
    public const ushort ReviveCategory = 14;

    /// <summary>The combined per-cast hit cap. spec: skills.md §3 ("combined hit count is capped at 40") / §1.4 (max_targets clamped to 40).</summary>
    public const int MaxTargetCap = 40;

    /// <summary>Minimum effective range after adding the body radius / buff bonus. spec: skills.md §2.2 ("Clamp to a minimum of 1.0").</summary>
    public const float MinEffectiveRange = 1.0f;

    /// <summary>The MP gate multiplier: gate fails when available MP &lt; 100 × mp_cost_factor. spec: skills.md §1.4 (+1332) / §2.1 gate 14.</summary>
    public const int MpGateMultiplier = 100;
}