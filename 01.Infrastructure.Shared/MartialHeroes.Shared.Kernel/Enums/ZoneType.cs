namespace MartialHeroes.Shared.Kernel.Enums;

/// <summary>
///     The zone type assigned to a region cell, read from the zone-type field at record offset +40
///     in <c>regiontable&lt;area&gt;.bin</c>.
/// </summary>
/// <remarks>
///     <para>
///         This is a small <b>enumerated value</b>, not a packed bitmask — every consuming site in the
///         client performs an equality compare (<c>== 1</c>, <c>== 2</c>, <c>!= 0</c>), never a bit
///         test.
///         spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "not a packed bitmask — every consuming
///         site does an equality compare".
///     </para>
///     <para>
///         The enum is <b>complete at exactly three values</b> (0, 1, 2). A census of every consuming
///         site (movement gate, combat-mode arbiter, attack/target-validity gates, skill-use validator,
///         minimap switch) found no comparison against any value ≥ 3 and no bitmask test. Raw values
///         3..31 behave as the default arm (Safe) — they are not modelled as a named member.
///         spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "CONFIRMED-COMPLETE at three values;
///         3..31 = default (safe)".
///     </para>
///     <para>
///         A region id ≥ 32 has no record; the combat arbiter defaults to <see cref="OpenPvp" /> (1) in
///         that case.
///         spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "A missing record (region id >= 32) is
///         treated by the combat arbiter as type 1": CONFIRMED.
///     </para>
///     <para>
///         Confidence per value:
///         <list type="bullet">
///             <item><see cref="Safe" /> (0) — CONFIRMED (default / safe branch in minimap switch and arbiter).</item>
///             <item><see cref="OpenPvp" /> (1) — CONFIRMED (1 = combat-permitted).</item>
///             <item><see cref="Closed" /> (2) — CONFIRMED (2 = movement-restricted).</item>
///         </list>
///     </para>
/// </remarks>
public enum ZoneType : byte
{
    /// <summary>
    ///     Safe / no-combat zone — the combat arbiter yields the "denied" result; the minimap
    ///     renders a distinct safe caption/colour. Raw values ≥ 3 also resolve to this member
    ///     because no consuming site branches on any value ≥ 3.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 0: CONFIRMED (behaviour);
    ///     label PLAUSIBLE; 3..31 = default (Safe): CONFIRMED-COMPLETE.
    /// </summary>
    Safe = 0,

    /// <summary>
    ///     Open PvP / combat-enabled zone — combat is permitted (subject to the faction check).
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 1: CONFIRMED (1 = combat-permitted).
    /// </summary>
    OpenPvp = 1,

    /// <summary>
    ///     Movement-restricted / closed zone — entry or movement into a type-2 cell is denied; the
    ///     actor is snapped back and a localised message (id 74309) is shown.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 2: CONFIRMED (2 = movement-restricted).
    /// </summary>
    Closed = 2
}