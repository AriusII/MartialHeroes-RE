namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// The complete, explicit input set for the recovered max-HP / max-MP formula
/// (<see cref="VitalFormula"/>). Pure value data; no platform state. spec: Docs/RE/structs/stats.md
/// ("Implementation guidance for the domain engineer", item 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Provisional / UNVERIFIED inputs (spec).</b>
/// <list type="bullet">
/// <item><see cref="LevelBaseHp"/>, <see cref="LevelBaseMp"/>, <see cref="ServerBaseHp"/> and
///   <see cref="ServerBaseMp"/> come from server / catalog data the legacy client received at
///   runtime; we do not have that data. They are injected and <b>default to 0</b>. A max computed
///   with the defaults is structurally correct but numerically incomplete — treat it as provisional
///   until a data file pins these (spec: stats.md "External inputs (UNVERIFIED)").</item>
/// <item><see cref="ClassId"/> is the raw class-id byte; its mapping to
///   <see cref="Shared.Kernel.Enums.CharacterClass"/> is UNVERIFIED (spec: stats.md).</item>
/// <item><see cref="EquipmentHpFlat"/> / <see cref="EquipmentMpFlat"/> are expected to already fold
///   in the two extra equip HP/MP bonus slots whose populating gear is UNVERIFIED (spec: stats.md
///   "equip_slot_hp / equip_slot_mp"); we model them as part of the equipment flat term so the
///   formula stays a pure function of resolved numbers.</item>
/// </list>
/// </para>
/// <para>
/// <b>Modeling choice (ours).</b> Equipment slot index 8 is skipped in the legacy accumulation
/// (spec: stats.md). That skip happens while summing worn items into the flat terms — i.e. <em>before</em>
/// this struct. The caller resolving <see cref="EquipmentHpFlat"/> / <see cref="EquipmentMpFlat"/>
/// (and the stat sums) must exclude slot 8; this struct receives the already-summed flat values.
/// </para>
/// </remarks>
public readonly record struct VitalFormulaInputs
{
    /// <summary>The five effective primary stats. spec: stats.md ("Primary stats (effective values)").</summary>
    public PrimaryStats Stats { get; init; }

    /// <summary>Raw class-id byte indexing <see cref="ClassHpTable"/>. UNVERIFIED mapping (spec: stats.md).</summary>
    public byte ClassId { get; init; }

    /// <summary>
    /// Flat equipment HP bonus: sum of every worn item's HP-grant field (slot 8 excluded by the
    /// caller), plus the two extra equip HP slots. spec: stats.md (Stage 2 HP base).
    /// </summary>
    public long EquipmentHpFlat { get; init; }

    /// <summary>
    /// Flat equipment MP bonus: sum of every worn item's MP-grant field (slot 8 excluded by the
    /// caller), plus the two extra equip MP slots. spec: stats.md (Stage 2 MP base).
    /// </summary>
    public long EquipmentMpFlat { get; init; }

    /// <summary>HP set bonus, applied only when <see cref="IsSetComplete"/> is true. spec: stats.md (set-bonus rule).</summary>
    public long SetBonusHp { get; init; }

    /// <summary>MP set bonus, applied only when <see cref="IsSetComplete"/> is true. spec: stats.md (set-bonus rule).</summary>
    public long SetBonusMp { get; init; }

    /// <summary>
    /// True only when every piece of the matched set is currently worn. A partial set grants no set
    /// bonus (all-or-nothing). spec: stats.md ("The set-bonus rule (all-or-nothing)").
    /// </summary>
    public bool IsSetComplete { get; init; }

    /// <summary>
    /// PROVISIONAL / UNVERIFIED. Externally-supplied HP level base (written on level-up). Defaults to
    /// <c>0</c> until a data file pins it. spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    public long LevelBaseHp { get; init; }

    /// <summary>
    /// PROVISIONAL / UNVERIFIED. Externally-supplied MP level base. Defaults to <c>0</c>.
    /// spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    public long LevelBaseMp { get; init; }

    /// <summary>
    /// PROVISIONAL / UNVERIFIED. Externally-supplied HP server base (server-overridden). Defaults to
    /// <c>0</c>. spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    public long ServerBaseHp { get; init; }

    /// <summary>
    /// PROVISIONAL / UNVERIFIED. Externally-supplied MP server base. Defaults to <c>0</c>.
    /// spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    public long ServerBaseMp { get; init; }

    /// <summary>
    /// Optional %HP buff slot ("slot81"), in whole percent. 0 if absent. Added to the HP multiplier
    /// as <c>value / 100.0</c>. spec: stats.md (Stage 3 HP, slot81_value / 100.0).
    /// </summary>
    public int HpPercentBuffPermille { get; init; }

    /// <summary>First active aura slot (companion). spec: stats.md ("Aura terms").</summary>
    public AuraTerm Aura0 { get; init; }

    /// <summary>Second active aura slot (secondary buff source). spec: stats.md ("Aura terms").</summary>
    public AuraTerm Aura1 { get; init; }

    /// <summary>Inputs with all-zero stats/bonuses, no auras, and the sentinel class id (0).</summary>
    public static VitalFormulaInputs Empty => new()
    {
        Stats = PrimaryStats.Zero,
        ClassId = 0,
        Aura0 = AuraTerm.None,
        Aura1 = AuraTerm.None,
    };
}