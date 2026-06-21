namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     The complete, explicit input set for the recovered max-HP / max-MP formula
///     (<see cref="VitalFormula" />). Pure value data; no platform state. spec: Docs/RE/structs/stats.md
///     ("Implementation guidance for the domain engineer", item 1).
/// </summary>
/// <remarks>
///     <para>
///         <b>Provisional / UNVERIFIED inputs (spec).</b>
///         <list type="bullet">
///             <item>
///                 <see cref="LevelBaseHp" />, <see cref="LevelBaseMp" />, <see cref="ServerBaseHp" /> and
///                 <see cref="ServerBaseMp" /> come from server / catalog data the legacy client received at
///                 runtime; we do not have that data. They are injected and <b>default to 0</b>. A max computed
///                 with the defaults is structurally correct but numerically incomplete — treat it as provisional
///                 until a data file pins these (spec: stats.md "External inputs (UNVERIFIED)").
///             </item>
///             <item>
///                 <see cref="ClassId" /> is the raw class-id byte; its mapping to
///                 <see cref="Shared.Kernel.Enums.CharacterClass" /> is UNVERIFIED (spec: stats.md).
///             </item>
///             <item>
///                 <see cref="EquipmentHpFlat" /> / <see cref="EquipmentMpFlat" /> are expected to already fold
///                 in the two extra equip HP/MP bonus slots whose populating gear is UNVERIFIED (spec: stats.md
///                 "equip_slot_hp / equip_slot_mp"); we model them as part of the equipment flat term so the
///                 formula stays a pure function of resolved numbers.
///             </item>
///         </list>
///     </para>
///     <para>
///         <b>Modeling choice (ours).</b> Equipment slot index 8 is skipped in the legacy accumulation
///         (spec: stats.md). That skip happens while summing worn items into the flat terms — i.e. <em>before</em>
///         this struct. The caller resolving <see cref="EquipmentHpFlat" /> / <see cref="EquipmentMpFlat" />
///         (and the stat sums) must exclude slot 8; this struct receives the already-summed flat values.
///     </para>
/// </remarks>
public readonly record struct VitalFormulaInputs
{
    /// <summary>The five effective primary stats. spec: stats.md ("Primary stats (effective values)").</summary>
    public PrimaryStats Stats { get; init; }

    /// <summary>Raw class-id byte indexing <see cref="ClassHpTable" />. UNVERIFIED mapping (spec: stats.md).</summary>
    public byte ClassId { get; init; }

    /// <summary>
    ///     Flat equipment HP bonus: sum of every worn item's HP-grant field (slot 8 excluded by the
    ///     caller), plus the two extra equip HP slots. spec: stats.md (Stage 2 HP base).
    /// </summary>
    public long EquipmentHpFlat { get; init; }

    /// <summary>
    ///     Flat equipment MP bonus: sum of every worn item's MP-grant field (slot 8 excluded by the
    ///     caller), plus the two extra equip MP slots. spec: stats.md (Stage 2 MP base).
    /// </summary>
    public long EquipmentMpFlat { get; init; }

    /// <summary>HP set bonus, applied only when <see cref="IsSetComplete" /> is true. spec: stats.md (set-bonus rule).</summary>
    public long SetBonusHp { get; init; }

    /// <summary>MP set bonus, applied only when <see cref="IsSetComplete" /> is true. spec: stats.md (set-bonus rule).</summary>
    public long SetBonusMp { get; init; }

    /// <summary>
    ///     True only when every piece of the matched set is currently worn. A partial set grants no set
    ///     bonus (all-or-nothing). spec: stats.md ("The set-bonus rule (all-or-nothing)").
    /// </summary>
    public bool IsSetComplete { get; init; }

    /// <summary>
    ///     PROVISIONAL / UNVERIFIED. Externally-supplied HP level base (written on level-up). Defaults to
    ///     <c>0</c> until a data file pins it. spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    /// <remarks>
    ///     This is a direct flat override. The preferred channel is now <see cref="LevelBaseHpCurve" />
    ///     looked up by <see cref="Level" />; this field is summed in addition, so a caller may use
    ///     either or both. Wave-7 unblock — see <see cref="StatBaseCurve" />.
    /// </remarks>
    public long LevelBaseHp { get; init; }

    /// <summary>
    ///     PROVISIONAL / UNVERIFIED. Externally-supplied MP level base. Defaults to <c>0</c>.
    ///     spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    /// <remarks>
    ///     Preferred channel is <see cref="LevelBaseMpCurve" /> looked up by <see cref="Level" />; this
    ///     flat field is summed in addition.
    /// </remarks>
    public long LevelBaseMp { get; init; }

    /// <summary>
    ///     Character level (1-based) used to index the injected level-base curves. Defaults to <c>0</c>;
    ///     combined with an empty curve this preserves the prior all-zero behaviour. When a curve is
    ///     supplied this must be the actor's current level. spec: Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    ///     WAVE-7 UNBLOCK. Injected per-level HP base curve (from <c>userlevel.scr</c>), looked up by
    ///     <see cref="Level" /> and added to the Stage-2 HP base. Defaults to the empty (all-zero) curve,
    ///     so an unset curve contributes nothing and matches the previous hard-coded <c>0</c>.
    ///     The Domain does not parse the curve; values are injected by Application. The curve byte
    ///     layout is still UNVERIFIED. spec: Docs/RE/formats/config_tables.md §2.4 / Known unknowns #2.
    /// </summary>
    public StatBaseCurve LevelBaseHpCurve { get; init; }

    /// <summary>
    ///     WAVE-7 UNBLOCK. Injected per-level MP base curve (from <c>userlevel.scr</c>), looked up by
    ///     <see cref="Level" /> and added to the Stage-2 MP base. Defaults to the empty (all-zero) curve.
    ///     spec: Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    public StatBaseCurve LevelBaseMpCurve { get; init; }

    /// <summary>
    ///     PROVISIONAL / UNVERIFIED. Externally-supplied HP server base (server-overridden). Defaults to
    ///     <c>0</c>. spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    public long ServerBaseHp { get; init; }

    /// <summary>
    ///     PROVISIONAL / UNVERIFIED. Externally-supplied MP server base. Defaults to <c>0</c>.
    ///     spec: stats.md ("External inputs (UNVERIFIED)").
    /// </summary>
    public long ServerBaseMp { get; init; }

    /// <summary>
    ///     Optional %HP buff slot ("slot81"), in whole percent. 0 if absent. Added to the HP multiplier
    ///     as <c>value / 100.0</c> (whole-percent, not per-mille). spec: stats.md (Stage 3 HP, slot81_value / 100.0).
    /// </summary>
    public int HpPercentBuffPercent { get; init; }

    /// <summary>First active aura slot (companion). spec: stats.md ("Aura terms").</summary>
    public AuraTerm Aura0 { get; init; }

    /// <summary>Second active aura slot (secondary buff source). spec: stats.md ("Aura terms").</summary>
    public AuraTerm Aura1 { get; init; }

    /// <summary>Inputs with all-zero stats/bonuses, no auras, empty curves, and the sentinel class id (0).</summary>
    public static VitalFormulaInputs Empty => new()
    {
        Stats = PrimaryStats.Zero,
        ClassId = 0,
        Aura0 = AuraTerm.None,
        Aura1 = AuraTerm.None,
        LevelBaseHpCurve = StatBaseCurve.Empty,
        LevelBaseMpCurve = StatBaseCurve.Empty
    };

    /// <summary>
    ///     Effective HP level base: the curve value at <see cref="Level" /> plus the flat
    ///     <see cref="LevelBaseHp" /> override. With the default empty curve and the default level/flat
    ///     of 0 this returns 0, matching the previous hard-coded behaviour.
    ///     spec: Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    public long ResolveLevelBaseHp()
    {
        return LevelBaseHpCurve.BaseForLevel(Level) + LevelBaseHp;
    }

    /// <summary>
    ///     Effective MP level base: the curve value at <see cref="Level" /> plus the flat
    ///     <see cref="LevelBaseMp" /> override. spec: Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    public long ResolveLevelBaseMp()
    {
        return LevelBaseMpCurve.BaseForLevel(Level) + LevelBaseMp;
    }
}