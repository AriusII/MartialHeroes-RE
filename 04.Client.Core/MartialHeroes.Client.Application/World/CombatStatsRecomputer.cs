using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Application.World;

/// <summary>
/// Orchestration shim that recomposes the local player's derived combat-stat aggregate when an input
/// changes (equip / buff / level). It assembles the already-resolved contribution lists, runs the Domain
/// <see cref="StatAggregation"/> + <see cref="CombatFormula"/> + <see cref="BuffStatBridge"/>, and returns
/// the recomputed <see cref="CombatStats"/>. spec: Docs/RE/specs/combat.md §1 / §2 (fully cleared and
/// re-accumulated on input change).
/// </summary>
/// <remarks>
/// <para>
/// <b>No game-rule math lives here.</b> Every formula (primary-stat aggregation, the attack/hit-rating
/// composition, the buff-to-contribution bridge, the §2.1 equip slot skip) is a Domain call; this shim
/// only marshals inputs into the spans those methods take and copies the results into the immutable
/// aggregate. The numeric magnitudes (server bases, equipment grants, buff strengths) are
/// <b>injected data</b> — the Application/Assets layer resolves them; the Domain never parses items.
/// spec: combat.md §2 / §8.
/// </para>
/// <para>
/// <b>Allocation.</b> The contribution spans are stack-allocated from the caller-supplied input arrays,
/// so a recompute on the (low-rate) equip/buff/level change path does not churn the GC.
/// </para>
/// </remarks>
public static class CombatStatsRecomputer
{
    /// <summary>
    /// Recomposes the derived combat-stat aggregate from the supplied, already-resolved inputs. The
    /// active buff stat grants are bridged through <see cref="BuffStatBridge"/>; equipment grants have the
    /// §2.1 slot-8 skip applied via <see cref="EquipRules.RecomputeEquipmentContributions"/>; the five
    /// effective primary stats are aggregated and fed into the rating composition.
    /// spec: Docs/RE/specs/combat.md §1 / §2 / §2.1 / §3.
    /// </summary>
    /// <param name="serverBases">The five server-supplied primary-stat bases (from the 4/29 / 5/67 sync). spec: combat.md §2/§6.</param>
    /// <param name="buffGrants">Active buff slots paired with the stat they grant (data-supplied). spec: combat.md §2.2 / skills.md §6.</param>
    /// <param name="wornEquipment">Per-slot equipment grants (slot index + stat contribution). spec: combat.md §2.1/§2.3.</param>
    /// <param name="setPieces">Worn set-piece contributions for the all-or-nothing set distributor. spec: combat.md §2.4.</param>
    /// <param name="modifierSlots">Per-character modifier slots scanned by stat key. spec: combat.md §2.2.</param>
    /// <param name="ratingTerms">The injected, non-stat rating composition terms (weapon/level/grade/slots). spec: combat.md §3.3/§3.4.</param>
    /// <returns>The recomputed derived combat-stat aggregate.</returns>
    public static CombatStats Recompute(
        in PrimaryStatServerBases serverBases,
        ReadOnlySpan<BuffStatGrant> buffGrants,
        ReadOnlySpan<SlottedEquipmentContribution> wornEquipment,
        ReadOnlySpan<SetPieceContribution> setPieces,
        ReadOnlySpan<ModifierSlotContribution> modifierSlots,
        in CombatRatingTerms ratingTerms = default)
    {
        // Bridge active buffs into per-stat buff contributions (Domain owns the active-while-positive rule).
        // spec: combat.md §2.2 / skills.md §6.3.
        Span<BuffContribution> buffs = buffGrants.Length <= 64
            ? stackalloc BuffContribution[buffGrants.Length]
            : new BuffContribution[buffGrants.Length];
        int buffCount = BuffStatBridge.BuildContributions(buffGrants, buffs);
        buffs = buffs[..buffCount];

        // Apply the §2.1 stat-sum slot skip (slot 8) to the worn equipment grants. spec: combat.md §2.1.
        Span<EquipmentContribution> equipment = wornEquipment.Length <= 64
            ? stackalloc EquipmentContribution[wornEquipment.Length]
            : new EquipmentContribution[wornEquipment.Length];
        int equipCount = EquipRules.RecomputeEquipmentContributions(wornEquipment, equipment);
        equipment = equipment[..equipCount];

        // Aggregate the five effective primary stats (server base + buffs + equip + set + modifiers).
        // spec: combat.md §2.
        PrimaryStats primary = StatAggregation.AggregatePrimaryStats(
            in serverBases, buffs, equipment, setPieces, modifierSlots);

        // Compose the two character-sheet ratings via the recovered formulas. spec: combat.md §3.3/§3.4.
        AttackRatingInputs attackInputs = ratingTerms.ToAttackInputs(primary);
        HitRatingInputs hitInputs = ratingTerms.ToHitInputs(primary);

        int attackRating = CombatFormula.AttackRating(in attackInputs);
        int hitRating = CombatFormula.HitRating(in hitInputs);

        return CombatStats.Empty with
        {
            Str = primary.Str,
            Dex = primary.Dex,
            Agil = primary.Agi,
            Vital = primary.Con,
            Inte = primary.Int,
            AttackRating = attackRating,
            HitRating = hitRating,
        };
    }
}

