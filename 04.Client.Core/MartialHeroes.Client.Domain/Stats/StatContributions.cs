namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// One per-stat buff contribution: a buff that adds <see cref="Value"/> to the stat identified by
/// <see cref="Key"/>. spec: Docs/RE/specs/combat.md §2 ("a per-stat buff term keyed by a primary buff
/// kind, and a shared all-stats buff term keyed by a single shared buff kind").
/// </summary>
/// <remarks>
/// A buff whose <see cref="Key"/> is <see cref="StatKey.AllStats"/> (93) contributes to <em>every</em>
/// primary stat (the shared all-stats term). spec: combat.md §2.2 (key 93).
/// </remarks>
public readonly record struct BuffContribution(StatKey Key, int Value);

/// <summary>
/// One per-item equipment grant for a single stat: the item's grant field value for
/// <see cref="Key"/>. The caller (Application/Assets) has already iterated the worn-equipment slot
/// table and applied the §2.1 slot skips before producing these; the Domain does not parse items.
/// spec: Docs/RE/specs/combat.md §2.1/§2.3.
/// </summary>
public readonly record struct EquipmentContribution(StatKey Key, int Value);

/// <summary>
/// One per-character modifier slot: a slot keyed by <see cref="Key"/> carrying <see cref="Value"/>.
/// When the running aggregation for a stat scans the modifier table, a slot whose key matches is
/// added. spec: Docs/RE/specs/combat.md §2.2.
/// </summary>
public readonly record struct ModifierSlotContribution(StatKey Key, int Value);

/// <summary>
/// One worn set-piece's bonus columns for a single stat, used by the all-or-nothing set distributor.
/// spec: Docs/RE/specs/combat.md §2.4.
/// </summary>
/// <remarks>
/// <para>
/// The set distributor runs in two phases (spec: combat.md §2.4):
/// <list type="number">
/// <item><b>Per-piece (always):</b> add <see cref="PerPieceBonus"/> for the matching <see cref="Key"/>.</item>
/// <item><b>Set-complete (gated):</b> if the count of worn pieces sharing <see cref="SetTypeId"/>
///   equals <see cref="RequiredPieceCount"/>, also add <see cref="SetCompleteBonus"/>.</item>
/// </list>
/// A partial set grants per-piece bonuses only; a complete set grants per-piece <b>plus</b> the
/// full-set bonus.
/// </para>
/// <para>
/// The per-stat <b>magnitudes</b> (<see cref="PerPieceBonus"/>, <see cref="SetCompleteBonus"/>) are
/// item catalogue data, not constants in this spec (spec: combat.md §2.4). The Domain receives them
/// already resolved.
/// </para>
/// </remarks>
public readonly record struct SetPieceContribution(
    int SetTypeId,
    int RequiredPieceCount,
    StatKey Key,
    int PerPieceBonus,
    int SetCompleteBonus);