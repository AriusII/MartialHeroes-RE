using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Domain.Inventory;

/// <summary>
/// The result of evaluating an equip / unequip / move request against the client-side gates.
/// spec: Docs/RE/specs/inventory_trade.md §4.2.
/// </summary>
public enum EquipCheckResult
{
    /// <summary>The request passes every client-side gate and may be sent. spec: inventory_trade.md §4.2.</summary>
    Allowed = 0,

    /// <summary>The item index is invalid (&lt; 0 / empty slot). spec: inventory_trade.md §4.2 gate 1.</summary>
    InvalidIndex = 1,

    /// <summary>A shared state gate failed (not in game / busy / dead). spec: inventory_trade.md §4.2 gate 2.</summary>
    StateBlocked = 2,

    /// <summary>The slot-8 relation guard rejected the move (message id 59003). spec: inventory_trade.md §4.2 gate 3.</summary>
    RelationGuardBlocked = 3,
}

/// <summary>
/// The shared client-side state predicate consulted before an equip / move send: in-game, not busy,
/// not dead. spec: Docs/RE/specs/inventory_trade.md §4.2 gate 2.
/// </summary>
public readonly record struct EquipStateGates
{
    /// <summary>The main handler's in-game flags are set. spec: inventory_trade.md §4.2 gate 2.</summary>
    public bool InGame { get; init; }

    /// <summary>A busy-check passes (the actor is not busy). spec: inventory_trade.md §4.2 gate 2.</summary>
    public bool NotBusy { get; init; }

    /// <summary>A not-dead check passes. spec: inventory_trade.md §4.2 gate 2.</summary>
    public bool NotDead { get; init; }

    /// <summary>True when all three state gates pass. spec: inventory_trade.md §4.2 gate 2.</summary>
    public bool AllPass => InGame && NotBusy && NotDead;

    /// <summary>A state with every gate passing.</summary>
    public static EquipStateGates AllClear => new() { InGame = true, NotBusy = true, NotDead = true };
}

/// <summary>
/// The inputs to the §4.2 equip-onto-other relation guard (the "slot-8 rejection"). The guard rejects
/// the move when both actors exist and are different, share the same non-zero relation-context id, and
/// the destination slot is 8. spec: Docs/RE/specs/inventory_trade.md §4.2 gate 3.
/// </summary>
/// <remarks>
/// The meaning of the relation-context value on a player actor is <c>UNVERIFIED</c> (party / couple /
/// duel context); it is taken as an opaque non-zero id here. spec: inventory_trade.md §4.2 / §11 #9.
/// </remarks>
public readonly record struct EquipRelationContext
{
    /// <summary>Both the slot actor and the slot-15 actor exist. spec: inventory_trade.md §4.2 gate 3.</summary>
    public bool BothActorsExist { get; init; }

    /// <summary>The two actors are different. spec: inventory_trade.md §4.2 gate 3.</summary>
    public bool ActorsAreDifferent { get; init; }

    /// <summary>The slot actor's relation-context id (opaque). spec: inventory_trade.md §4.2 gate 3.</summary>
    public int SlotActorContextId { get; init; }

    /// <summary>The slot-15 actor's relation-context id (opaque). spec: inventory_trade.md §4.2 gate 3.</summary>
    public int OtherActorContextId { get; init; }

    /// <summary>True when both share the same non-zero relation-context id. spec: inventory_trade.md §4.2 gate 3.</summary>
    public bool ShareSameNonZeroContext =>
        SlotActorContextId != 0 && SlotActorContextId == OtherActorContextId;
}

/// <summary>
/// Pure equipment rules: the client-side equip / unequip / move gates (the slot-8 relation guard and
/// the state gates) and the equipment-contribution recompute that feeds <see cref="StatAggregation"/>.
/// spec: Docs/RE/specs/inventory_trade.md §4 and Docs/RE/specs/combat.md §2.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Equip eligibility is server-authoritative.</b> No item level / class equip-requirement check
/// runs on the client send path; the only client gates are the state gates plus the slot-8 relation
/// guard (§4.2/§4.3). So <see cref="CheckEquip"/> models exactly those — it does <b>not</b> invent a
/// level/class gate. For a future authoritative server, <see cref="CheckRequirements"/> evaluates an
/// injected requirement against injected stats; the requirement thresholds are <b>data</b>, never
/// constants here. spec: inventory_trade.md §4.3 / §11 #4.
/// </para>
/// <para>
/// The §10 reserved-slot constants (15, 14, 8) live in <see cref="EquipSlots"/>.
/// </para>
/// </remarks>
public static class EquipRules
{
    /// <summary>
    /// Evaluates the client-side equip / unequip / move gates in order: valid index, state gates, then
    /// the slot-8 relation guard. spec: Docs/RE/specs/inventory_trade.md §4.2.
    /// </summary>
    /// <param name="itemIndex">The validated item / inventory index; must be ≥ 0. spec: §4.2 gate 1.</param>
    /// <param name="destinationSlot">The destination slot index. spec: §4.1 (`to`) / §4.2 gate 3.</param>
    /// <param name="state">The shared in-game / not-busy / not-dead predicate. spec: §4.2 gate 2.</param>
    /// <param name="relation">The relation-guard inputs (only consulted when the destination is slot 8). spec: §4.2 gate 3.</param>
    public static EquipCheckResult CheckEquip(
        int itemIndex,
        int destinationSlot,
        in EquipStateGates state,
        in EquipRelationContext relation)
    {
        // Gate 1: valid-index. The client will not send for an empty / invalid slot. spec: §4.2 gate 1.
        if (itemIndex < 0)
        {
            return EquipCheckResult.InvalidIndex;
        }

        // Gate 2: shared state gates. spec: §4.2 gate 2.
        if (!state.AllPass)
        {
            return EquipCheckResult.StateBlocked;
        }

        // Gate 3: equip-onto-other relation guard — only when the destination is slot 8. spec: §4.2 gate 3.
        if (destinationSlot == EquipSlots.StatExcludedSlot
            && relation.BothActorsExist
            && relation.ActorsAreDifferent
            && relation.ShareSameNonZeroContext)
        {
            return EquipCheckResult.RelationGuardBlocked;
        }

        return EquipCheckResult.Allowed;
    }