/// <summary>
/// The injected, non-stat terms feeding the §3.3 / §3.4 rating composition (weapon / level / grade /
/// modifier slots). These are item-catalogue / actor data the Application/Assets layer resolves; the
/// Domain does not parse them. All default to 0 (the no-equipment baseline). spec: combat.md §3.3 / §3.4.
/// </summary>
public readonly record struct CombatRatingTerms
{
    /// <summary>Modifier slot key 15 (attack-rating hit term). spec: combat.md §3.3.</summary>
    public int Slot15 { get; init; }

    /// <summary>Modifier slot key 94 (attack-rating hit term). spec: combat.md §3.3.</summary>
    public int Slot94 { get; init; }

    /// <summary>Modifier slot key 5 (attack-rating hit term). spec: combat.md §3.3.</summary>
    public int Slot5 { get; init; }

    /// <summary>Modifier slot key 16 (hit-rating accuracy term). spec: combat.md §3.4.</summary>
    public int Slot16 { get; init; }

    /// <summary>Modifier slot key 20 (hit-rating accuracy term). spec: combat.md §3.4.</summary>
    public int Slot20 { get; init; }

    /// <summary>Modifier slot key 83 (hit % multiplier, applied as (value − 100) %). spec: combat.md §2.2/§3.3.</summary>
    public int Slot83 { get; init; }

    /// <summary>Modifier slot key 61 (final flat add). spec: combat.md §3.3.</summary>
    public int Slot61 { get; init; }

    /// <summary>Per-weapon integer lookup term. spec: combat.md §3.3/§3.4.</summary>
    public int WeaponTerm { get; init; }

    /// <summary>Weapon-grade helper (contributes grade × 0.1). spec: combat.md §3.3/§3.4.</summary>
    public int WeaponGrade { get; init; }

    /// <summary>Two-field attack accumulator summed over worn gear. spec: combat.md §2.3/§3.3.</summary>
    public int DamageEquipSum { get; init; }

    /// <summary>Defence-family accuracy accumulator summed over worn gear. spec: combat.md §2.3/§3.4.</summary>
    public int AccuracyEquipSum { get; init; }

    /// <summary>Level/grade byte (contributes level × 0.5). spec: combat.md §3.3/§3.4.</summary>
    public int LevelTerm { get; init; }

    /// <summary>Class/grade byte (+2.0 when ≥ 8). spec: combat.md §3.3/§3.4.</summary>
    public int GradeByte { get; init; }

    /// <summary>Opaque weapon-proficiency penalty key for the hit-rating penalty. spec: combat.md §4.</summary>
    public int ProficiencyKey { get; init; }

    /// <summary>Rank-progress gate adding a second +300 baseline to the hit rating. spec: combat.md §3.4.</summary>
    public bool RankProgressGate { get; init; }

    /// <summary>Projects these terms plus the aggregated <paramref name="stats"/> into the attack-rating inputs. spec: combat.md §3.3.</summary>
    public AttackRatingInputs ToAttackInputs(in PrimaryStats stats) => new()
    {
        Stats = stats,
        Slot15 = Slot15,
        Slot94 = Slot94,
        Slot5 = Slot5,
        WeaponTerm = WeaponTerm,
        WeaponGrade = WeaponGrade,
        DamageEquipSum = DamageEquipSum,
        LevelTerm = LevelTerm,
        GradeByte = GradeByte,
        Slot83 = Slot83,
        Slot61 = Slot61,
    };

    /// <summary>Projects these terms plus the aggregated <paramref name="stats"/> into the hit-rating inputs. spec: combat.md §3.4.</summary>
    public HitRatingInputs ToHitInputs(in PrimaryStats stats) => new()
    {
        Stats = stats,
        Slot16 = Slot16,
        Slot20 = Slot20,
        WeaponTerm = WeaponTerm,
        WeaponGrade = WeaponGrade,
        AccuracyEquipSum = AccuracyEquipSum,
        LevelTerm = LevelTerm,
        GradeByte = GradeByte,
        // Resolve the opaque proficiency key into a percent via the Domain banding table. spec: combat.md §4.
        ProficiencyPenaltyPercent = CombatFormula.WeaponProficiencyPenaltyPercent(ProficiencyKey),
        RankProgressGate = RankProgressGate,
    };
}