    /// <summary>
    /// Server-side / parity requirement check: true when <paramref name="stats"/> meet the injected
    /// <paramref name="requirement"/> (level + class + primary-stat minimums). Used by a future
    /// authoritative server; the client send path does <b>not</b> gate on this (§4.3). The requirement
    /// values are injected catalogue data, never invented. spec: Docs/RE/specs/inventory_trade.md §4.3.
    /// </summary>
    public static bool CheckRequirements(in EquipRequirement requirement, in EquipCandidateStats stats)
    {
        if (requirement.RequiredLevel > stats.Level)
        {
            return false;
        }

        if (requirement.RequiredClass is { } cls && cls != stats.Class)
        {
            return false;
        }

        return stats.Str >= requirement.RequiredStr
            && stats.Dex >= requirement.RequiredDex
            && stats.Agi >= requirement.RequiredAgi
            && stats.Con >= requirement.RequiredCon
            && stats.Int >= requirement.RequiredInt;
    }

    /// <summary>
    /// Recomputes the equipment stat-contribution rows for the worn-equipment slots, applying the
    /// §2.1 stat-sum slot skip (slot 8). The caller supplies one <see cref="EquipmentContribution"/> per
    /// (worn slot × granted stat) tagged with its source slot; this filters out the skipped slot so the
    /// result is ready for <see cref="StatAggregation"/>. spec: combat.md §2.1 / inventory_trade.md §1.2.
    /// </summary>
    /// <param name="wornBySlot">Per-slot equipment grants (slot index + the stat contribution).</param>
    /// <param name="destination">Caller-owned output span; must be at least <paramref name="wornBySlot"/>'s length.</param>
    /// <returns>The number of contributions written (the slot-8 entries are dropped).</returns>
    public static int RecomputeEquipmentContributions(
        ReadOnlySpan<SlottedEquipmentContribution> wornBySlot,
        Span<EquipmentContribution> destination)
    {
        int written = 0;
        for (int i = 0; i < wornBySlot.Length; i++)
        {
            SlottedEquipmentContribution entry = wornBySlot[i];

            // §2.1 skip rule: the non-stat slot (8) is excluded from the worn-item stat sum.
            if (EquipSlots.IsExcludedFromStatSum(entry.Slot))
            {
                continue;
            }

            if (written >= destination.Length)
            {
                throw new ArgumentException(
                    "Destination span is too small for the equipment contributions.", nameof(destination));
            }

            destination[written++] = entry.Contribution;
        }

        return written;
    }
}

/// <summary>
/// An injected per-item equip requirement (level / class / primary-stat minimums). The values are
/// catalogue data; the client does not enforce them (server-authoritative). A <c>null</c>
/// <see cref="RequiredClass"/> means "any class". spec: Docs/RE/specs/inventory_trade.md §4.3.
/// </summary>
public readonly record struct EquipRequirement
{
    /// <summary>Minimum character level. spec: inventory_trade.md §4.3 (server-authoritative).</summary>
    public ushort RequiredLevel { get; init; }

    /// <summary>Required class, or <c>null</c> for any. spec: inventory_trade.md §4.3.</summary>
    public Shared.Kernel.Enums.CharacterClass? RequiredClass { get; init; }

    /// <summary>Minimum STR. spec: inventory_trade.md §4.3.</summary>
    public int RequiredStr { get; init; }

    /// <summary>Minimum DEX. spec: inventory_trade.md §4.3.</summary>
    public int RequiredDex { get; init; }

    /// <summary>Minimum AGI. spec: inventory_trade.md §4.3.</summary>
    public int RequiredAgi { get; init; }

    /// <summary>Minimum CON. spec: inventory_trade.md §4.3.</summary>
    public int RequiredCon { get; init; }

    /// <summary>Minimum INT. spec: inventory_trade.md §4.3.</summary>
    public int RequiredInt { get; init; }
}

/// <summary>
/// The candidate wearer's stats evaluated against an <see cref="EquipRequirement"/>.
/// spec: Docs/RE/specs/inventory_trade.md §4.3.
/// </summary>
public readonly record struct EquipCandidateStats(
    ushort Level,
    Shared.Kernel.Enums.CharacterClass Class,
    int Str,
    int Dex,
    int Agi,
    int Con,
    int Int);

/// <summary>
/// An equipment contribution tagged with the worn slot it comes from, so the §2.1 stat-sum slot skip
/// can be applied during the equipment recompute. spec: combat.md §2.1 / inventory_trade.md §1.2.
/// </summary>
public readonly record struct SlottedEquipmentContribution(int Slot, EquipmentContribution Contribution);
